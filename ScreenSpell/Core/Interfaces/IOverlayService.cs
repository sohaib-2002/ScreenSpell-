using ScreenSpell.Core.Models;

namespace ScreenSpell.Core.Interfaces
{
    public interface IOverlayService
    {
        void Show();

        void Hide();

        void Render(IReadOnlyList<SpellIssue> issues);

        void Clear();
    }
}
