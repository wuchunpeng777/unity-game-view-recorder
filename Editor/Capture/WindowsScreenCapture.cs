using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace GameViewRecorder.Editor.Capture
{
    internal sealed class WindowsScreenCapture : IDisposable
    {
        private Texture2D _texture;
        private Color32[] _pixels;
        private byte[] _buffer;

        public Texture2D Capture(GameViewCaptureArea area, bool includeCursor)
        {
#if UNITY_EDITOR_WIN
            EnsureBuffers(area.Width, area.Height);

            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memoryDc = CreateCompatibleDC(screenDc);
            IntPtr bitmap = CreateCompatibleBitmap(screenDc, area.Width, area.Height);
            IntPtr oldBitmap = SelectObject(memoryDc, bitmap);

            try
            {
                BitBlt(memoryDc, 0, 0, area.Width, area.Height, screenDc, area.X, area.Y, CopyPixelOperation.SourceCopy | CopyPixelOperation.CaptureBlt);

                if (includeCursor)
                {
                    DrawCursor(memoryDc, area);
                }

                var info = BitmapInfo.Create(area.Width, area.Height);
                GetDIBits(memoryDc, bitmap, 0, (uint)area.Height, _buffer, ref info, 0);

                for (int sourceY = 0; sourceY < area.Height; sourceY++)
                {
                    int targetY = area.Height - 1 - sourceY;
                    int sourceRow = sourceY * area.Width * 4;
                    int targetRow = targetY * area.Width;

                    for (int x = 0; x < area.Width; x++)
                    {
                        int source = sourceRow + x * 4;
                        _pixels[targetRow + x] = new Color32(_buffer[source + 2], _buffer[source + 1], _buffer[source], _buffer[source + 3]);
                    }
                }

                _texture.SetPixels32(_pixels);
                _texture.Apply(false, false);
                return _texture;
            }
            finally
            {
                SelectObject(memoryDc, oldBitmap);
                DeleteObject(bitmap);
                DeleteDC(memoryDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
#else
            throw new PlatformNotSupportedException("真实系统鼠标录制和窗口像素捕获当前仅支持 Windows Editor。");
#endif
        }

        public void Dispose()
        {
            if (_texture != null)
            {
                UnityEngine.Object.DestroyImmediate(_texture);
                _texture = null;
            }
        }

        private void EnsureBuffers(int width, int height)
        {
            int pixelCount = width * height;
            if (_texture == null || _texture.width != width || _texture.height != height)
            {
                if (_texture != null)
                {
                    UnityEngine.Object.DestroyImmediate(_texture);
                }

                _texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                _texture.hideFlags = HideFlags.HideAndDontSave;
            }

            if (_pixels == null || _pixels.Length != pixelCount)
            {
                _pixels = new Color32[pixelCount];
                _buffer = new byte[pixelCount * 4];
            }
        }

#if UNITY_EDITOR_WIN
        private static void DrawCursor(IntPtr targetDc, GameViewCaptureArea area)
        {
            var cursorInfo = new CursorInfo();
            cursorInfo.cbSize = Marshal.SizeOf(typeof(CursorInfo));
            if (!GetCursorInfo(out cursorInfo) || cursorInfo.flags != CursorShowing)
            {
                return;
            }

            var iconInfo = new IconInfo();
            int hotSpotX = 0;
            int hotSpotY = 0;

            if (GetIconInfo(cursorInfo.hCursor, out iconInfo))
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

            int x = cursorInfo.ptScreenPos.x - area.X - hotSpotX;
            int y = cursorInfo.ptScreenPos.y - area.Y - hotSpotY;
            DrawIconEx(targetDc, x, y, cursorInfo.hCursor, 0, 0, 0, IntPtr.Zero, DrawIconNormal);
        }

        [Flags]
        private enum CopyPixelOperation : int
        {
            SourceCopy = 0x00CC0020,
            CaptureBlt = 0x40000000
        }

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

        private const int CursorShowing = 0x00000001;
        private const int DrawIconNormal = 0x0003;

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr hdc, int x, int y, int cx, int cy, IntPtr hdcSrc, int x1, int y1, CopyPixelOperation rop);

        [DllImport("gdi32.dll")]
        private static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint lines, byte[] bits, ref BitmapInfo info, uint usage);

        [DllImport("user32.dll")]
        private static extern bool GetCursorInfo(out CursorInfo pci);

        [DllImport("user32.dll")]
        private static extern bool DrawIconEx(IntPtr hdc, int xLeft, int yTop, IntPtr hIcon, int cxWidth, int cyWidth, int istepIfAniCur, IntPtr hbrFlickerFreeDraw, int diFlags);

        [DllImport("user32.dll")]
        private static extern bool GetIconInfo(IntPtr hIcon, out IconInfo piconinfo);
#endif
    }
}
