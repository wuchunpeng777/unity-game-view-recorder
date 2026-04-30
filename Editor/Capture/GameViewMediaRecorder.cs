using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEditor.Media;
using UnityEngine;
using GameViewRecorder.Runtime;
using Debug = UnityEngine.Debug;

namespace GameViewRecorder.Editor.Capture
{
    internal sealed class GameViewMediaRecorder : IDisposable
    {
        private readonly GameViewNativeFrameCapture _nativeCapture = new GameViewNativeFrameCapture();

        private MediaEncoder _encoder;
        private NativeArray<float> _audioNativeBuffer;
        private float[] _audioManagedBuffer;
        private byte[] _audioByteBuffer;
        private byte[] _frameByteBuffer;
        private int _ffmpegAudioSampleCount;
        private Process _ffmpegProcess;
        private Stream _ffmpegInput;
        private FileStream _ffmpegAudioFile;
        private StringBuilder _ffmpegErrors;
        private bool _usingFfmpeg;
        private string _outputPath;
        private string _tempVideoPath;
        private string _tempAudioPath;
        private bool _recordAudio;
        private bool _usingFmodAudio;
        private bool _audioCaptureEnded;
        private int _width;
        private int _height;
        private int _frameRate;
        private int _audioSampleRate;
        private int _audioChannelCount;

        public void Start(string outputPath, int width, int height, int frameRate, bool recordAudio)
        {
            _width = width;
            _height = height;
            _recordAudio = recordAudio;
            _usingFmodAudio = false;
            _audioCaptureEnded = false;
            _ffmpegAudioSampleCount = 0;
            _outputPath = outputPath;
            _frameRate = frameRate;
            _audioSampleRate = AudioSettings.outputSampleRate;
            _audioChannelCount = 2;

            if (TryStartFfmpeg(outputPath, width, height, frameRate, recordAudio))
            {
                return;
            }

            var videoAttributes = new VideoTrackAttributes
            {
                frameRate = new MediaRational(frameRate),
                width = (uint)width,
                height = (uint)height,
                includeAlpha = false
            };
            TrySetHighBitrateMode(ref videoAttributes);

            if (_recordAudio)
            {
                var audioAttributes = new AudioTrackAttributes
                {
                    sampleRate = new MediaRational(_audioSampleRate),
                    channelCount = (ushort)_audioChannelCount,
                    language = "und"
                };

                int audioSamplesPerFrame = audioAttributes.channelCount * audioAttributes.sampleRate.numerator / frameRate;
                _audioManagedBuffer = new float[audioSamplesPerFrame];
                _audioNativeBuffer = new NativeArray<float>(audioSamplesPerFrame, Allocator.Persistent);
                _encoder = new MediaEncoder(outputPath, videoAttributes, audioAttributes);
                GameViewRecorderAudioTap.BeginCapture();
            }
            else
            {
                _encoder = new MediaEncoder(outputPath, videoAttributes);
            }
        }

        public void AddFrame(GameViewCaptureArea area, bool includeCursor, int repeatCount)
        {
            if (_encoder == null && !_usingFfmpeg)
            {
                throw new InvalidOperationException("录制器尚未启动。");
            }

            if (repeatCount <= 0)
            {
                return;
            }

            if (!area.HasNativeSize)
            {
                throw new InvalidOperationException("当前 GameView 未提供原画分辨率，无法录制。");
            }

            if (area.NativeWidth != _width || area.NativeHeight != _height)
            {
                throw new InvalidOperationException("录制过程中 GameView 原画分辨率发生变化，请停止后重新开始录制。");
            }

            var frame = _nativeCapture.Capture(area, includeCursor);
            if (_usingFfmpeg)
            {
                WriteFfmpegFrame(frame, repeatCount);
                return;
            }

            for (int i = 0; i < repeatCount; i++)
            {
                _encoder.AddFrame(frame);

                if (_recordAudio)
                {
                    GameViewRecorderAudioTap.Fill(_audioManagedBuffer);
                    _audioNativeBuffer.CopyFrom(_audioManagedBuffer);
                    _encoder.AddSamples(_audioNativeBuffer);
                }
            }
        }

