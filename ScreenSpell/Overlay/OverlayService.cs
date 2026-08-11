using System.Windows;
using System.Windows.Threading;
using ScreenSpell.Core.Interfaces;
using ScreenSpell.Core.Models;

namespace ScreenSpell.Overlay
{
    /// <summary>
    /// Owns the single <see cref="OverlayWindow"/> and marshals every call onto the UI thread
    /// so the scan loop can render from a background task.
    /// </summary>
    public sealed class OverlayService : IOverlayService, IDisposable
    {
        private readonly Dispatcher _dispatcher;
        private OverlayWindow? _window;
        private bool _disposed;

        public OverlayService(Dispatcher? dispatcher = null)
        {
            _dispatcher = dispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        }

        public event EventHandler<OverlayWordAction>? WordActionRequested;

        public void Show() => Invoke(window => window.Show());

        public void Hide() => Invoke(window => window.Hide());

        public void Render(IReadOnlyList<SpellIssue> issues) => Invoke(window =>
        {
            window.StretchOverVirtualScreen();
            window.SetIssues(issues);
            if (!window.IsVisible)
                window.Show();
        });

        public void Clear() => Invoke(window => window.Clear());

        private OverlayWindow CreateWindow()
        {
            var window = new OverlayWindow();
            window.WordActionRequested += (_, action) => WordActionRequested?.Invoke(this, action);
            return window;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _dispatcher.Invoke(() =>
            {
                _window?.Close();
                _window = null;
            });
        }

        private void Invoke(Action<OverlayWindow> action)
        {
            if (_disposed)
                return;

            _dispatcher.Invoke(() => action(_window ??= CreateWindow()));
        }
    }
}
