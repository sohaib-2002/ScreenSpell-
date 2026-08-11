using ScreenSpell.Core.Models;

namespace ScreenSpell.Core.Interfaces
{
    public interface IOverlayService
    {
        /// <summary>Raised when the user picks an entry from the menu shown over a word.</summary>
        event EventHandler<OverlayWordAction>? WordActionRequested;

        void Show();

        void Hide();

        void Render(IReadOnlyList<SpellIssue> issues);

        void Clear();
    }
}
