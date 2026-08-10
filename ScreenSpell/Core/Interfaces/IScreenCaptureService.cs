using ScreenSpell.Core.Models;

namespace ScreenSpell.Core.Interfaces
{
    public interface IScreenCaptureService
    {
        /// <summary>Grabs the primary screen as a BGRA frame.</summary>
        ScreenFrame CaptureScreen();

        /// <summary>
        /// Grabs the foreground window only. Far cheaper than the whole screen, which is what
        /// keeps the scan loop up with window switching. Returns null when there is nothing
        /// worth scanning: no foreground window, a minimized one, or one of our own.
        /// </summary>
        ScreenFrame? CaptureActiveWindow();

        /// <summary>Grabs a rectangle of the virtual desktop as a BGRA frame.</summary>
        ScreenFrame CaptureRegion(int x, int y, int width, int height);
    }
}
