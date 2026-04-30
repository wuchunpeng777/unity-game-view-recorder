using System;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GameViewRecorder.Editor.Capture
{
    /// <summary>
    /// 按 Game 标签配置的目标分辨率离屏渲染当前游戏摄像机，避免屏幕抓取受窗口缩放影响。
    /// </summary>
    internal sealed class GameViewNativeFrameCapture : IDisposable
    {
        private Camera _overlayCamera;
        private GameObject _overlayCameraObject;
        private RenderTexture _renderTexture;
        private Texture2D _texture;

        public Texture2D Capture(GameViewCaptureArea area, bool includeCursor)
        {
            int width = area.NativeWidth;
            int height = area.NativeHeight;
            EnsureBuffers(width, height);
            var prevActive = RenderTexture.active;
            var cameras = Camera.allCameras
                .Where(camera =>
                    camera != null
                    && camera.enabled
                    && camera.gameObject.activeInHierarchy
                    && camera.cameraType == CameraType.Game
                    && camera.targetTexture == null)
                .OrderBy(camera => camera.depth)
                .ToArray();

            if (cameras.Length == 0)
            {
                throw new InvalidOperationException("原画画质录制需要场景中至少有一个启用的 Camera。");
            }

            try
            {
                RenderTexture.active = _renderTexture;
                GL.Clear(true, true, Color.black);

                foreach (var camera in cameras)
                {
                    RenderCamera(camera);
                }

                RenderOverlayCanvases();

                RenderTexture.active = _renderTexture;
                _texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                if (includeCursor)
                {
                    DrawCursor(_texture, area);
                }
                else
                {
                    _texture.Apply(false, false);
                }
            }
            finally
            {
                RenderTexture.active = prevActive;
            }

            return _texture;
        }

        public void Dispose()
        {
            if (_texture != null)
            {
                UnityEngine.Object.DestroyImmediate(_texture);
                _texture = null;
            }

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(_renderTexture);
                _renderTexture = null;
            }

            if (_overlayCameraObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_overlayCameraObject);
                _overlayCameraObject = null;
                _overlayCamera = null;
            }
        }

        private void EnsureBuffers(int width, int height)
        {
            if (_texture != null
                && _texture.width == width
                && _texture.height == height
                && _renderTexture != null
                && _renderTexture.width == width
                && _renderTexture.height == height)
            {
                return;
            }

            if (_texture != null)
            {
                UnityEngine.Object.DestroyImmediate(_texture);
            }

            if (_renderTexture != null)
            {
                _renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(_renderTexture);
            }

            _renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                hideFlags = HideFlags.HideAndDontSave,
                antiAliasing = Mathf.Max(1, QualitySettings.antiAliasing)
            };
            _renderTexture.Create();

            _texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave
            };

            EnsureOverlayCamera();
        }

        private void RenderCamera(Camera camera)
        {
            var previousTarget = camera.targetTexture;
            var previousRect = camera.rect;

            try
            {
                camera.targetTexture = _renderTexture;
                camera.rect = new Rect(0f, 0f, 1f, 1f);
                camera.Render();
            }
            finally
            {
                camera.targetTexture = previousTarget;
                camera.rect = previousRect;
            }
        }

        private void EnsureOverlayCamera()
        {
            if (_overlayCamera != null)
            {
                return;
            }

            _overlayCameraObject = new GameObject("GameView Recorder Overlay Camera")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            _overlayCamera = _overlayCameraObject.AddComponent<Camera>();
            _overlayCamera.enabled = false;
            _overlayCamera.clearFlags = CameraClearFlags.Nothing;
            _overlayCamera.orthographic = true;
            _overlayCamera.nearClipPlane = -1000f;
            _overlayCamera.farClipPlane = 1000f;
            _overlayCamera.depth = 10000f;
            _overlayCamera.allowHDR = false;
            _overlayCamera.allowMSAA = false;
        }

        private void RenderOverlayCanvases()
        {
            var canvases = UnityEngine.Object.FindObjectsOfType<Canvas>();
            if (canvases == null || canvases.Length == 0)
            {
                return;
            }

            EnsureOverlayCamera();
            _overlayCamera.targetTexture = _renderTexture;
            _overlayCamera.pixelRect = new Rect(0f, 0f, _renderTexture.width, _renderTexture.height);

            var states = new OverlayCanvasState[canvases.Length];
            int count = 0;
            int overlayLayerMask = 0;

            try
            {
                foreach (var canvas in canvases)
                {
                    if (canvas == null
                        || !canvas.enabled
                        || !canvas.gameObject.activeInHierarchy
                        || canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    {
                        continue;
                    }

                    states[count++] = new OverlayCanvasState(canvas);
                    overlayLayerMask |= 1 << canvas.gameObject.layer;
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = _overlayCamera;
                    canvas.planeDistance = 1f;
                }

                if (count > 0)
                {
                    _overlayCamera.cullingMask = overlayLayerMask;
                    _overlayCamera.Render();
                }
            }
            finally
            {
                for (int i = count - 1; i >= 0; i--)
                {
                    states[i].Restore();
                }

                _overlayCamera.targetTexture = null;
                _overlayCamera.cullingMask = 0;
            }
        }

        private static void DrawCursor(Texture2D texture, GameViewCaptureArea area)
        {
#if UNITY_EDITOR_WIN
            var cursor = TryCreateCursorPixels(out var cursorPixels, out int cursorWidth, out int cursorHeight, out int hotSpotX, out int hotSpotY);
            if (!cursor)
            {
                texture.Apply(false, false);
                return;
            }

            if (!TryGetCursorScreenPosition(out int screenX, out int screenY))
            {
                texture.Apply(false, false);
                return;
            }

            float scaleX = texture.width / (float)area.Width;
            float scaleY = texture.height / (float)area.Height;
            int startX = Mathf.RoundToInt((screenX - area.X - hotSpotX) * scaleX);
            int startTopY = Mathf.RoundToInt((screenY - area.Y - hotSpotY) * scaleY);
            int scaledWidth = Mathf.Max(1, Mathf.RoundToInt(cursorWidth * scaleX));
            int scaledHeight = Mathf.Max(1, Mathf.RoundToInt(cursorHeight * scaleY));

            if (startX >= texture.width
                || startTopY >= texture.height
                || startX + scaledWidth <= 0
                || startTopY + scaledHeight <= 0)
            {
                texture.Apply(false, false);
                return;
            }

            var framePixels = texture.GetPixels32();
            for (int y = 0; y < scaledHeight; y++)
            {
                int targetTopY = startTopY + y;
                if (targetTopY < 0 || targetTopY >= texture.height)
                {
                    continue;
                }

                int sourceY = Mathf.Clamp(y * cursorHeight / scaledHeight, 0, cursorHeight - 1);
                int targetY = texture.height - 1 - targetTopY;
                for (int x = 0; x < scaledWidth; x++)
                {
                    int targetX = startX + x;
                    if (targetX < 0 || targetX >= texture.width)
                    {
                        continue;
                    }

                    int sourceX = Mathf.Clamp(x * cursorWidth / scaledWidth, 0, cursorWidth - 1);
                    var cursorPixel = cursorPixels[sourceY * cursorWidth + sourceX];
                    if (cursorPixel.a == 0)
                    {
                        continue;
                    }

                    int targetIndex = targetY * texture.width + targetX;
                    framePixels[targetIndex] = AlphaBlend(framePixels[targetIndex], cursorPixel);
                }
            }

            texture.SetPixels32(framePixels);
#endif
            texture.Apply(false, false);
        }

#if UNITY_EDITOR_WIN
        private static Color32 AlphaBlend(Color32 background, Color32 foreground)
        {
            int alpha = foreground.a;
            int inverse = 255 - alpha;
            return new Color32(
                (byte)((foreground.r * alpha + background.r * inverse) / 255),
                (byte)((foreground.g * alpha + background.g * inverse) / 255),
                (byte)((foreground.b * alpha + background.b * inverse) / 255),
                255);
        }

        private static bool TryGetCursorScreenPosition(out int x, out int y)
        {
            x = 0;
            y = 0;

            var cursorInfo = new CursorInfo
            {
                cbSize = Marshal.SizeOf(typeof(CursorInfo))
            };
            if (!GetCursorInfo(out cursorInfo) || cursorInfo.flags != CursorShowing)
            {
                return false;
            }

            x = cursorInfo.ptScreenPos.x;
            y = cursorInfo.ptScreenPos.y;
            return true;
        }

        private static bool TryCreateCursorPixels(
            out Color32[] pixels,
            out int width,
            out int height,
            out int hotSpotX,
            out int hotSpotY)
        {
            pixels = null;
            width = Mathf.Max(32, GetSystemMetrics(SystemMetricCursorWidth));
            height = Mathf.Max(32, GetSystemMetrics(SystemMetricCursorHeight));
            hotSpotX = 0;
            hotSpotY = 0;

            var cursorInfo = new CursorInfo
            {
                cbSize = Marshal.SizeOf(typeof(CursorInfo))
            };
            if (!GetCursorInfo(out cursorInfo) || cursorInfo.flags != CursorShowing || cursorInfo.hCursor == IntPtr.Zero)
            {
                return false;
            }

            if (GetIconInfo(cursorInfo.hCursor, out var iconInfo))
            {
                hotSpotX = iconInfo.xHotspot;
                hotSpotY = iconInfo.yHotspot;
                if (iconInfo.hbmMask != IntPtr.Zero)
                {
                    DeleteObject(iconInfo.hbmMask);
                }

                if (iconInfo.hbmColor != IntPtr.Zero)
                {
                    DeleteObject(iconInfo.hbmColor);
                }
            }

            byte[] black = RenderCursorOnBackground(cursorInfo.hCursor, width, height, 0);
            byte[] white = RenderCursorOnBackground(cursorInfo.hCursor, width, height, 255);
            if (black == null || white == null)
            {
                return false;
            }

            pixels = new Color32[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                int offset = i * 4;
                int blackB = black[offset];
                int blackG = black[offset + 1];
                int blackR = black[offset + 2];
                int whiteB = white[offset];
                int whiteG = white[offset + 1];
                int whiteR = white[offset + 2];

                int diff = Mathf.Max(
                    Mathf.Abs(whiteR - blackR),
                    Mathf.Max(Mathf.Abs(whiteG - blackG), Mathf.Abs(whiteB - blackB)));
                int alpha = 255 - diff;
                if (alpha <= 0)
                {
                    pixels[i] = new Color32(0, 0, 0, 0);
                    continue;
                }

                int r = alpha >= 255 ? blackR : Mathf.Clamp(blackR * 255 / alpha, 0, 255);
                int g = alpha >= 255 ? blackG : Mathf.Clamp(blackG * 255 / alpha, 0, 255);
                int b = alpha >= 255 ? blackB : Mathf.Clamp(blackB * 255 / alpha, 0, 255);
                pixels[i] = new Color32((byte)r, (byte)g, (byte)b, (byte)alpha);
            }

            return true;
        }

        private static byte[] RenderCursorOnBackground(IntPtr cursor, int width, int height, byte background)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memoryDc = CreateCompatibleDC(screenDc);
            IntPtr bits;
            var info = BitmapInfo.Create(width, height);
            IntPtr bitmap = CreateDIBSection(screenDc, ref info, 0, out bits, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
            {
                DeleteDC(memoryDc);
                ReleaseDC(IntPtr.Zero, screenDc);
                return null;
            }

            IntPtr oldBitmap = SelectObject(memoryDc, bitmap);
            int byteCount = width * height * 4;
            var buffer = new byte[byteCount];
            for (int i = 0; i < byteCount; i += 4)
            {
                buffer[i] = background;
                buffer[i + 1] = background;
                buffer[i + 2] = background;
                buffer[i + 3] = 255;
            }

            try
            {
                Marshal.Copy(buffer, 0, bits, byteCount);
                DrawIconEx(memoryDc, 0, 0, cursor, width, height, 0, IntPtr.Zero, DrawIconNormal);
                Marshal.Copy(bits, buffer, 0, byteCount);
                return buffer;
            }
            finally
            {
                SelectObject(memoryDc, oldBitmap);
                DeleteObject(bitmap);
                DeleteDC(memoryDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private const int CursorShowing = 0x00000001;
        private const int DrawIconNormal = 0x0003;
        private const int SystemMetricCursorWidth = 13;
        private const int SystemMetricCursorHeight = 14;

        [StructLayout(LayoutKind.Sequential)]
        private struct Point
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CursorInfo
        {
            public int cbSize;
            public int flags;
            public IntPtr hCursor;
            public Point ptScreenPos;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IconInfo
        {
            public bool fIcon;
            public int xHotspot;
            public int yHotspot;
            public IntPtr hbmMask;
            public IntPtr hbmColor;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;

            public static BitmapInfo Create(int width, int height)
            {
                return new BitmapInfo
                {
                    biSize = Marshal.SizeOf(typeof(BitmapInfo)),
                    biWidth = width,
                    biHeight = -height,
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0,
                    biSizeImage = width * height * 4
                };
            }
        }

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfo pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

        [DllImport("user32.dll")]
        private static extern bool GetCursorInfo(out CursorInfo pci);

        [DllImport("user32.dll")]
        private static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyWidth, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);

        [DllImport("user32.dll")]
        private static extern bool GetIconInfo(IntPtr hIcon, out IconInfo piconinfo);
#endif

        private struct OverlayCanvasState
        {
            private readonly Canvas _canvas;
            private readonly RenderMode _renderMode;
            private readonly Camera _worldCamera;
            private readonly float _planeDistance;

            public OverlayCanvasState(Canvas canvas)
            {
                _canvas = canvas;
                _renderMode = canvas.renderMode;
                _worldCamera = canvas.worldCamera;
                _planeDistance = canvas.planeDistance;
            }

            public void Restore()
            {
                if (_canvas == null)
                {
                    return;
                }

                _canvas.renderMode = _renderMode;
                _canvas.worldCamera = _worldCamera;
                _canvas.planeDistance = _planeDistance;
            }
        }
    }
}
