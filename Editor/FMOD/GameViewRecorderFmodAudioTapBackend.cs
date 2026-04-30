// ================================================================
// 本文件由 AI 自动生成
// 生成工具: GPT-5.5
// 作者: 邬春鹏
// 生成时间: 2026-04-30 15:11:00
// ================================================================

#if GAME_VIEW_RECORDER_FMOD

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using FMODUnity;
using GameViewRecorder.Runtime;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace GameViewRecorder.Editor.FmodIntegration
{
    [InitializeOnLoad]
    internal static class GameViewRecorderFmodAudioTapRegistration
    {
        static GameViewRecorderFmodAudioTapRegistration()
        {
            GameViewRecorderFmodAudioTap.RegisterBackend(new GameViewRecorderFmodAudioTapBackend());
        }
    }

    internal sealed class GameViewRecorderFmodAudioTapBackend : GameViewRecorderFmodAudioTap.IBackend
    {
        private const uint FmodPluginSdkVersion = 110;
        private const int OutputChannelCount = 2;
        private const int MaxPendingBuffers = 240;

        private readonly object _syncRoot = new object();
        private readonly Queue<float[]> _pendingBuffers = new Queue<float[]>();

        private FMOD.DSP_READ_CALLBACK _readCallback;
        private FMOD.ChannelGroup _masterGroup;
        private FMOD.DSP _dsp;
        private bool _isCapturing;

        public bool BeginCapture(out int sampleRate, out int channelCount)
        {
            EndCapture();

            sampleRate = AudioSettings.outputSampleRate;
            channelCount = OutputChannelCount;

            try
            {
                FMOD.System coreSystem = RuntimeManager.CoreSystem;
                if (!coreSystem.hasHandle())
                {
                    return false;
                }

                int softwareRate;
                FMOD.SPEAKERMODE speakerMode;
                int rawSpeakerCount;
                FMOD.RESULT result = coreSystem.getSoftwareFormat(out softwareRate, out speakerMode, out rawSpeakerCount);
                if (result == FMOD.RESULT.OK && softwareRate > 0)
                {
                    sampleRate = softwareRate;
                }

                if (_readCallback == null)
                {
                    _readCallback = OnDspRead;
                }

                var description = new FMOD.DSP_DESCRIPTION
                {
                    pluginsdkversion = FmodPluginSdkVersion,
                    name = CreateNameBytes("GameViewRecorderTap"),
                    version = 1u,
                    numinputbuffers = 1,
                    numoutputbuffers = 1,
                    read = _readCallback
                };

                CheckResult(coreSystem.createDSP(ref description, out _dsp), "createDSP");
                CheckResult(coreSystem.getMasterChannelGroup(out _masterGroup), "getMasterChannelGroup");

                lock (_syncRoot)
                {
                    _pendingBuffers.Clear();
                    _isCapturing = true;
                }

                CheckResult(_masterGroup.addDSP(FMOD.CHANNELCONTROL_DSP_INDEX.TAIL, _dsp), "addDSP");
                return true;
            }
            catch (Exception exception)
            {
                EndCapture();
                Debug.LogWarning("GameView Recorder: 无法挂载 FMOD 音频采集，回退到 Unity Audio。原因：" + exception.Message);
                return false;
            }
        }

        public void EndCapture()
        {
            lock (_syncRoot)
            {
                _isCapturing = false;
                _pendingBuffers.Clear();
            }

            if (_masterGroup.hasHandle() && _dsp.hasHandle())
            {
                _masterGroup.removeDSP(_dsp);
            }

            if (_dsp.hasHandle())
            {
                _dsp.release();
                _dsp.clearHandle();
            }

            if (_masterGroup.hasHandle())
            {
                _masterGroup.clearHandle();
            }
        }

        public bool TryDequeue(out float[] buffer)
        {
            lock (_syncRoot)
            {
                if (_pendingBuffers.Count > 0)
                {
                    buffer = _pendingBuffers.Dequeue();
                    return true;
                }
            }

            buffer = null;
            return false;
        }

        private FMOD.RESULT OnDspRead(
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

            lock (_syncRoot)
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

                _pendingBuffers.Enqueue(stereo);
                while (_pendingBuffers.Count > MaxPendingBuffers)
                {
                    _pendingBuffers.Dequeue();
                }
            }

            return FMOD.RESULT.OK;
        }

        private static void CheckResult(FMOD.RESULT result, string operation)
        {
            if (result != FMOD.RESULT.OK)
            {
                throw new InvalidOperationException(operation + " failed: " + result);
            }
        }

        private static byte[] CreateNameBytes(string name)
        {
            var bytes = new byte[32];
            Encoding.ASCII.GetBytes(name, 0, Math.Min(name.Length, bytes.Length - 1), bytes, 0);
            return bytes;
        }
    }
}

#endif
