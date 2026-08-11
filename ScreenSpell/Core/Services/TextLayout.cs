using ScreenSpell.Core.Models;

namespace ScreenSpell.Core.Services
{
    /// <summary>
    /// Turns a label and the rectangle it is drawn in into one box per word. Controls such as
    /// buttons or menu items report their whole caption and a single rectangle, so the words
    /// are spread over it by character count, right to left when the line is Arabic.
    /// </summary>
    public static class TextLayout
    {
        public static List<OcrWord> SplitLine(string? text, BoundingBox box, double confidence = 1.0)
        {
            var words = new List<OcrWord>();
            if (string.IsNullOrWhiteSpace(text) || box.Width <= 0 || box.Height <= 0)
                return words;

            var rightToLeft = IsRightToLeft(text);
            var length = text.Length;

            var index = 0;
            while (index < length)
            {
                while (index < length && char.IsWhiteSpace(text[index]))
                    index++;

                var start = index;
                while (index < length && !char.IsWhiteSpace(text[index]))
                    index++;

                if (index == start)
                    break;

                var from = (double)start / length;
                var to = (double)index / length;

                // Only the running direction differs: the first word of an Arabic line is drawn
                // against the right edge, not the left one.
                var x = rightToLeft
                    ? box.Right - to * box.Width
                    : box.X + from * box.Width;

                words.Add(new OcrWord
                {
                    Text = text[start..index],
                    BoundingBox = new BoundingBox(x, box.Y, (to - from) * box.Width, box.Height),
                    Confidence = confidence
                });
            }

            return words;
        }

        /// <summary>Direction of the line, decided by its first letter as the bidi rules do.</summary>
        public static bool IsRightToLeft(string text)
        {
            foreach (var character in text)
            {
                if (character is >= '\u0590' and <= '\u08FF' or >= '\uFB1D' and <= '\uFEFC')
                    return true;

                if (char.IsLetter(character))
                    return false;
            }

            return false;
        }
    }
}
