using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Forms;
using ScreenSpell.Core.Interfaces;
using ScreenSpell.Core.Models;

namespace ScreenSpell.Capture
{
    /// <summary>
    /// GDI (BitBlt) screen grabber. It is slower than Windows.Graphics.Capture but needs no
    /// capture picker, works on every Windows 10 build and never shows a yellow border.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class GraphicsCaptureService : IScreenCaptureService
    {
        public ScreenFrame CaptureScreen()
        {
            var bounds = Screen.PrimaryScreen?.Bounds ?? SystemInformation.VirtualScreen;
            return CaptureRegion(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }

        public ScreenFrame? CaptureActiveWindow()
        {
            var bounds = ForegroundWindowBounds();
            if (bounds is null)
                return null;

            return CaptureRegion(bounds.Value.X, bounds.Value.Y, bounds.Value.Width, bounds.Value.Height);
        }

        /// <summary>Grabs every monitor of the virtual desktop in one frame.</summary>
        public ScreenFrame CaptureVirtualScreen()
        {
            var bounds = SystemInformation.VirtualScreen;
            return CaptureRegion(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        }

        /// <summary>
        /// Visible bounds of the foreground window, clipped to the virtual desktop. Uses the
        /// DWM frame so the invisible resize border of a normal window is not captured.
        /// </summary>
        private static Rectangle? ForegroundWindowBounds()
        {
            var handle = NativeMethods.GetForegroundWindow();
            if (handle == IntPtr.Zero)
                return null;

            // Checking our own window would only report the words listed in it.
            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            if (processId == Environment.ProcessId)
                return null;

            NativeMethods.Rect rect;
            if (NativeMethods.DwmGetWindowAttribute(
                    handle,
                    NativeMethods.DwmwaExtendedFrameBounds,
                    out rect,
                    Marshal.SizeOf<NativeMethods.Rect>()) != 0
                && !NativeMethods.GetWindowRect(handle, out rect))
            {
                return null;
            }

            var bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            bounds.Intersect(SystemInformation.VirtualScreen);

            // A minimized or collapsed window is not worth a scan.
            return bounds.Width < 32 || bounds.Height < 32 ? null : bounds;
        }

        public ScreenFrame CaptureRegion(int x, int y, int width, int height)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
            }

            var data = bitmap.LockBits(
                new Rectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            try
            {
                var pixels = new byte[data.Stride * height];
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                return new ScreenFrame(width, height, data.Stride, pixels, x, y);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }

        private static class NativeMethods
        {
            public const int DwmwaExtendedFrameBounds = 9;

            [StructLayout(LayoutKind.Sequential)]
            public struct Rect
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }

            [DllImport("user32.dll")]
            public static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll")]
            public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);

            [DllImport("user32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

            [DllImport("dwmapi.dll")]
            public static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out Rect value, int size);
        }
    }
}
