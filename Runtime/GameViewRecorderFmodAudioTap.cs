// ================================================================
// 本文件由 AI 自动生成
// 生成工具: GPT-5.5
// 作者: 邬春鹏
// 生成时间: 2026-04-30 14:12:00
// ================================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using FMODUnity;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GameViewRecorder.Runtime
{
    public static class GameViewRecorderFmodAudioTap
    {
        private const uint FmodPluginSdkVersion = 110;
        private const int OutputChannelCount = 2;
        private const int MaxPendingBuffers = 240;

        private static readonly object SyncRoot = new object();
        private static readonly Queue<float[]> PendingBuffers = new Queue<float[]>();
        private static readonly FMOD.DSP_READ_CALLBACK ReadCallback = OnDspRead;

        private static FMOD.ChannelGroup _masterGroup;
        private static FMOD.DSP _dsp;
        private static bool _isCapturing;

        public static bool BeginCapture(out int sampleRate, out int channelCount)
        {
            EndCapture();

            sampleRate = AudioSettings.outputSampleRate;
            channelCount = OutputChannelCount;

            try
            {
                var coreSystem = RuntimeManager.CoreSystem;
                var speakerMode = FMOD.SPEAKERMODE.DEFAULT;
                int rawSpeakerCount;
                if (coreSystem.getSoftwareFormat(out int fmodSampleRate, out speakerMode, out rawSpeakerCount) == FMOD.RESULT.OK && fmodSampleRate > 0)
                {
                    sampleRate = fmodSampleRate;
                }

                var description = new FMOD.DSP_DESCRIPTION
                {
                    pluginsdkversion = FmodPluginSdkVersion,
                    name = CreateNameBytes("GameViewRecorderTap"),
                    version = 1,
                    numinputbuffers = 1,
                    numoutputbuffers = 1,
                    read = ReadCallback
                };

                var result = coreSystem.createDSP(ref description, out _dsp);
                if (result != FMOD.RESULT.OK)
                {
                    throw new InvalidOperationException("createDSP failed: " + result);
                }

                result = coreSystem.getMasterChannelGroup(out _masterGroup);
                if (result != FMOD.RESULT.OK)
                {
                    throw new InvalidOperationException("getMasterChannelGroup failed: " + result);
                }

                lock (SyncRoot)
                {
                    PendingBuffers.Clear();
                    _isCapturing = true;
                }

                result = _masterGroup.addDSP(FMOD.CHANNELCONTROL_DSP_INDEX.TAIL, _dsp);
                if (result != FMOD.RESULT.OK)
                {
                    throw new InvalidOperationException("addDSP failed: " + result);
                }

                return true;
            }
            catch (Exception exception)
            {
                EndCapture();
                Debug.LogWarning("GameView Recorder: 无法挂载 FMOD 音频采集，回退到 Unity Audio。原因：" + exception.Message);
                return false;
            }
        }

        public static void EndCapture()
        {
            lock (SyncRoot)
            {
                _isCapturing = false;
                PendingBuffers.Clear();
            }

            if (_masterGroup.handle != IntPtr.Zero && _dsp.handle != IntPtr.Zero)
            {
                _masterGroup.removeDSP(_dsp);
            }

            if (_dsp.handle != IntPtr.Zero)
            {
                _dsp.release();
                _dsp.clearHandle();
            }

            _masterGroup.clearHandle();
        }

        public static bool TryDequeue(out float[] buffer)
        {
            lock (SyncRoot)
            {
                if (PendingBuffers.Count > 0)
                {
                    buffer = PendingBuffers.Dequeue();
                    return true;
                }
            }

            buffer = null;
            return false;
        }

        private static FMOD.RESULT OnDspRead(
            ref FMOD.DSP_STATE dspState,
            IntPtr inBuffer,
            IntPtr outBuffer,
            uint length,
            int inChannels,
            ref int outChannels)
        {
            outChannels = inChannels;
            if (inBuffer == IntPtr.Zero || outBuffer == IntPtr.Zero || length == 0 || inChannels <= 0)
            {
                return FMOD.RESULT.OK;
            }

            int frameCount = (int)length;
            int inputSampleCount = frameCount * inChannels;
            var input = new float[inputSampleCount];
            Marshal.Copy(inBuffer, input, 0, inputSampleCount);
            Marshal.Copy(input, 0, outBuffer, inputSampleCount);

            lock (SyncRoot)
            {
                if (!_isCapturing)
                {
                    return FMOD.RESULT.OK;
                }

                var stereo = new float[frameCount * OutputChannelCount];
                for (int frame = 0; frame < frameCount; frame++)
                {
                    int inputIndex = frame * inChannels;
                    int outputIndex = frame * OutputChannelCount;
                    float left = input[inputIndex];
                    float right = inChannels > 1 ? input[inputIndex + 1] : left;
                    stereo[outputIndex] = left;
                    stereo[outputIndex + 1] = right;
                }

                PendingBuffers.Enqueue(stereo);
                while (PendingBuffers.Count > MaxPendingBuffers)
                {
                    PendingBuffers.Dequeue();
                }
            }

            return FMOD.RESULT.OK;
        }

        private static byte[] CreateNameBytes(string name)
        {
            var bytes = new byte[32];
            Encoding.ASCII.GetBytes(name, 0, Math.Min(name.Length, bytes.Length - 1), bytes, 0);
            return bytes;
        }
    }
}
