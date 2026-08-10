using ScreenSpell.Text;

namespace ScreenSpell.SpellCheck
{
    /// <summary>Ranks candidate corrections and keeps the user dictionary / ignore list.</summary>
    public class SuggestionService
    {
        private readonly HashSet<string> _userDictionary = new(StringComparer.Ordinal);
        private readonly HashSet<string> _ignoredWords = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> UserDictionary => _userDictionary;

        public IReadOnlyCollection<string> IgnoredWords => _ignoredWords;

        public void AddToDictionary(string? word)
        {
            var normalized = ArabicNormalizer.Normalize(ArabicNormalizer.TrimPunctuation(word));
            if (normalized.Length > 0)
                _userDictionary.Add(normalized);
        }

        public void IgnoreWord(string? word)
        {
            var normalized = ArabicNormalizer.Normalize(ArabicNormalizer.TrimPunctuation(word));
            if (normalized.Length > 0)
                _ignoredWords.Add(normalized);
        }

        public bool IsIgnoredOrValid(string? word)
        {
            var normalized = ArabicNormalizer.Normalize(ArabicNormalizer.TrimPunctuation(word));
            return normalized.Length > 0 && (_ignoredWords.Contains(normalized) || _userDictionary.Contains(normalized));
        }

        public void Reset()
        {
            _userDictionary.Clear();
            _ignoredWords.Clear();
        }

        /// <summary>Orders candidates by edit distance to the original word, closest first.</summary>
        public List<string> GetTopSuggestions(string? originalWord, IEnumerable<string>? candidates, int maxSuggestions = 5)
        {
            if (string.IsNullOrWhiteSpace(originalWord) || candidates is null || maxSuggestions <= 0)
                return new List<string>();

            var normalized = ArabicNormalizer.Normalize(originalWord);

            return candidates
                .Where(s => !string.IsNullOrWhiteSpace(s) && s != originalWord && s != normalized)
                .Distinct(StringComparer.Ordinal)
                .Select(s => (Word: s, Distance: EditDistance.Compute(normalized, ArabicNormalizer.Normalize(s))))
                .OrderBy(x => x.Distance)
                .ThenBy(x => Math.Abs(x.Word.Length - normalized.Length))
                .ThenBy(x => x.Word, StringComparer.Ordinal)
                .Take(maxSuggestions)
                .Select(x => x.Word)
                .ToList();
        }
    }
}
