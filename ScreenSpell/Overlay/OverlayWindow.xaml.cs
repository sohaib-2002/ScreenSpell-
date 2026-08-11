using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ScreenSpell.Core.Models;
using ScreenSpell.Core.Services;

namespace ScreenSpell.Overlay
{
    /// <summary>
    /// Transparent window spanning the whole virtual desktop. It is click-through everywhere
    /// except the underlines themselves, which open the correction menu. Issue coordinates
    /// arrive in physical pixels and are converted to WPF device independent units before
    /// drawing.
    /// </summary>
    public partial class OverlayWindow : Window
    {
        private readonly OverlayRenderer _renderer;
        private IReadOnlyList<SpellIssue> _issues = Array.Empty<SpellIssue>();

        public OverlayWindow()
        {
            InitializeComponent();
            _renderer = new OverlayRenderer(OverlayCanvas);
            StretchOverVirtualScreen();
        }

        /// <summary>Raised when the user picks an entry from the menu over a word.</summary>
        public event EventHandler<OverlayWordAction>? WordActionRequested;

        /// <summary>Draws a squiggle under every issue; an empty list clears the overlay.</summary>
        public void SetIssues(IReadOnlyList<SpellIssue> issues)
        {
            _renderer.Clear();
            _issues = issues ?? Array.Empty<SpellIssue>();

            if (_issues.Count == 0)
            {
                ApplyClickableBands();
                return;
            }

            var transform = TransformFromDevice();

            foreach (var issue in _issues)
            {
                var topLeft = transform.Transform(new Point(issue.BoundingBox.X, issue.BoundingBox.Y));
                var bottomRight = transform.Transform(new Point(issue.BoundingBox.Right, issue.BoundingBox.Bottom));

                _renderer.DrawWaveUnderline(
                    topLeft.X - Left,
                    bottomRight.Y - Top,
                    bottomRight.X - topLeft.X);
            }

            ApplyClickableBands();
        }

        public void Clear() => SetIssues(Array.Empty<SpellIssue>());

        /// <summary>Re-covers the virtual desktop, e.g. after a monitor is plugged in.</summary>
        public void StretchOverVirtualScreen()
        {
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;
        }

        /// <summary>
        /// Restricts the window to the underline strips, so the operating system routes every
        /// other click straight to the application below without us having to forward it.
        /// </summary>
        private void ApplyClickableBands()
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
                return;

            var origin = TransformToDevice().Transform(new Point(Left, Top));
            var region = NativeMethods.CreateRectRgn(0, 0, 0, 0);

            foreach (var issue in _issues)
            {
                var band = OverlayHitTester.BandOf(issue.BoundingBox);
                var left = band.X - (int)Math.Round(origin.X);
                var top = band.Y - (int)Math.Round(origin.Y);

                var strip = NativeMethods.CreateRectRgn(left, top, left + band.Width, top + band.Height);
                NativeMethods.CombineRgn(region, region, strip, NativeMethods.RGN_OR);
                NativeMethods.DeleteObject(strip);
            }

            // The window takes ownership of the region handle, so it must not be freed here.
            NativeMethods.SetWindowRgn(handle, region, true);
        }

        private Matrix TransformFromDevice() =>
            PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

        private Matrix TransformToDevice() =>
            PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            var point = TransformToDevice().Transform(e.GetPosition(this));
            var origin = TransformToDevice().Transform(new Point(Left, Top));

            var issue = OverlayHitTester.IssueAt(
                _issues,
                (int)Math.Round(point.X + origin.X),
                (int)Math.Round(point.Y + origin.Y));

            if (issue is null)
                return;

            e.Handled = true;
            ShowMenu(issue);
        }

        private void ShowMenu(SpellIssue issue)
        {
            var menu = new ContextMenu
            {
                PlacementTarget = OverlayCanvas,
                FlowDirection = FlowDirection.RightToLeft,
                IsOpen = false
            };

            if (issue.Suggestions.Count == 0)
            {
                menu.Items.Add(new MenuItem { Header = "لا توجد اقتراحات", IsEnabled = false });
            }
            else
            {
                foreach (var suggestion in issue.Suggestions)
                {
                    var item = new MenuItem { Header = suggestion, FontWeight = FontWeights.SemiBold };
                    item.Click += (_, _) => Raise(new OverlayWordAction(
                        OverlayActionKind.Suggestion,
                        issue.Word,
                        suggestion));
                    menu.Items.Add(item);
                }
            }

            menu.Items.Add(new Separator());

            var ignore = new MenuItem { Header = "تجاهل الكلمة" };
            ignore.Click += (_, _) => Raise(new OverlayWordAction(OverlayActionKind.Ignore, issue.Word));
            menu.Items.Add(ignore);

            var add = new MenuItem { Header = "إضافة إلى القاموس" };
            add.Click += (_, _) => Raise(new OverlayWordAction(OverlayActionKind.AddToDictionary, issue.Word));
            menu.Items.Add(add);

            menu.IsOpen = true;
        }

        private void Raise(OverlayWordAction action)
        {
            // The word is settled either way, so its underline goes now instead of lingering
            // until the next scan notices.
            SetIssues(_issues.Where(issue => issue.Word != action.Word).ToList());
            WordActionRequested?.Invoke(this, action);
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            var handle = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLong(handle, NativeMethods.GWL_EXSTYLE);

            // No WS_EX_TRANSPARENT: the window has to receive the clicks that land on an
            // underline. Everything else is excluded by the window region instead.
            NativeMethods.SetWindowLong(
                handle,
                NativeMethods.GWL_EXSTYLE,
                style | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW);

            // Without this the next capture contains our own squiggles, the frame hash changes
            // every pass and OCR keeps re-reading underlined text - the overlay flickers.
            // Needs Windows 10 2004; ignored on older builds.
            NativeMethods.SetWindowDisplayAffinity(handle, NativeMethods.WDA_EXCLUDEFROMCAPTURE);

            ApplyClickableBands();
        }
    }

    internal static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
        public const int RGN_OR = 2;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

        [DllImport("gdi32.dll")]
        public static extern int CombineRgn(IntPtr hrgnDest, IntPtr hrgnSrc1, IntPtr hrgnSrc2, int fnCombineMode);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr hObject);
    }
}
