using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameViewRecorder.Runtime
{
    [DisallowMultipleComponent]
    public sealed class GameViewRecorderAudioTap : MonoBehaviour
    {
        private static readonly object SyncRoot = new object();
        private static readonly Queue<float[]> PendingBuffers = new Queue<float[]>();

        private static bool _isCapturing;
        private static float[] _currentBuffer;
        private static int _currentBufferIndex;

        public static void BeginCapture()
        {
            lock (SyncRoot)
            {
                PendingBuffers.Clear();
                _currentBuffer = null;
                _currentBufferIndex = 0;
                _isCapturing = true;
            }
        }

        public static void EndCapture()
        {
            lock (SyncRoot)
            {
                _isCapturing = false;
                PendingBuffers.Clear();
                _currentBuffer = null;
                _currentBufferIndex = 0;
            }
        }

        public static void Fill(float[] destination)
        {
            if (destination == null)
            {
                return;
            }

            int written = 0;

            lock (SyncRoot)
            {
                while (written < destination.Length)
                {
                    if (_currentBuffer == null || _currentBufferIndex >= _currentBuffer.Length)
                    {
                        if (PendingBuffers.Count == 0)
                        {
                            break;
                        }

                        _currentBuffer = PendingBuffers.Dequeue();
                        _currentBufferIndex = 0;
                    }

                    int count = Math.Min(destination.Length - written, _currentBuffer.Length - _currentBufferIndex);
                    Array.Copy(_currentBuffer, _currentBufferIndex, destination, written, count);
                    written += count;
                    _currentBufferIndex += count;
                }
            }

            if (written < destination.Length)
            {
                Array.Clear(destination, written, destination.Length - written);
            }
        }

        public static bool TryDequeue(out float[] buffer)
        {
            lock (SyncRoot)
            {
                if (_currentBuffer != null && _currentBufferIndex < _currentBuffer.Length)
                {
                    int count = _currentBuffer.Length - _currentBufferIndex;
                    buffer = new float[count];
                    Array.Copy(_currentBuffer, _currentBufferIndex, buffer, 0, count);
                    _currentBuffer = null;
                    _currentBufferIndex = 0;
                    return true;
                }

                if (PendingBuffers.Count > 0)
                {
                    buffer = PendingBuffers.Dequeue();
                    return true;
                }
            }

            buffer = null;
            return false;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            lock (SyncRoot)
            {
                if (!_isCapturing)
                {
                    return;
                }

                var copy = new float[data.Length];
                Array.Copy(data, copy, data.Length);
                PendingBuffers.Enqueue(copy);

                while (PendingBuffers.Count > 120)
                {
                    PendingBuffers.Dequeue();
                }
            }
        }
    }
}
