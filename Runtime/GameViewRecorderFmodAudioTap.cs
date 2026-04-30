using UnityEngine;

namespace GameViewRecorder.Runtime
{
    public static class GameViewRecorderFmodAudioTap
    {
        public interface IBackend
        {
            bool BeginCapture(out int sampleRate, out int channelCount);
            void EndCapture();
            bool TryDequeue(out float[] buffer);
        }

        private static IBackend _backend;

        public static void RegisterBackend(IBackend backend)
        {
            _backend = backend;
        }

        public static bool BeginCapture(out int sampleRate, out int channelCount)
        {
            sampleRate = AudioSettings.outputSampleRate;
            channelCount = 2;
            return _backend != null && _backend.BeginCapture(out sampleRate, out channelCount);
        }

        public static void EndCapture()
        {
            _backend?.EndCapture();
        }

        public static bool TryDequeue(out float[] buffer)
        {
            if (_backend != null)
            {
                return _backend.TryDequeue(out buffer);
            }

            buffer = null;
            return false;
        }
    }
}
