using ScreenSpell.Core.Models;

namespace ScreenSpell.Core.Services
{
    /// <summary>
    /// Smooths the flicker caused by OCR returning slightly different words and boxes on every
    /// pass: a word has to be reported misspelled <c>stabilityFrames</c> times in a row before it
    /// is underlined, and its underline survives the same number of passes once it disappears.
    /// </summary>
    public sealed class IssueStabilizer
    {
        // Boxes move by a pixel or two between passes; quantizing keeps them the same issue.
        private const int PositionBucket = 12;

        private readonly Dictionary<string, Entry> _tracked = new(StringComparer.Ordinal);

        public void Reset() => _tracked.Clear();

        public IReadOnlyList<SpellIssue> Stabilize(IReadOnlyList<SpellIssue> current, int stabilityFrames)
        {
            if (stabilityFrames <= 1)
            {
                _tracked.Clear();
                return current;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var issue in current)
            {
                var key = MatchExisting(issue) ?? KeyOf(issue, 0, 0);
                seen.Add(key);

                if (_tracked.TryGetValue(key, out var entry))
                {
                    entry.Issue = issue;
                    entry.Misses = 0;
                    if (entry.Hits < stabilityFrames)
                        entry.Hits++;
                }
                else
                {
                    _tracked[key] = new Entry { Issue = issue, Hits = 1 };
                }
            }

            var stale = new List<string>();
            foreach (var (key, entry) in _tracked)
            {
                if (seen.Contains(key))
                    continue;

                entry.Misses++;
                if (entry.Misses >= stabilityFrames)
                    stale.Add(key);
            }

            foreach (var key in stale)
                _tracked.Remove(key);

            return _tracked.Values
                .Where(entry => entry.Hits >= stabilityFrames)
                .Select(entry => entry.Issue)
                .ToList();
        }

        /// <summary>Finds the key of the same word already tracked within one bucket in any direction.</summary>
        private string? MatchExisting(SpellIssue issue)
        {
            var exact = KeyOf(issue, 0, 0);
            if (_tracked.ContainsKey(exact))
                return exact;

            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    var key = KeyOf(issue, dx, dy);
                    if (_tracked.ContainsKey(key))
                        return key;
                }
            }

            return null;
        }

        private static string KeyOf(SpellIssue issue, int dx, int dy)
        {
            var word = issue.Word.ToLowerInvariant();
            var x = (int)issue.BoundingBox.X / PositionBucket + dx;
            var y = (int)issue.BoundingBox.Y / PositionBucket + dy;
            return $"{word}@{x},{y}";
        }

        private sealed class Entry
        {
            public required SpellIssue Issue { get; set; }
            public int Hits { get; set; }
            public int Misses { get; set; }
        }
    }
}
