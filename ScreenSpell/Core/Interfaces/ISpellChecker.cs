using ScreenSpell.Core.Models;

namespace ScreenSpell.Core.Interfaces
{
    public interface ISpellChecker
    {
        SpellResult CheckWord(string word);

        List<string> GetSuggestions(string word);
    }
}