        private bool TryStartFfmpeg(string outputPath, int width, int height, int frameRate, bool recordAudio)
        {
            string videoOutputPath = outputPath;
            if (recordAudio)
            {
                string outputFolder = Path.GetDirectoryName(outputPath);
                string outputName = Path.GetFileNameWithoutExtension(outputPath);
                _tempVideoPath = Path.Combine(outputFolder, outputName + ".video.tmp.mp4");
                _tempAudioPath = Path.Combine(outputFolder, outputName + ".audio.tmp.f32");
                videoOutputPath = _tempVideoPath;
                DeleteIfExists(_tempVideoPath);
                DeleteIfExists(_tempAudioPath);
            }

            var arguments = string.Format(
                "-y -hide_banner -loglevel error -f rawvideo -pix_fmt rgba -s {0}x{1} -r {2} -i - -an -c:v libx264 -preset slow -crf 12 -pix_fmt yuv420p -movflags +faststart \"{3}\"",
                width,
                height,
                frameRate,
                videoOutputPath);

            try
            {
                _ffmpegErrors = new StringBuilder();
                _ffmpegProcess = StartProcess("ffmpeg", arguments, true);
                if (_ffmpegProcess == null)
                {
                    throw new InvalidOperationException("无法创建 FFmpeg 进程。");
                }

                _ffmpegProcess.ErrorDataReceived += OnFfmpegErrorDataReceived;
                _ffmpegInput = _ffmpegProcess.StandardInput.BaseStream;
                _ffmpegProcess.BeginErrorReadLine();
                _usingFfmpeg = true;

                if (recordAudio)
                {
                    _ffmpegAudioFile = new FileStream(_tempAudioPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                    if (GameViewRecorderFmodAudioTap.BeginCapture(out int fmodSampleRate, out int fmodChannelCount))
                    {
                        _usingFmodAudio = true;
                        _audioSampleRate = fmodSampleRate;
                        _audioChannelCount = fmodChannelCount;
                    }
                    else
                    {
                        GameViewRecorderAudioTap.BeginCapture();
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                CleanupFfmpeg();
                Debug.LogWarning("GameView Recorder: 无法启动 FFmpeg 高质量编码，回退到 Unity MediaEncoder。原因：" + exception.Message);
                return false;
            }
        }

        private void WriteFfmpegFrame(Texture2D frame, int repeatCount)
        {
            var rawFrame = frame.GetRawTextureData<byte>();
            int rowBytes = _width * 4;
            int frameBytes = rowBytes * _height;
            if (_frameByteBuffer == null || _frameByteBuffer.Length != frameBytes)
            {
                _frameByteBuffer = new byte[frameBytes];
            }

            for (int y = 0; y < _height; y++)
            {
                NativeArray<byte>.Copy(rawFrame, (_height - 1 - y) * rowBytes, _frameByteBuffer, y * rowBytes, rowBytes);
            }

            for (int i = 0; i < repeatCount; i++)
            {
                _ffmpegInput.Write(_frameByteBuffer, 0, _frameByteBuffer.Length);
            }

            if (_recordAudio)
            {
                DrainFfmpegAudio();
            }
        }

        private void DrainFfmpegAudio()
        {
            while (TryDequeueAudio(out var audioBuffer))
            {
                int byteCount = audioBuffer.Length * sizeof(float);
                if (_audioByteBuffer == null || _audioByteBuffer.Length < byteCount)
                {
                    _audioByteBuffer = new byte[byteCount];
                }

                Buffer.BlockCopy(audioBuffer, 0, _audioByteBuffer, 0, byteCount);
                _ffmpegAudioFile.Write(_audioByteBuffer, 0, byteCount);
                _ffmpegAudioSampleCount += audioBuffer.Length;
            }
        }

        private bool TryDequeueAudio(out float[] audioBuffer)
        {
            return _usingFmodAudio
                ? GameViewRecorderFmodAudioTap.TryDequeue(out audioBuffer)
                : GameViewRecorderAudioTap.TryDequeue(out audioBuffer);
        }

        private void EndAudioCapture()
        {
            if (!_recordAudio || _audioCaptureEnded)
            {
                return;
            }

            if (_usingFmodAudio)
            {
                GameViewRecorderFmodAudioTap.EndCapture();
            }
            else
            {
                GameViewRecorderAudioTap.EndCapture();
            }

            _audioCaptureEnded = true;
        }

        private void FinishFfmpeg()
        {
            try
            {
                _ffmpegInput?.Dispose();
                _ffmpegInput = null;

                if (_ffmpegProcess != null && !_ffmpegProcess.WaitForExit(30000))
                {
                    _ffmpegProcess.Kill();
                    Debug.LogWarning("GameView Recorder: FFmpeg 视频编码超时，已终止进程。");
                }
                else if (_ffmpegProcess != null && _ffmpegProcess.ExitCode != 0)
                {
                    Debug.LogWarning("GameView Recorder: FFmpeg 视频编码失败。原因：" + _ffmpegErrors);
                }

                if (_recordAudio)
                {
                    DrainFfmpegAudio();
                    EndAudioCapture();
                    _ffmpegAudioFile?.Dispose();
                    _ffmpegAudioFile = null;
                    MuxFfmpegAudio();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("GameView Recorder: FFmpeg 收尾失败，输出可能不完整。原因：" + exception.Message);
            }
            finally
            {
                CleanupFfmpeg();
                DeleteIfExists(_tempAudioPath);
                DeleteIfExists(_tempVideoPath);
            }
        }

        private void MuxFfmpegAudio()
        {
            if (string.IsNullOrEmpty(_tempVideoPath)
                || string.IsNullOrEmpty(_tempAudioPath)
                || !File.Exists(_tempVideoPath)
                || !File.Exists(_tempAudioPath))
            {
                return;
            }

            if (_ffmpegAudioSampleCount <= 0)
            {
                File.Copy(_tempVideoPath, _outputPath, true);
                Debug.LogWarning("GameView Recorder: 未采集到游戏音频，已保留无音频视频。请确认场景中有启用的 AudioListener 且 Game 视图能听到声音。");
                return;
            }

            DeleteIfExists(_outputPath);
            string arguments = string.Format(
                "-y -hide_banner -loglevel error -i \"{0}\" -f f32le -ar {1} -ac {2} -i \"{3}\" -c:v copy -c:a aac -b:a 192k -shortest -movflags +faststart \"{4}\"",
                _tempVideoPath,
                _audioSampleRate,
                _audioChannelCount,
                _tempAudioPath,
                _outputPath);

            using (var process = StartProcess("ffmpeg", arguments, false))
            {
                if (process == null)
                {
                    File.Copy(_tempVideoPath, _outputPath, true);
                    Debug.LogWarning("GameView Recorder: 无法创建 FFmpeg 音频封装进程，已保留无音频视频。");
                    return;
                }

                string errors = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    File.Copy(_tempVideoPath, _outputPath, true);
                    Debug.LogWarning("GameView Recorder: FFmpeg 音频封装失败，已保留无音频视频。原因：" + errors);
                }
            }
        }

        private static Process StartProcess(string fileName, string arguments, bool redirectInput)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardInput = redirectInput,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            return Process.Start(startInfo);
        }

        private void OnFfmpegErrorDataReceived(object sender, DataReceivedEventArgs args)
        {
            if (!string.IsNullOrEmpty(args.Data))
            {
                _ffmpegErrors.AppendLine(args.Data);
            }
        }

        private void CleanupFfmpeg()
        {
            if (_ffmpegProcess != null)
            {
                _ffmpegProcess.ErrorDataReceived -= OnFfmpegErrorDataReceived;
                _ffmpegProcess.Dispose();
                _ffmpegProcess = null;
            }

            _ffmpegInput = null;
            _ffmpegAudioFile?.Dispose();
            _ffmpegAudioFile = null;
            _usingFfmpeg = false;
        }

        private static void DeleteIfExists(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static void TrySetHighBitrateMode(ref VideoTrackAttributes attributes)
        {
            var property = typeof(VideoTrackAttributes).GetProperty("bitRateMode");
            if (property == null || !property.CanWrite || !property.PropertyType.IsEnum)
            {
                return;
            }

            try
            {
                var boxedAttributes = (object)attributes;
                property.SetValue(boxedAttributes, Enum.Parse(property.PropertyType, "High"), null);
                attributes = (VideoTrackAttributes)boxedAttributes;
            }
            catch
            {
                // 部分 Unity 版本未公开该枚举值，保留默认编码质量继续录制。
            }
        }

        public void Dispose()
        {
            if (_usingFfmpeg)
            {
                FinishFfmpeg();
                EndAudioCapture();

                _nativeCapture.Dispose();
                return;
            }

            EndAudioCapture();

            if (_audioNativeBuffer.IsCreated)
            {
                _audioNativeBuffer.Dispose();
            }

            _encoder?.Dispose();
            _encoder = null;
            _nativeCapture.Dispose();
        }
    }
}
