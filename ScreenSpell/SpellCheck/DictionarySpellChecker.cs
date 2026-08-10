using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScreenSpell.Core.Interfaces;
using ScreenSpell.Core.Models;
using ScreenSpell.Text;

namespace ScreenSpell.SpellCheck
{
    /// <summary>
    /// Word list based checker. It is the default engine and the fallback used whenever
    /// no ONNX model is deployed next to the executable.
    /// </summary>
    public sealed class DictionarySpellChecker : ISpellChecker
    {
        private const int MaxEditDistance = 2;

        private readonly ArabicDictionary _dictionary;
        private readonly SuggestionService _suggestions;
        private readonly ILogger<DictionarySpellChecker> _logger;
        private readonly int _minWordLength;
        private readonly int _maxSuggestions;

        public DictionarySpellChecker(
            ArabicDictionary dictionary,
            SuggestionService suggestions,
            AppSettings? settings = null,
            ILogger<DictionarySpellChecker>? logger = null)
        {
            _dictionary = dictionary ?? throw new ArgumentNullException(nameof(dictionary));
            _suggestions = suggestions ?? throw new ArgumentNullException(nameof(suggestions));
            _logger = logger ?? NullLogger<DictionarySpellChecker>.Instance;
            _minWordLength = settings?.MinWordLength ?? 3;
            _maxSuggestions = settings?.MaxSuggestions ?? 5;

            _logger.LogInformation("Dictionary spell checker ready with {Count} entries.", _dictionary.Count);
        }

        public SpellResult CheckWord(string word)
        {
            var trimmed = ArabicNormalizer.TrimPunctuation(word);
            var normalized = ArabicNormalizer.Normalize(trimmed);

            // The original casing matters here: normalization lower cases Latin text, which
            // would hide acronyms and camel case identifiers.
            if (normalized.Length == 0 || !ArabicNormalizer.IsCheckableWord(trimmed))
                return SpellResult.Correct(word ?? string.Empty);

            // Very short tokens are almost always OCR noise or particles.
            if (normalized.Length < _minWordLength)
                return SpellResult.Correct(word);

            if (_suggestions.IsIgnoredOrValid(normalized) || _dictionary.ContainsWithAffixes(normalized))
                return SpellResult.Correct(word);

            var candidates = _dictionary
                .CandidatesFor(normalized, MaxEditDistance)
                .Where(candidate => EditDistance.Compute(normalized, candidate, MaxEditDistance) <= MaxEditDistance);

            var ranked = _suggestions.GetTopSuggestions(normalized, candidates, _maxSuggestions);

            // Confidence grows with how sure we are that the word is unknown: a word with a
            // near neighbour in the dictionary is a likely typo, an isolated one may just be
            // missing from the word list.
            var confidence = ranked.Count > 0 ? 0.9 : 0.6;
            return SpellResult.Error(trimmed, ranked, confidence);
        }

        public List<string> GetSuggestions(string word) => CheckWord(word).Suggestions;
    }
}
