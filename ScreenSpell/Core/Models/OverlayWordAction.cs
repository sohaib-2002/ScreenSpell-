namespace ScreenSpell.Core.Models
{
    /// <summary>What the user picked from the menu that pops up over an underlined word.</summary>
    public enum OverlayActionKind
    {
        /// <summary>A correction was chosen; <see cref="OverlayWordAction.Suggestion"/> holds it.</summary>
        Suggestion,

        /// <summary>Stop reporting the word for this session.</summary>
        Ignore,

        /// <summary>Stop reporting the word for good.</summary>
        AddToDictionary
    }

    /// <summary>
    /// A decision taken on the overlay itself, so a correction never requires going back to
    /// the main window.
    /// </summary>
    public sealed record OverlayWordAction(OverlayActionKind Kind, string Word, string? Suggestion = null);
}
