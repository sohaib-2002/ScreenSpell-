namespace ScreenSpell.SpellCheck
{
    public static class EditDistance
    {
        /// <summary>
        /// Optimal string alignment distance (Damerau-Levenshtein restricted to adjacent
        /// transpositions). Returns <paramref name="maxDistance"/> + 1 as soon as the
        /// distance is known to exceed the limit.
        /// </summary>
        public static int Compute(string source, string target, int maxDistance = int.MaxValue)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(target);

            if (source == target)
                return 0;
            if (source.Length == 0)
                return target.Length;
            if (target.Length == 0)
                return source.Length;
            if (Math.Abs(source.Length - target.Length) > maxDistance)
                return maxDistance + 1;

            var previousPrevious = new int[target.Length + 1];
            var previous = new int[target.Length + 1];
            var current = new int[target.Length + 1];

            for (var j = 0; j <= target.Length; j++)
                previous[j] = j;

            for (var i = 1; i <= source.Length; i++)
            {
                current[0] = i;
                var rowMinimum = current[0];

                for (var j = 1; j <= target.Length; j++)
                {
                    var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                    var value = Math.Min(
                        Math.Min(current[j - 1] + 1, previous[j] + 1),
                        previous[j - 1] + cost);

                    if (i > 1 && j > 1 && source[i - 1] == target[j - 2] && source[i - 2] == target[j - 1])
                        value = Math.Min(value, previousPrevious[j - 2] + 1);

                    current[j] = value;
                    rowMinimum = Math.Min(rowMinimum, value);
                }

                if (rowMinimum > maxDistance)
                    return maxDistance + 1;

                (previousPrevious, previous, current) = (previous, current, previousPrevious);
            }

            return previous[target.Length];
        }
    }
}
