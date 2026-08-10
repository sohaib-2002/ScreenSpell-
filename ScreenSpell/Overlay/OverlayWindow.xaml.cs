using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenSpell.Core.Models;

namespace ScreenSpell.Overlay
{
    /// <summary>
    /// Transparent, click-through window spanning the whole virtual desktop. Issue
    /// coordinates arrive in physical pixels and are converted to WPF device independent
    /// units before drawing.
    /// </summary>
    public partial class OverlayWindow : Window
    {
        private readonly OverlayRenderer _renderer;

        public OverlayWindow()
        {
            InitializeComponent();
            _renderer = new OverlayRenderer(OverlayCanvas);
            StretchOverVirtualScreen();
        }

        /// <summary>Draws a squiggle under every issue; an empty list clears the overlay.</summary>
        public void SetIssues(IReadOnlyList<SpellIssue> issues)
        {
            _renderer.Clear();
            if (issues is null || issues.Count == 0)
                return;

            var transform = PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
                            ?? Matrix.Identity;

            foreach (var issue in issues)
            {
                var topLeft = transform.Transform(new Point(issue.BoundingBox.X, issue.BoundingBox.Y));
                var bottomRight = transform.Transform(new Point(issue.BoundingBox.Right, issue.BoundingBox.Bottom));

                _renderer.DrawWaveUnderline(
                    topLeft.X - Left,
                    bottomRight.Y - Top,
                    bottomRight.X - topLeft.X);
            }
        }

        public void Clear() => _renderer.Clear();

        /// <summary>Re-covers the virtual desktop, e.g. after a monitor is plugged in.</summary>
        public void StretchOverVirtualScreen()
        {
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(
                handle,
                NativeMethods.GWL_EXSTYLE,
                style | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);
        }
    }

    internal static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TRANSPARENT = 0x00000020;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }
}
