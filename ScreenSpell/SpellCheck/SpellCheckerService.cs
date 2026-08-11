using ScreenSpell.Cache;
using ScreenSpell.Core.Interfaces;
using ScreenSpell.Core.Models;
using ScreenSpell.Text;

namespace ScreenSpell.SpellCheck
{
    /// <summary>
    /// Front door used by the rest of the application: applies the user dictionary and the
    /// ignore list, memoises results and delegates the actual decision to the configured
    /// engine (ONNX when a model is deployed, the word list otherwise).
    /// </summary>
    public class SpellCheckerService : ISpellChecker, IUserWordList
    {
        private readonly ISpellChecker _engine;
        private readonly SuggestionService _suggestions;
        private readonly SpellCache _cache;

        public SpellCheckerService(ISpellChecker engine, SuggestionService suggestions, SpellCache cache)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _suggestions = suggestions ?? throw new ArgumentNullException(nameof(suggestions));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public SpellResult CheckWord(string word)
        {
            var trimmed = ArabicNormalizer.TrimPunctuation(word);
            var normalized = ArabicNormalizer.Normalize(trimmed);
            if (normalized.Length == 0)
                return SpellResult.Correct(word ?? string.Empty);

            if (_suggestions.IsIgnoredOrValid(normalized))
                return SpellResult.Correct(normalized);

            if (_cache.TryGetValue(normalized, out var cached) && cached is not null)
                return cached;

            // The engine gets the original casing so it can recognise acronyms; the cache is
            // keyed on the normalized form so the ignore list can invalidate it.
            var result = _engine.CheckWord(trimmed);
            _cache.AddOrUpdate(normalized, result);
            return result;
        }

        public List<string> GetSuggestions(string word) => CheckWord(word).Suggestions;

        /// <summary>Accepts the word permanently and drops any cached verdict for it.</summary>
        public void AddToDictionary(string word)
        {
            _suggestions.AddToDictionary(word);
            _cache.Remove(ArabicNormalizer.Normalize(ArabicNormalizer.TrimPunctuation(word)));
        }

        /// <summary>Stops reporting the word for the rest of the session.</summary>
        public void IgnoreWord(string word)
        {
            _suggestions.IgnoreWord(word);
            _cache.Remove(ArabicNormalizer.Normalize(ArabicNormalizer.TrimPunctuation(word)));
        }
    }
}
