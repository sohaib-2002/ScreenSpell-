using ScreenSpell.Cache;
using ScreenSpell.Core.Models;
using ScreenSpell.Settings;
using ScreenSpell.SpellCheck;
using ScreenSpell.Text;
using Xunit;

namespace ScreenSpell.Tests
{
    public class ArabicNormalizerTests
    {
        [Theory]
        [InlineData("مُحَمَّد", "محمد")]
        [InlineData("الــكــتــاب", "الكتاب")]
        [InlineData("إسلام", "اسلام")]
        [InlineData("مصطفى", "مصطفي")]
        [InlineData("١٢٣", "123")]
        public void NormalizeStripsDiacriticsAndUnifiesLetters(string input, string expected) =>
            Assert.Equal(expected, ArabicNormalizer.Normalize(input));

        [Fact]
        public void TrimPunctuationRemovesArabicAndLatinMarks() =>
            Assert.Equal("كتاب", ArabicNormalizer.TrimPunctuation("«كتاب»،"));

        [Fact]
        public void IsArabicWordRejectsMixedTokens()
        {
            Assert.True(ArabicNormalizer.IsArabicWord("كتاب"));
            Assert.False(ArabicNormalizer.IsArabicWord("book"));
            Assert.False(ArabicNormalizer.IsArabicWord("كتابbook"));
        }
    }

    public class EditDistanceTests
    {
        [Theory]
        [InlineData("كتاب", "كتاب", 0)]
        [InlineData("كتاب", "كتب", 1)]
        [InlineData("كتاب", "كاتب", 1)]
        [InlineData("مدرسة", "مدرسه", 1)]
        public void ComputesExpectedDistance(string a, string b, int expected) =>
            Assert.Equal(expected, EditDistance.Compute(a, b));

        [Fact]
        public void StopsEarlyWhenOverTheLimit() =>
            Assert.True(EditDistance.Compute("كتاب", "الطائرة", maxDistance: 2) > 2);
    }

    public class ArabicDictionaryTests
    {
        private static ArabicDictionary Build(params string[] words)
        {
            var dictionary = new ArabicDictionary();
            dictionary.AddRange(words);
            return dictionary;
        }

        [Fact]
        public void ContainsIgnoresDiacritics()
        {
            var dictionary = Build("كتاب");
            Assert.True(dictionary.Contains("كِتَاب"));
        }

        [Fact]
        public void ContainsWithAffixesPeelsCommonClitics()
        {
            var dictionary = Build("كتاب");
            Assert.True(dictionary.ContainsWithAffixes("الكتاب"));
            Assert.True(dictionary.ContainsWithAffixes("وكتابها"));
            Assert.False(dictionary.ContainsWithAffixes("سيارة"));
        }

        [Fact]
        public void CandidatesAreLimitedByLength()
        {
            var dictionary = Build("كتاب", "مستشفيات");
            Assert.Contains("كتاب", dictionary.CandidatesFor("كتب", 2));
            Assert.DoesNotContain("مستشفيات", dictionary.CandidatesFor("كتب", 2));
        }
    }

    public class DictionarySpellCheckerTests
    {
        private static SpellCheckerService Build(params string[] words)
        {
            var dictionary = new ArabicDictionary();
            dictionary.AddRange(words);
            var suggestions = new SuggestionService();
            var engine = new DictionarySpellChecker(dictionary, suggestions);
            return new SpellCheckerService(engine, suggestions, new SpellCache());
        }

        [Fact]
        public void KnownWordIsAccepted() =>
            Assert.False(Build("مدرسة").CheckWord("مدرسة").IsError);

        [Fact]
        public void UnknownWordIsReportedWithTheClosestSuggestion()
        {
            var result = Build("مدرسة", "مدينة").CheckWord("مدرصة");

            Assert.True(result.IsError);
            Assert.Equal("مدرسة", result.Suggestions.First());
        }

        [Fact]
        public void NonArabicTokensAreLeftAlone() =>
            Assert.False(Build("مدرسة").CheckWord("Windows").IsError);

        [Fact]
        public void AddToDictionaryClearsThePreviousVerdict()
        {
            var checker = Build("مدرسة");
            Assert.True(checker.CheckWord("سوهيب").IsError);

            checker.AddToDictionary("سوهيب");

            Assert.False(checker.CheckWord("سوهيب").IsError);
        }
    }

    public class OcrCacheTests
    {
        private static ScreenFrame Frame(byte fill)
        {
            var pixels = new byte[64 * 64 * 4];
            Array.Fill(pixels, fill);
            return new ScreenFrame(64, 64, 64 * 4, pixels);
        }

        [Fact]
        public void SecondIdenticalFrameHitsTheCache()
        {
            var cache = new OcrCache();
            Assert.False(cache.TryGetCachedFrame(Frame(0x10), out _));
            cache.UpdateCache(new List<OcrWord> { new() { Text = "كتاب" } });

            Assert.True(cache.TryGetCachedFrame(Frame(0x10), out var cached));
            Assert.Single(cached);
        }

        [Fact]
        public void ChangedFrameMissesTheCache()
        {
            var cache = new OcrCache();
            cache.TryGetCachedFrame(Frame(0x10), out _);
            Assert.False(cache.TryGetCachedFrame(Frame(0x20), out _));
        }
    }

    public class ConfigurationManagerTests
    {
        [Fact]
        public void SettingsRoundTripThroughDisk()
        {
            var path = Path.Combine(Path.GetTempPath(), $"screenspell-{Guid.NewGuid():N}.json");
            try
            {
                var manager = new ConfigurationManager(settingsPath: path);
                manager.Update(settings =>
                {
                    settings.ScanIntervalMs = 999;
                    settings.UserDictionary.Add("سوهيب");
                });

                var reloaded = new ConfigurationManager(settingsPath: path);

                Assert.Equal(999, reloaded.Settings.ScanIntervalMs);
                Assert.Contains("سوهيب", reloaded.Settings.UserDictionary);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
