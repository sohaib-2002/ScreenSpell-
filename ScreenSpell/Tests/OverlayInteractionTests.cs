using ScreenSpell.Core.Models;
using ScreenSpell.Core.Services;
using Xunit;

namespace ScreenSpell.Tests
{
    public class OverlayInteractionTests
    {
        private static SpellIssue Issue(string word, double x, double y, double width, double height) => new()
        {
            Word = word,
            BoundingBox = new BoundingBox(x, y, width, height),
            Suggestions = new List<string> { "مدرسة" }
        };

        [Fact]
        public void TheClickableStripHugsTheUnderlineNotTheWholeWord()
        {
            var band = OverlayHitTester.BandOf(new BoundingBox(100, 50, 80, 30));

            Assert.Equal(100, band.X);
            Assert.Equal(80, band.Width);
            Assert.Equal(80 - OverlayHitTester.BandRadius, band.Y);
            Assert.Equal(OverlayHitTester.BandRadius * 2, band.Height);
        }

        [Fact]
        public void RightClickOnTheUnderlineFindsItsWord()
        {
            var issues = new List<SpellIssue> { Issue("مدرصة", 100, 50, 80, 30) };

            Assert.Equal("مدرصة", OverlayHitTester.IssueAt(issues, 140, 80)?.Word);
        }

        [Fact]
        public void ClickingBesideOrAboveTheUnderlineHitsNothing()
        {
            var issues = new List<SpellIssue> { Issue("مدرصة", 100, 50, 80, 30) };

            Assert.Null(OverlayHitTester.IssueAt(issues, 140, 60));
            Assert.Null(OverlayHitTester.IssueAt(issues, 40, 80));
            Assert.Null(OverlayHitTester.IssueAt(issues, 240, 80));
        }

        [Fact]
        public void OverlappingUnderlinesResolveToTheWordAimedAt()
        {
            var issues = new List<SpellIssue>
            {
                Issue("جملة كاملة", 100, 50, 200, 30),
                Issue("كاملة", 150, 50, 40, 30)
            };

            Assert.Equal("كاملة", OverlayHitTester.IssueAt(issues, 160, 80)?.Word);
        }

        [Fact]
        public void AnEmptyScreenSwallowsNoClicks()
        {
            Assert.Null(OverlayHitTester.IssueAt(Array.Empty<SpellIssue>(), 10, 10));
        }
    }
}
