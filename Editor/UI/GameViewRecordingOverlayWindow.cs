using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using GameViewRecorder.Editor.Capture;

namespace GameViewRecorder.Editor.UI
{
    internal static class GameViewRecordingOverlayWindow
    {
        private const float BorderThickness = 1f;
        private const float OutsideGap = 2f;
        private const float LabelHeight = 22f;
        private const float LabelMinWidth = 60f;
        private const float LabelOutsidePadding = 4f;
        private const float CountdownDigitHeightRatio = 0.18f;
        private const float CountdownDigitMinHeight = 48f;
        private const float CountdownDigitAspect = 0.55f;
        private const float CountdownDigitSpacingRatio = 0.18f;
        private const float CountdownSegmentThicknessRatio = 0.08f;
        private const float CountdownBorderThickness = 3f;

        private static BorderSegmentWindow _top;
        private static BorderSegmentWindow _bottom;
        private static BorderSegmentWindow _left;
        private static BorderSegmentWindow _right;
        private static BorderSegmentWindow _label;
        private static BorderSegmentWindow _countdownTop;
        private static BorderSegmentWindow _countdownBottom;
        private static BorderSegmentWindow _countdownLeft;
        private static BorderSegmentWindow _countdownRight;
        private static readonly List<BorderSegmentWindow> CountdownSegments = new List<BorderSegmentWindow>();

        public static void ShowOverlay(GameViewCaptureArea area, Func<string> statusProvider, int countdownSeconds)
        {
            EnsureBorderWindows();

            var rect = area.ScreenRectPoints;
            string status = statusProvider != null ? statusProvider() : "REC";
            Color borderColor = countdownSeconds > 0 ? Color.yellow : Color.red;

            // 红框只包裹有效画面区域，绝不向上越过 GameView 标题/工具栏。
            _top.Configure(
                new Rect(
                    rect.xMin - OutsideGap - BorderThickness,
                    rect.yMin - OutsideGap - BorderThickness,
                    rect.width + (OutsideGap + BorderThickness) * 2f,
                    BorderThickness),
                BorderSegment.Line,
                status,
                borderColor);
            _bottom.Configure(
                new Rect(
                    rect.xMin - OutsideGap - BorderThickness,
                    rect.yMax + OutsideGap,
                    rect.width + (OutsideGap + BorderThickness) * 2f,
                    BorderThickness),
                BorderSegment.Line,
                status,
                borderColor);
            _left.Configure(
                new Rect(
                    rect.xMin - OutsideGap - BorderThickness,
                    rect.yMin - OutsideGap,
                    BorderThickness,
                    rect.height + OutsideGap * 2f),
                BorderSegment.Line,
                status,
                borderColor);
            _right.Configure(
                new Rect(
                    rect.xMax + OutsideGap,
                    rect.yMin - OutsideGap,
                    BorderThickness,
                    rect.height + OutsideGap * 2f),
                BorderSegment.Line,
                status,
                borderColor);

            if (countdownSeconds > 0)
            {
                // 倒计时阶段：隐藏 REC 小标签，在 GameView 中央显示大字倒计时数字。
                CloseWindow(ref _label);
                EnsureCountdownWindows();

                string countdownText = countdownSeconds.ToString();
                float digitHeight = Mathf.Max(CountdownDigitMinHeight, Mathf.Min(rect.width, rect.height) * CountdownDigitHeightRatio);
                float digitWidth = digitHeight * CountdownDigitAspect;
                float digitSpacing = digitHeight * CountdownDigitSpacingRatio;
                float segmentThickness = Mathf.Max(4f, digitHeight * CountdownSegmentThicknessRatio);
                float groupWidth = countdownText.Length * digitWidth + Mathf.Max(0, countdownText.Length - 1) * digitSpacing;
                float maxGroupWidth = Mathf.Max(1f, rect.width - LabelOutsidePadding * 2f - segmentThickness * 6f);
                if (groupWidth > maxGroupWidth)
                {
                    float scale = maxGroupWidth / groupWidth;
                    digitHeight *= scale;
                    digitWidth *= scale;
                    digitSpacing *= scale;
                    segmentThickness = Mathf.Max(3f, segmentThickness * scale);
                    groupWidth = maxGroupWidth;
                }

                var digitGroupRect = new Rect(
                    rect.xMin + (rect.width - groupWidth) * 0.5f,
                    rect.yMin + (rect.height - digitHeight) * 0.5f,
                    groupWidth,
                    digitHeight);
                float borderPadding = segmentThickness * 3f;
                var countdownRect = new Rect(
                    digitGroupRect.xMin - borderPadding,
                    digitGroupRect.yMin - borderPadding,
                    digitGroupRect.width + borderPadding * 2f,
                    digitGroupRect.height + borderPadding * 2f);

                _countdownTop.Configure(
                    new Rect(
                        countdownRect.xMin,
                        countdownRect.yMin,
                        countdownRect.width,
                        CountdownBorderThickness),
                    BorderSegment.Line,
                    countdownText,
                    Color.red);
                _countdownBottom.Configure(
                    new Rect(
                        countdownRect.xMin,
                        countdownRect.yMax - CountdownBorderThickness,
                        countdownRect.width,
                        CountdownBorderThickness),
                    BorderSegment.Line,
                    countdownText,
                    Color.red);
                _countdownLeft.Configure(
                    new Rect(
                        countdownRect.xMin,
                        countdownRect.yMin,
                        CountdownBorderThickness,
                        countdownRect.height),
                    BorderSegment.Line,
                    countdownText,
                    Color.red);
                _countdownRight.Configure(
                    new Rect(
                        countdownRect.xMax - CountdownBorderThickness,
                        countdownRect.yMin,
                        CountdownBorderThickness,
                        countdownRect.height),
                    BorderSegment.Line,
                    countdownText,
                    Color.red);

                ConfigureCountdownDigits(countdownText, digitGroupRect, digitWidth, digitHeight, digitSpacing, segmentThickness);
            }
            else
            {
                // 录制阶段：隐藏中央倒计时，把 REC 小标签放到红框下方，避免遮挡录制范围。
                CloseCountdownWindows();
                EnsureLabelWindow();

                float labelWidth = Mathf.Max(LabelMinWidth, EstimateLabelWidth(status));
                labelWidth = Mathf.Min(labelWidth, Mathf.Max(LabelMinWidth, rect.width));
                _label.Configure(
                    new Rect(
                        rect.xMin,
                        rect.yMax + OutsideGap + BorderThickness + LabelOutsidePadding,
                        labelWidth,
                        LabelHeight),
                    BorderSegment.Label,
                    status,
                    Color.red);
            }
        }

