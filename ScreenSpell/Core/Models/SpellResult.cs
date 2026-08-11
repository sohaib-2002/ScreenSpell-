namespace ScreenSpell.Core.Models
{
    public class SpellResult
    {
        public required string Word { get; set; }

        public bool IsError { get; set; }

        public double Confidence { get; set; }

        public required List<string> Suggestions { get; set; }

        public static SpellResult Correct(string word, double confidence = 1.0) =>
            new() { Word = word, IsError = false, Confidence = confidence, Suggestions = new List<string>() };

        public static SpellResult Error(string word, List<string> suggestions, double confidence) =>
            new() { Word = word, IsError = true, Confidence = confidence, Suggestions = suggestions };
    }
}
