namespace ScreenSpell.Core.Models
{
    /// <summary>A misspelled word together with where it was found on screen.</summary>
    public sealed class SpellIssue
    {
        public required string Word { get; init; }

        public required BoundingBox BoundingBox { get; init; }

        public required IReadOnlyList<string> Suggestions { get; init; }

        public double Confidence { get; init; }

        public string SuggestionText => Suggestions.Count == 0 ? string.Empty : string.Join("، ", Suggestions);
    }
}
