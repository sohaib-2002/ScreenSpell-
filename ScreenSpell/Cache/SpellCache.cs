using System.Collections.Concurrent;
using ScreenSpell.Core.Models;

namespace ScreenSpell.Cache
{
    /// <summary>Memoises spell results so a word is only checked once per session.</summary>
    public class SpellCache
    {
        private readonly ConcurrentDictionary<string, SpellResult> _cache = new(StringComparer.Ordinal);

        public int Count => _cache.Count;

        public void AddOrUpdate(string word, SpellResult result)
        {
            if (!string.IsNullOrEmpty(word))
                _cache[word] = result;
        }

        public bool TryGetValue(string word, out SpellResult? result)
        {
            if (string.IsNullOrEmpty(word))
            {
                result = null;
                return false;
            }

            return _cache.TryGetValue(word, out result);
        }

        public void Remove(string word) => _cache.TryRemove(word, out _);

        public void Clear() => _cache.Clear();
    }
}
