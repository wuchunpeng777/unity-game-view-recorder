using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace GameViewRecorder.Editor.Capture
{
    internal struct GameViewCaptureArea
    {
        public readonly int X;
        public readonly int Y;
        public readonly int Width;
        public readonly int Height;
        // GameView 当前 Game 标签页配置的渲染分辨率（原画画质录制时使用，单位为像素）。
        public readonly int NativeWidth;
        public readonly int NativeHeight;
        public readonly Rect ScreenRectPoints;

        public GameViewCaptureArea(int x, int y, int width, int height, int nativeWidth, int nativeHeight, Rect screenRectPoints)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
            NativeWidth = nativeWidth;
            NativeHeight = nativeHeight;
            ScreenRectPoints = screenRectPoints;
        }

        public bool IsValid => Width > 0 && Height > 0;

        public bool HasNativeSize => NativeWidth > 0 && NativeHeight > 0;
    }

    internal static class GameViewCaptureAreaUtility
    {
        // GameView 顶部工具栏（Display 1 / Free Aspect / Stats / Gizmos 等）的高度。
        private const float ToolbarHeight = 20f;
        // DockArea 中 Tab 标签栏（Game / Scene / Console 等标签）的高度。
        // 不同 Unity 版本可能略有差异，19~21px 都常见，取一个略大值确保不会再框住标题。
        private const float DockTabHeight = 21f;

        private static readonly Type GameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
        private static readonly PropertyInfo TargetSizeProperty = GameViewType?.GetProperty(
            "targetSize",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo ParentField = typeof(EditorWindow).GetField(
            "m_Parent",
            BindingFlags.Instance | BindingFlags.NonPublic);

        public static bool TryGetArea(out GameViewCaptureArea area, out string error)
        {
            area = new GameViewCaptureArea();
            error = null;

            if (GameViewType == null)
            {
                error = "无法找到 UnityEditor.GameView 类型。";
                return false;
            }

            var gameView = GetGameView();
            if (gameView == null)
            {
                error = "无法获取 GameView 窗口。";
                return false;
            }

            // EditorWindow.position 在停靠时返回的是相对父容器的局部坐标，直接 * pixelsPerPoint
            // 不会等于屏幕绝对像素，会导致 BitBlt 录到的画面比实际向上/向左偏移。
            // 这里通过反射拿 HostView.screenPosition——它始终是绝对屏幕坐标（单位为编辑器点），
            // 并且包含顶部 Tab 标签栏 + GameView Toolbar，需要再减去这两段才能得到内容区域。
            if (!TryGetHostScreenRect(gameView, out var hostScreenRect))
            {
                // 反射失败时退化为浮动窗口的近似处理，position 已是屏幕坐标。
                hostScreenRect = gameView.position;
            }

            float chromeHeight = DockTabHeight + ToolbarHeight;
            if (hostScreenRect.width <= 1f || hostScreenRect.height <= chromeHeight + 1f)
            {
                error = "GameView 窗口尺寸过小。";
                return false;
            }

            var targetSize = GetTargetSize(gameView);
            if (targetSize.x <= 0f || targetSize.y <= 0f)
            {
                error = "无法获取 GameView 目标分辨率。";
                return false;
            }

            // GameView 真正用于绘制画面的屏幕区域（已跳过 Tab 标签栏和 Toolbar）。
            var contentBounds = new Rect(
                hostScreenRect.x,
                hostScreenRect.y + chromeHeight,
                hostScreenRect.width,
                Mathf.Max(1f, hostScreenRect.height - chromeHeight));

            var fitted = FitAspect(contentBounds, targetSize.x / targetSize.y);

            float pixelsPerPoint = EditorGUIUtility.pixelsPerPoint;

            int x = Mathf.RoundToInt(fitted.x * pixelsPerPoint);
            int y = Mathf.RoundToInt(fitted.y * pixelsPerPoint);
            int width = Mathf.RoundToInt(fitted.width * pixelsPerPoint);
            int height = Mathf.RoundToInt(fitted.height * pixelsPerPoint);

            width -= width % 2;
            height -= height % 2;

            int nativeWidth = Mathf.Max(2, Mathf.RoundToInt(targetSize.x));
            int nativeHeight = Mathf.Max(2, Mathf.RoundToInt(targetSize.y));
            nativeWidth -= nativeWidth % 2;
            nativeHeight -= nativeHeight % 2;

            area = new GameViewCaptureArea(x, y, width, height, nativeWidth, nativeHeight, fitted);
            if (!area.IsValid)
            {
                error = "计算出的 GameView 有效显示区域无效。";
                return false;
            }

            return true;
        }

        private static bool TryGetHostScreenRect(EditorWindow window, out Rect screenRect)
        {
            screenRect = default;
            if (ParentField == null)
            {
                return false;
            }

            var host = ParentField.GetValue(window);
            if (host == null)
            {
                return false;
            }

            var screenPositionProperty = host.GetType().GetProperty(
                "screenPosition",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (screenPositionProperty == null)
            {
                return false;
            }

            if (screenPositionProperty.GetValue(host) is Rect rect && rect.width > 0f && rect.height > 0f)
            {
                screenRect = rect;
                return true;
            }

            return false;
        }

        private static Vector2 GetTargetSize(EditorWindow gameView)
        {
            if (TargetSizeProperty != null)
            {
                var value = TargetSizeProperty.GetValue(gameView, null);
                if (value is Vector2 vector)
                {
                    return vector;
                }
            }

            return new Vector2(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
        }

        private static EditorWindow GetGameView()
        {
            var windows = Resources.FindObjectsOfTypeAll(GameViewType);
            if (windows != null && windows.Length > 0)
            {
                return windows[0] as EditorWindow;
            }

            return EditorWindow.GetWindow(GameViewType);
        }

        private static Rect FitAspect(Rect bounds, float aspect)
        {
            float width = bounds.width;
            float height = width / aspect;

            if (height > bounds.height)
            {
                height = bounds.height;
                width = height * aspect;
            }

            return new Rect(
                bounds.x + (bounds.width - width) * 0.5f,
                bounds.y + (bounds.height - height) * 0.5f,
                width,
                height);
        }
    }
}
