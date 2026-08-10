using System.Drawing;
using System.Drawing.Imaging;
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

        /// <summary>Grabs every monitor of the virtual desktop in one frame.</summary>
        public ScreenFrame CaptureVirtualScreen()
        {
            var bounds = SystemInformation.VirtualScreen;
            return CaptureRegion(bounds.X, bounds.Y, bounds.Width, bounds.Height);
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
                System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                return new ScreenFrame(width, height, data.Stride, pixels, x, y);
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
        }
    }
}
