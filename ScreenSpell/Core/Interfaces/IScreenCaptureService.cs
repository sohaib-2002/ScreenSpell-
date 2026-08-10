using ScreenSpell.Core.Models;

namespace ScreenSpell.Core.Interfaces
{
    public interface IScreenCaptureService
    {
        /// <summary>Grabs the primary screen as a BGRA frame.</summary>
        ScreenFrame CaptureScreen();

        /// <summary>Grabs a rectangle of the virtual desktop as a BGRA frame.</summary>
        ScreenFrame CaptureRegion(int x, int y, int width, int height);
    }
}
