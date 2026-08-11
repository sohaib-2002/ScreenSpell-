using ScreenSpell.Core.Models;

namespace ScreenSpell.Core.Interfaces
{
    /// <summary>
    /// Reads the text of the window in front from the application itself instead of from a
    /// picture of it. When the application exposes its text (browsers, Office, editors, most
    /// Windows controls) this is exact and takes microseconds, so no recognition, no upscale
    /// and no waiting for the picture to settle is needed.
    /// </summary>
    public interface ITextSource
    {
        bool IsAvailable { get; }

        /// <summary>
        /// Words of the foreground window in desktop pixels, or null when the window exposes
        /// no text and the caller has to fall back to reading the screen.
        /// </summary>
        IReadOnlyList<OcrWord>? TryReadForegroundWindow();
    }
}
