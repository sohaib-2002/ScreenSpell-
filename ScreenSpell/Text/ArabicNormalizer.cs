using System.Text;
using System.Text.RegularExpressions;

namespace ScreenSpell.Text
{
    public static class ArabicNormalizer
    {
        private static readonly Regex DiacriticsRegex = new(@"[\u0610-\u061A\u064B-\u065F\u0670\u06D6-\u06ED]", RegexOptions.Compiled);
        private static readonly Regex TatweelRegex = new(@"\u0640+", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex ArabicLetterRegex = new(@"[\u0621-\u064A\u0671-\u06D3]", RegexOptions.Compiled);
        private static readonly Regex TrimmableRegex = new(@"^[^\p{L}\p{N}]+|[^\p{L}\p{N}]+$", RegexOptions.Compiled);

        /// <summary>
        /// Strips diacritics and tatweel and unifies the letter shapes that OCR engines
        /// tend to confuse, so a word can be compared against a dictionary entry.
        /// </summary>
        public static string Normalize(string? text, bool convertTehMarbutaToHeh = false)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var cleanText = DiacriticsRegex.Replace(text, string.Empty);
            cleanText = TatweelRegex.Replace(cleanText, string.Empty);

            var sb = new StringBuilder(cleanText.Length);
            foreach (var c in cleanText)
            {
                switch (c)
                {
                    case 'أ':
                    case 'إ':
                    case 'آ':
                    case 'ٱ':
                    case 'ٲ':
                    case 'ٳ':
                        sb.Append('ا');
                        break;
                    case 'ى':
                        sb.Append('ي');
                        break;
                    case 'ئ':
                        sb.Append('ي');
                        break;
                    case 'ؤ':
                        sb.Append('و');
                        break;
                    case 'ة':
                        sb.Append(convertTehMarbutaToHeh ? 'ه' : 'ة');
                        break;
                    case '٠': sb.Append('0'); break;
                    case '١': sb.Append('1'); break;
                    case '٢': sb.Append('2'); break;
                    case '٣': sb.Append('3'); break;
                    case '٤': sb.Append('4'); break;
                    case '٥': sb.Append('5'); break;
                    case '٦': sb.Append('6'); break;
                    case '٧': sb.Append('7'); break;
                    case '٨': sb.Append('8'); break;
                    case '٩': sb.Append('9'); break;
                    default:
                        sb.Append(c);
                        break;
                }
            }

            var result = WhitespaceRegex.Replace(sb.ToString(), " ").Trim();
            return result.Normalize(NormalizationForm.FormC);
        }

        /// <summary>Removes leading and trailing punctuation left over by the OCR pass.</summary>
        public static string TrimPunctuation(string? text) =>
            string.IsNullOrEmpty(text) ? string.Empty : TrimmableRegex.Replace(text, string.Empty);

        /// <summary>True when the token contains at least one Arabic letter.</summary>
        public static bool ContainsArabic(string? text) =>
            !string.IsNullOrEmpty(text) && ArabicLetterRegex.IsMatch(text);

        /// <summary>True when every letter of the token is Arabic.</summary>
        public static bool IsArabicWord(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var letters = 0;
            foreach (var c in text)
            {
                if (!char.IsLetter(c))
                    continue;

                letters++;
                if (!ArabicLetterRegex.IsMatch(c.ToString()))
                    return false;
            }

            return letters > 0;
        }
    }
}
