using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using GameViewRecorder.Editor.Capture;
using GameViewRecorder.Runtime;

namespace GameViewRecorder.Editor.UI
{
    public sealed class GameViewRecorderWindow : EditorWindow
    {
        private const int DefaultFrameRate = 30;
        private const int MaxFramesPerUpdateSeconds = 5;
        private const string MenuPath = "Tools/GameView Recorder";
        private const string OutputFolderName = "GameViewRecord";

        private enum QualityPreset
        {
            High = 12,
            Standard = 18,
            Performance = 24
        }

        private enum RecorderState
        {
            Idle,
            Countdown,
            Recording
        }

        private const string IncludeCursorPrefsKey = "GameViewRecorder.IncludeCursor";
        private const string RecordAudioPrefsKey = "GameViewRecorder.RecordAudio";
        private const string RevealOnStopPrefsKey = "GameViewRecorder.RevealOnStop";
        private const string CountdownSecondsPrefsKey = "GameViewRecorder.CountdownSeconds";
        private const string FrameRatePrefsKey = "GameViewRecorder.FrameRate";
        private const string QualityPresetPrefsKey = "GameViewRecorder.QualityPreset";

        private bool _includeCursor = true;
        private bool _recordAudio = true;
        private bool _revealOnStop = true;
        private int _countdownSeconds = 3;
        private int _frameRate = DefaultFrameRate;
        private QualityPreset _qualityPreset = QualityPreset.Standard;

        private RecorderState _state = RecorderState.Idle;
        private double _countdownEndTime;
        private double _nextFrameTime;
        private string _outputPath;
        private string _status = "准备就绪";
        private GameViewCaptureArea _captureArea;
        private GameViewMediaRecorder _recorder;
        private GameViewRecorderAudioTap _audioTap;
        private bool _createdAudioTap;

        [MenuItem(MenuPath, false, 300)]
        private static void Open()
        {
            var window = GetWindow<GameViewRecorderWindow>("GameView Recorder");
            window.minSize = new Vector2(360f, 230f);
            window.Show();
        }

        private void OnEnable()
        {
            _includeCursor = EditorPrefs.GetBool(IncludeCursorPrefsKey, true);
            _recordAudio = EditorPrefs.GetBool(RecordAudioPrefsKey, true);
            _revealOnStop = EditorPrefs.GetBool(RevealOnStopPrefsKey, true);
            _countdownSeconds = EditorPrefs.GetInt(CountdownSecondsPrefsKey, 3);
            _frameRate = EditorPrefs.GetInt(FrameRatePrefsKey, DefaultFrameRate);
            _qualityPreset = (QualityPreset)EditorPrefs.GetInt(QualityPresetPrefsKey, (int)QualityPreset.Standard);
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            if (_state != RecorderState.Idle)
            {
                StopRecording("录制窗口已关闭。", false);
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("GameView 录制", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("仅在 Play Mode 下录制 GameView 的有效显示区域，输出 MP4/H.264 到 Assets 同级 GameViewRecord 文件夹。", MessageType.Info);

            using (new EditorGUI.DisabledScope(_state != RecorderState.Idle))
            {
                using (var check = new EditorGUI.ChangeCheckScope())
                {
                    _countdownSeconds = EditorGUILayout.IntSlider("倒计时（秒）", _countdownSeconds, 0, 10);
                    _frameRate = EditorGUILayout.IntPopup("录制帧率", _frameRate, new[] { "30 FPS（推荐）", "60 FPS" }, new[] { 30, 60 });
                    _qualityPreset = (QualityPreset)EditorGUILayout.IntPopup(
                        "画质",
                        (int)_qualityPreset,
                        new[] { "高画质（接近原画）", "普通（推荐）", "性能优先" },
                        new[] { (int)QualityPreset.High, (int)QualityPreset.Standard, (int)QualityPreset.Performance });
                    _includeCursor = EditorGUILayout.ToggleLeft("录制真实系统鼠标光标", _includeCursor);
                    _recordAudio = EditorGUILayout.ToggleLeft("录制游戏音频", _recordAudio);
                    if (check.changed)
                    {
                        SaveRecordingOptions();
                    }
                }
            }

            using (var check = new EditorGUI.ChangeCheckScope())
            {
                _revealOnStop = EditorGUILayout.ToggleLeft("停止录制后自动打开输出文件夹", _revealOnStop);
                if (check.changed)
                {
                    EditorPrefs.SetBool(RevealOnStopPrefsKey, _revealOnStop);
                }
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("当前状态", _status);

            EditorGUILayout.Space(8f);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_state != RecorderState.Idle))
                {
                    if (GUILayout.Button("开始录制", GUILayout.Height(30f)))
                    {
                        StartCountdown();
                    }
                }

                using (new EditorGUI.DisabledScope(_state == RecorderState.Idle))
                {
                    if (GUILayout.Button("停止录制", GUILayout.Height(30f)))
                    {
                        StopRecording("录制已停止。", _revealOnStop);
                    }
                }
            }
        }

        private void StartCountdown()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("无法开始录制", "GameView 录制仅支持 Play Mode。请先进入 Play Mode 后再开始录制。", "确定");
                return;
            }

            if (!GameViewCaptureAreaUtility.TryGetArea(out _captureArea, out string error))
            {
                EditorUtility.DisplayDialog("无法开始录制", error, "确定");
                return;
            }

            _countdownEndTime = EditorApplication.timeSinceStartup + _countdownSeconds;
            _state = _countdownSeconds > 0 ? RecorderState.Countdown : RecorderState.Recording;
            _status = _state == RecorderState.Countdown ? "倒计时中" : "录制中";

