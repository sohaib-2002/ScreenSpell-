using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace ScreenSpell.Tray
{
    /// <summary>
    /// Notification area icon with a context menu. Uses the WinForms NotifyIcon, which is the
    /// only tray API available in the framework itself.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class SystemTrayManager : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly ToolStripMenuItem _scanItem;
        private bool _disposed;

        public SystemTrayManager()
        {
            _scanItem = new ToolStripMenuItem("بدء التدقيق", null, (_, _) => ScanToggleRequested?.Invoke(this, EventArgs.Empty));

            var menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem("إظهار النافذة", null, (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty)));
            menu.Items.Add(_scanItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("الإعدادات", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty)));
            menu.Items.Add(new ToolStripMenuItem("خروج", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty)));

            _notifyIcon = new NotifyIcon
            {
                Icon = LoadIcon(),
                Text = "ScreenSpell",
                Visible = true,
                ContextMenuStrip = menu
            };

            _notifyIcon.DoubleClick += (_, _) => ShowRequested?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler? ShowRequested;

        public event EventHandler? ScanToggleRequested;

        public event EventHandler? SettingsRequested;

        public event EventHandler? ExitRequested;

        /// <summary>Keeps the menu label in sync with the scan loop.</summary>
        public void SetScanning(bool scanning)
        {
            _scanItem.Text = scanning ? "إيقاف التدقيق" : "بدء التدقيق";
            _notifyIcon.Text = scanning ? "ScreenSpell - يعمل" : "ScreenSpell";
        }

        public void ShowMessage(string title, string message) =>
            _notifyIcon.ShowBalloonTip(3000, title, message, ToolTipIcon.Info);

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
        }

        private static Icon LoadIcon()
        {
            try
            {
                var executable = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(executable))
                {
                    var extracted = Icon.ExtractAssociatedIcon(executable);
                    if (extracted is not null)
                        return extracted;
                }
            }
            catch (Exception)
            {
                // Falls through to the stock application icon.
            }

            return SystemIcons.Application;
        }
    }
}