        public static void CloseOverlay()
        {
            CloseWindow(ref _top);
            CloseWindow(ref _bottom);
            CloseWindow(ref _left);
            CloseWindow(ref _right);
            CloseWindow(ref _label);
            CloseCountdownWindows();
        }

        private static void EnsureBorderWindows()
        {
            _top = EnsureWindow(_top);
            _bottom = EnsureWindow(_bottom);
            _left = EnsureWindow(_left);
            _right = EnsureWindow(_right);
        }

        private static void EnsureLabelWindow()
        {
            _label = EnsureWindow(_label);
        }

        private static void EnsureCountdownWindows()
        {
            _countdownTop = EnsureWindow(_countdownTop);
            _countdownBottom = EnsureWindow(_countdownBottom);
            _countdownLeft = EnsureWindow(_countdownLeft);
            _countdownRight = EnsureWindow(_countdownRight);
        }

        private static BorderSegmentWindow EnsureWindow(BorderSegmentWindow window)
        {
            if (window != null)
            {
                return window;
            }

            window = ScriptableObject.CreateInstance<BorderSegmentWindow>();
            window.hideFlags = HideFlags.HideAndDontSave;
            // 必须在 ShowPopup 之前放宽 min/max，否则 EditorWindow 会强制使用默认最小尺寸（约 100x100），
            // 导致 1px 的边框窗口被显示成又粗又厚的红块，遮挡 GameView。
            window.minSize = new Vector2(1f, 1f);
            window.maxSize = new Vector2(8192f, 8192f);
            window.ShowPopup();
            return window;
        }

        private static void CloseWindow(ref BorderSegmentWindow window)
        {
            if (window == null)
            {
                return;
            }

            window.Close();
            window = null;
        }

        private static void CloseCountdownWindows()
        {
            CloseWindow(ref _countdownTop);
            CloseWindow(ref _countdownBottom);
            CloseWindow(ref _countdownLeft);
            CloseWindow(ref _countdownRight);
            for (int i = CountdownSegments.Count - 1; i >= 0; i--)
            {
                var segment = CountdownSegments[i];
                CloseWindow(ref segment);
                CountdownSegments[i] = segment;
            }

            CountdownSegments.Clear();
        }

        private static void ConfigureCountdownDigits(string text, Rect groupRect, float digitWidth, float digitHeight, float digitSpacing, float thickness)
        {
            int used = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char digit = text[i];
                float x = groupRect.xMin + i * (digitWidth + digitSpacing);
                var digitRect = new Rect(x, groupRect.yMin, digitWidth, digitHeight);
                ConfigureDigitSegment(digit, 0, SegmentA(digitRect, thickness), ref used);
                ConfigureDigitSegment(digit, 1, SegmentB(digitRect, thickness), ref used);
                ConfigureDigitSegment(digit, 2, SegmentC(digitRect, thickness), ref used);
                ConfigureDigitSegment(digit, 3, SegmentD(digitRect, thickness), ref used);
                ConfigureDigitSegment(digit, 4, SegmentE(digitRect, thickness), ref used);
                ConfigureDigitSegment(digit, 5, SegmentF(digitRect, thickness), ref used);
                ConfigureDigitSegment(digit, 6, SegmentG(digitRect, thickness), ref used);
            }