            if (_state == RecorderState.Recording)
            {
                BeginRecording();
            }

            Repaint();
        }

        private void BeginRecording()
        {
            try
            {
                PrepareAudioTap();
                _outputPath = CreateOutputPath();
                Directory.CreateDirectory(Path.GetDirectoryName(_outputPath));

                int encodeWidth = _captureArea.Width;
                int encodeHeight = _captureArea.Height;

                _recorder = new GameViewMediaRecorder();
                _recorder.Start(_outputPath, _captureArea, _frameRate, (int)_qualityPreset, _recordAudio, _includeCursor);
                _nextFrameTime = EditorApplication.timeSinceStartup;
                _state = RecorderState.Recording;
                _status = string.Format("录制中 {0}x{1} / {2}FPS / {3}", encodeWidth, encodeHeight, _frameRate, GetQualityLabel(_qualityPreset));
            }
            catch (Exception exception)
            {
                StopRecording("开始录制失败：" + exception.Message, false);
                EditorUtility.DisplayDialog("开始录制失败", exception.ToString(), "确定");
            }
        }

        private void OnEditorUpdate()
        {
            if (_state == RecorderState.Idle)
            {
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                StopRecording("Play Mode 已退出，录制已中断。", _revealOnStop);
                return;
            }

            if (!GameViewCaptureAreaUtility.TryGetArea(out var currentArea, out string error))
            {
                StopRecording("录制中断：" + error, _revealOnStop);
                return;
            }

            _captureArea = currentArea;
            int countdownRemain = _state == RecorderState.Countdown
                ? Mathf.Max(0, Mathf.CeilToInt((float)(_countdownEndTime - EditorApplication.timeSinceStartup)))
                : 0;
            GameViewRecordingOverlayWindow.ShowOverlay(_captureArea, GetOverlayStatus, countdownRemain);

            if (_state == RecorderState.Countdown)
            {
                if (EditorApplication.timeSinceStartup >= _countdownEndTime)
                {
                    BeginRecording();
                }

                Repaint();
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            double frameInterval = 1.0 / _frameRate;
            if (now < _nextFrameTime)
            {
                return;
            }

            try
            {
                int framesToWrite = Mathf.Clamp(
                    Mathf.FloorToInt((float)((now - _nextFrameTime) / frameInterval)) + 1,
                    1,
                    Mathf.Max(1, _frameRate * MaxFramesPerUpdateSeconds));
                _recorder.AddFrame(_captureArea, _includeCursor, framesToWrite);
                _nextFrameTime += frameInterval * framesToWrite;
            }
            catch (Exception exception)
            {
                StopRecording("录制中断：" + exception.Message, _revealOnStop);
                EditorUtility.DisplayDialog("录制中断", exception.ToString(), "确定");
            }

            Repaint();
        }

        private string GetOverlayStatus()
        {
            // 倒计时在 GameView 中央以大字显示，左上角小标签只在录制阶段使用，固定为 REC。
            return "REC";
        }

        private static string GetQualityLabel(QualityPreset preset)
        {
            switch (preset)
            {
                case QualityPreset.High:
                    return "高画质";
                case QualityPreset.Performance:
                    return "性能优先";
                default:
                    return "普通";
            }
        }

        private void SaveRecordingOptions()
        {
            EditorPrefs.SetBool(IncludeCursorPrefsKey, _includeCursor);
            EditorPrefs.SetBool(RecordAudioPrefsKey, _recordAudio);
            EditorPrefs.SetInt(CountdownSecondsPrefsKey, _countdownSeconds);
            EditorPrefs.SetInt(FrameRatePrefsKey, _frameRate);
            EditorPrefs.SetInt(QualityPresetPrefsKey, (int)_qualityPreset);
        }

        private void StopRecording(string status, bool revealOutput)
        {
            bool wasRecording = _state == RecorderState.Recording;
            _status = status;
            _state = RecorderState.Idle;

            GameViewRecordingOverlayWindow.CloseOverlay();

            try
            {
                _recorder?.Dispose();
            }
            finally
            {
                _recorder = null;
                CleanupAudioTap();
            }

            if (wasRecording && revealOutput && !string.IsNullOrEmpty(_outputPath) && File.Exists(_outputPath))
            {
                EditorUtility.RevealInFinder(_outputPath);
            }

            Repaint();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode && _state != RecorderState.Idle)
            {
                StopRecording("Play Mode 已退出，录制已中断。", _revealOnStop);
            }
        }

        private void PrepareAudioTap()
        {
            CleanupAudioTap();

            if (!_recordAudio)
            {
                return;
            }

            var listener = FindObjectOfType<AudioListener>();
            if (listener == null)
            {
                Debug.LogWarning("GameView Recorder: 未找到 AudioListener，Unity Audio 回退采集不可用；如果 FMOD 采集成功，输出仍会包含 FMOD 音频。");
                return;
            }

            _audioTap = listener.GetComponent<GameViewRecorderAudioTap>();
            if (_audioTap == null)
            {
                _audioTap = listener.gameObject.AddComponent<GameViewRecorderAudioTap>();
                _createdAudioTap = true;
            }
        }

        private void CleanupAudioTap()
        {
            if (_createdAudioTap && _audioTap != null)
            {
                DestroyImmediate(_audioTap);
            }

            _audioTap = null;
            _createdAudioTap = false;
        }

        private static string CreateOutputPath()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outputFolder = Path.Combine(projectRoot, OutputFolderName);
            string fileName = DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4";
            return Path.Combine(outputFolder, fileName);
        }
    }
}
