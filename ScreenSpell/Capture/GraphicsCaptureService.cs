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
    ///
    /// Bitmaps and pixel buffers are kept and reused between grabs: a full screen frame is
    /// several megabytes, and allocating one on every refresh is what turns a watching loop
    /// into a garbage collector benchmark. The consequence is that a returned
    /// <see cref="ScreenFrame"/> is only valid until the next grab of the same kind, so a
    /// caller that needs to keep pixels has to copy them.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class GraphicsCaptureService : IScreenCaptureService
    {
        private const int DefaultRefreshRateHz = 60;

        /// <summary>Longest side of a change-detection probe, in pixels.</summary>
        private const int ProbeSize = 384;

        private readonly object _gate = new();
        private readonly Surface _full = new();
        private readonly Surface _probe = new();

        public int RefreshRateHz
        {
            get
            {
                var mode = new NativeMethods.DevMode { Size = (short)Marshal.SizeOf<NativeMethods.DevMode>() };

                return NativeMethods.EnumDisplaySettings(null, NativeMethods.EnumCurrentSettings, ref mode)
                       && mode.DisplayFrequency > 1
                    ? mode.DisplayFrequency
                    : DefaultRefreshRateHz;
            }
        }

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

        public ScreenFrame? CaptureProbe(bool activeWindowOnly)
        {
            Rectangle bounds;
            if (activeWindowOnly)
            {
                var window = ForegroundWindowBounds();
                if (window is null)
                    return null;

                bounds = window.Value;
            }
            else
            {
                bounds = Screen.PrimaryScreen?.Bounds ?? SystemInformation.VirtualScreen;
            }

            var scale = Math.Min(1.0, (double)ProbeSize / Math.Max(bounds.Width, bounds.Height));
            var width = Math.Max(1, (int)(bounds.Width * scale));
            var height = Math.Max(1, (int)(bounds.Height * scale));

            lock (_gate)
            {
                var bitmap = _probe.BitmapOf(width, height);

                using (var graphics = Graphics.FromImage(bitmap))
                {
                    var screen = NativeMethods.CreateDC("DISPLAY", null, null, IntPtr.Zero);
                    if (screen == IntPtr.Zero)
                        return null;

                    var target = graphics.GetHdc();
                    try
                    {
                        NativeMethods.SetStretchBltMode(target, NativeMethods.HalfTone);
                        NativeMethods.StretchBlt(
                            target, 0, 0, width, height,
                            screen, bounds.X, bounds.Y, bounds.Width, bounds.Height,
                            NativeMethods.SrcCopy);
                    }
                    finally
                    {
                        graphics.ReleaseHdc(target);
                        NativeMethods.DeleteDC(screen);
                    }
                }

                // The probe carries the bounds of what it stands for, so the caller can still
                // tell that the window moved or was resized without grabbing it in full.
                return _probe.Read(bitmap, bounds.X, bounds.Y);
            }
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

            lock (_gate)
            {
                var bitmap = _full.BitmapOf(width, height);

                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
                }

                return _full.Read(bitmap, x, y);
            }
        }

        /// <summary>A bitmap and its pixel buffer, both grown on demand and then reused.</summary>
        private sealed class Surface
        {
            private Bitmap? _bitmap;
            private byte[] _pixels = Array.Empty<byte>();

            public Bitmap BitmapOf(int width, int height)
            {
                if (_bitmap is { } existing && existing.Width == width && existing.Height == height)
                    return existing;

                _bitmap?.Dispose();
                _bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                return _bitmap;
            }

            public ScreenFrame Read(Bitmap bitmap, int originX, int originY)
            {
                var data = bitmap.LockBits(
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);

                try
                {
                    var length = data.Stride * bitmap.Height;
                    if (_pixels.Length != length)
                        _pixels = new byte[length];

                    Marshal.Copy(data.Scan0, _pixels, 0, length);
                    return new ScreenFrame(bitmap.Width, bitmap.Height, data.Stride, _pixels, originX, originY);
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
            }
        }

        private static class NativeMethods
        {
            public const int DwmwaExtendedFrameBounds = 9;

            public const int EnumCurrentSettings = -1;

            public const int HalfTone = 4;

            public const int SrcCopy = 0x00CC0020;

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct DevMode
            {
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
                public string DeviceName;
                public short SpecVersion;
                public short DriverVersion;
                public short Size;
                public short DriverExtra;
                public int Fields;
                public int PositionX;
                public int PositionY;
                public int DisplayOrientation;
                public int DisplayFixedOutput;
                public short Color;
                public short Duplex;
                public short YResolution;
                public short TrueTypeOption;
                public short Collate;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
                public string FormName;
                public short LogPixels;
                public int BitsPerPel;
                public int PelsWidth;
                public int PelsHeight;
                public int DisplayFlags;
                public int DisplayFrequency;
                public int IcmMethod;
                public int IcmIntent;
                public int MediaType;
                public int DitherType;
                public int Reserved1;
                public int Reserved2;
                public int PanningWidth;
                public int PanningHeight;
            }

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DevMode devMode);

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

            [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
            public static extern IntPtr CreateDC(string driver, string? device, string? output, IntPtr initData);

            [DllImport("gdi32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DeleteDC(IntPtr hdc);

            [DllImport("gdi32.dll")]
            public static extern int SetStretchBltMode(IntPtr hdc, int mode);

            [DllImport("gdi32.dll")]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool StretchBlt(
                IntPtr destination, int x, int y, int width, int height,
                IntPtr source, int sourceX, int sourceY, int sourceWidth, int sourceHeight,
                int operation);
        }
    }
}
