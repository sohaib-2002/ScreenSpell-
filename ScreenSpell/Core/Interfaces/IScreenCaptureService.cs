using ScreenSpell.Core.Models;

namespace ScreenSpell.Core.Interfaces
{
    public interface IScreenCaptureService
    {
        /// <summary>Refresh rate of the display, in hertz; the natural pace for watching it.</summary>
        int RefreshRateHz { get; }

        /// <summary>Grabs the primary screen as a BGRA frame.</summary>
        ScreenFrame CaptureScreen();

        /// <summary>
        /// Grabs the foreground window only. Far cheaper than the whole screen, which is what
        /// keeps the scan loop up with window switching. Returns null when there is nothing
        /// worth scanning: no foreground window, a minimized one, or one of our own.
        /// </summary>
        ScreenFrame? CaptureActiveWindow();

        /// <summary>
        /// A heavily downscaled copy of what <see cref="CaptureActiveWindow"/> or
        /// <see cref="CaptureScreen"/> would return, meant only for noticing that the screen
        /// changed. It costs a fraction of a full grab, which is what makes it affordable to
        /// look at the screen on every refresh. Returns null in the same cases as
        /// <see cref="CaptureActiveWindow"/>.
        /// </summary>
        ScreenFrame? CaptureProbe(bool activeWindowOnly);

        /// <summary>Grabs a rectangle of the virtual desktop as a BGRA frame.</summary>
        ScreenFrame CaptureRegion(int x, int y, int width, int height);
    }
}
