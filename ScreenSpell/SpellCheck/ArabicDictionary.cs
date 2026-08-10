using ScreenSpell.Text;

namespace ScreenSpell.SpellCheck
{
    /// <summary>
    /// In-memory word list, indexed by word length so suggestion lookups only scan
    /// candidates that can possibly be within the requested edit distance.
    /// </summary>
    public sealed class ArabicDictionary
    {
        private static readonly char[] HunspellSeparators = { '/', '\t' };
        private static readonly string[] Prefixes = { "وال", "بال", "كال", "فال", "لل", "ال", "و", "ف", "ب", "ك", "ل", "س" };
        private static readonly string[] Suffixes = { "هما", "كما", "تين", "تان", "هم", "هن", "كم", "كن", "نا", "ها", "ات", "ان", "ون", "ين", "وا", "ية", "ه", "ك", "ي", "ا" };

        private readonly HashSet<string> _words = new(StringComparer.Ordinal);
        private readonly Dictionary<int, List<string>> _byLength = new();

        public int Count => _words.Count;

        public IReadOnlyCollection<string> Words => _words;

        public bool Add(string? word)
        {
            var normalized = ArabicNormalizer.Normalize(ArabicNormalizer.TrimPunctuation(word));
            if (normalized.Length == 0 || normalized.Contains(' '))
                return AddPhrase(normalized);

            return AddNormalized(normalized);
        }

        public int AddRange(IEnumerable<string> words)
        {
            ArgumentNullException.ThrowIfNull(words);
            return words.Count(Add);
        }

        /// <summary>
        /// Loads every "*.txt" and "*.dic" file in <paramref name="directory"/>. Lines
        /// starting with '#' are comments and Hunspell affix flags ("word/NM") are dropped.
        /// </summary>
        public int LoadDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return 0;

            var added = 0;
            foreach (var file in Directory.EnumerateFiles(directory, "*.*", SearchOption.AllDirectories))
            {
                var extension = Path.GetExtension(file);
                if (!extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) &&
                    !extension.Equals(".dic", StringComparison.OrdinalIgnoreCase))
                    continue;

                added += LoadFile(file);
            }

            return added;
        }

        public int LoadFile(string path)
        {
            if (!File.Exists(path))
                return 0;

            var added = 0;
            foreach (var rawLine in File.ReadLines(path))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;

                // Hunspell entries look like "word/FLAGS\tmorphological fields".
                var cut = line.IndexOfAny(HunspellSeparators);
                if (cut > 0)
                    line = line[..cut];

                // The first line of a Hunspell .dic file is the entry count.
                if (line.All(char.IsAsciiDigit))
                    continue;

                if (Add(line))
                    added++;
            }

            return added;
        }

        public bool Contains(string? word)
        {
            var normalized = ArabicNormalizer.Normalize(ArabicNormalizer.TrimPunctuation(word));
            return normalized.Length > 0 && _words.Contains(normalized);
        }

        /// <summary>
        /// True when the word is known once the common clitics (ال، و، ب، ل، ها، هم …)
        /// are peeled off. Keeps the false positive rate down on a small word list.
        /// </summary>
        public bool ContainsWithAffixes(string? word)
        {
            var normalized = ArabicNormalizer.Normalize(ArabicNormalizer.TrimPunctuation(word));
            if (normalized.Length == 0)
                return false;
            if (_words.Contains(normalized))
                return true;

            foreach (var stem in StripAffixes(normalized))
            {
                if (_words.Contains(stem))
                    return true;
            }

            return false;
        }

        /// <summary>Dictionary entries whose length is within <paramref name="maxDistance"/> of the word.</summary>
        public IEnumerable<string> CandidatesFor(string normalizedWord, int maxDistance)
        {
            for (var length = normalizedWord.Length - maxDistance; length <= normalizedWord.Length + maxDistance; length++)
            {
                if (length <= 0)
                    continue;
                if (_byLength.TryGetValue(length, out var bucket))
                {
                    foreach (var candidate in bucket)
                        yield return candidate;
                }
            }
        }

        private static IEnumerable<string> StripAffixes(string word)
        {
            foreach (var prefix in Prefixes)
            {
                if (word.Length > prefix.Length + 1 && word.StartsWith(prefix, StringComparison.Ordinal))
                {
                    var withoutPrefix = word[prefix.Length..];
                    yield return withoutPrefix;
                    foreach (var suffix in Suffixes)
                    {
                        if (withoutPrefix.Length > suffix.Length + 1 && withoutPrefix.EndsWith(suffix, StringComparison.Ordinal))
                            yield return withoutPrefix[..^suffix.Length];
                    }
                }
            }

            foreach (var suffix in Suffixes)
            {
                if (word.Length > suffix.Length + 1 && word.EndsWith(suffix, StringComparison.Ordinal))
                    yield return word[..^suffix.Length];
            }
        }

        private bool AddPhrase(string phrase)
        {
            var added = false;
            foreach (var part in phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                added |= AddNormalized(part);

            return added;
        }

        private bool AddNormalized(string normalized)
        {
            if (normalized.Length == 0 || !_words.Add(normalized))
                return false;

            if (!_byLength.TryGetValue(normalized.Length, out var bucket))
            {
                bucket = new List<string>();
                _byLength[normalized.Length] = bucket;
            }

            bucket.Add(normalized);
            return true;
        }
    }
}