            for (int i = CountdownSegments.Count - 1; i >= used; i--)
            {
                var segment = CountdownSegments[i];
                CloseWindow(ref segment);
                CountdownSegments[i] = segment;
                CountdownSegments.RemoveAt(i);
            }
        }

        private static void ConfigureDigitSegment(char digit, int segmentIndex, Rect rect, ref int used)
        {
            if (!DigitUsesSegment(digit, segmentIndex))
            {
                return;
            }

            while (CountdownSegments.Count <= used)
            {
                CountdownSegments.Add(null);
            }

            var window = CountdownSegments[used];
            window = EnsureWindow(window);
            window.Configure(rect, BorderSegment.Line, digit.ToString(), Color.red);
            CountdownSegments[used] = window;
            used++;
        }

        private static bool DigitUsesSegment(char digit, int segment)
        {
            switch (digit)
            {
                case '0': return segment != 6;
                case '1': return segment == 1 || segment == 2;
                case '2': return segment == 0 || segment == 1 || segment == 3 || segment == 4 || segment == 6;
                case '3': return segment == 0 || segment == 1 || segment == 2 || segment == 3 || segment == 6;
                case '4': return segment == 1 || segment == 2 || segment == 5 || segment == 6;
                case '5': return segment == 0 || segment == 2 || segment == 3 || segment == 5 || segment == 6;
                case '6': return segment == 0 || segment == 2 || segment == 3 || segment == 4 || segment == 5 || segment == 6;
                case '7': return segment == 0 || segment == 1 || segment == 2;
                case '8': return true;
                case '9': return segment == 0 || segment == 1 || segment == 2 || segment == 3 || segment == 5 || segment == 6;
                default: return false;
            }
        }

        private static Rect SegmentA(Rect rect, float thickness)
        {
            return new Rect(rect.xMin + thickness, rect.yMin, rect.width - thickness * 2f, thickness);
        }

        private static Rect SegmentB(Rect rect, float thickness)
        {
            return new Rect(rect.xMax - thickness, rect.yMin + thickness, thickness, rect.height * 0.5f - thickness * 1.5f);
        }

        private static Rect SegmentC(Rect rect, float thickness)
        {
            return new Rect(rect.xMax - thickness, rect.center.y + thickness * 0.5f, thickness, rect.height * 0.5f - thickness * 1.5f);
        }

        private static Rect SegmentD(Rect rect, float thickness)
        {
            return new Rect(rect.xMin + thickness, rect.yMax - thickness, rect.width - thickness * 2f, thickness);
        }

        private static Rect SegmentE(Rect rect, float thickness)
        {
            return new Rect(rect.xMin, rect.center.y + thickness * 0.5f, thickness, rect.height * 0.5f - thickness * 1.5f);
        }

        private static Rect SegmentF(Rect rect, float thickness)
        {
            return new Rect(rect.xMin, rect.yMin + thickness, thickness, rect.height * 0.5f - thickness * 1.5f);
        }

        private static Rect SegmentG(Rect rect, float thickness)
        {
            return new Rect(rect.xMin + thickness, rect.center.y - thickness * 0.5f, rect.width - thickness * 2f, thickness);
        }

        private static float EstimateLabelWidth(string status)
        {
            int length = string.IsNullOrEmpty(status) ? 3 : status.Length;
            // 14px 加粗字体大约 9px/字符，再留一些左右内边距。
            return length * 9f + 16f;
        }

        private enum BorderSegment
        {
            Line,
            Label
        }

        private sealed class BorderSegmentWindow : EditorWindow
        {
            private BorderSegment _segment;
            private string _status = "REC";
            private Color _color = Color.red;

            public void Configure(Rect rect, BorderSegment segment, string status, Color color)
            {
                position = rect;
                _segment = segment;
                _status = status;
                _color = color;
                Repaint();
            }

            private void OnGUI()
            {
                switch (_segment)
                {
                    case BorderSegment.Label:
                        DrawLabel();
                        break;
                    default:
                        EditorGUI.DrawRect(new Rect(0f, 0f, position.width, position.height), _color);
                        break;
                }
            }

            private void DrawLabel()
            {
                EditorGUI.DrawRect(new Rect(0f, 0f, position.width, position.height), new Color(0f, 0f, 0f, 0.75f));

                var style = new GUIStyle(EditorStyles.boldLabel)
                {
                    normal = { textColor = Color.red },
                    alignment = TextAnchor.MiddleLeft,
                    fontSize = 14
                };
                GUI.Label(new Rect(6f, 0f, position.width - 6f, position.height), _status, style);
            }

        }
    }
}
