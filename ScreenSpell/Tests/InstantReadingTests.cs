using ScreenSpell.Core.Models;
using ScreenSpell.Core.Services;
using Xunit;

namespace ScreenSpell.Tests
{
    public class InstantReadingTests
    {
        [Fact]
        public void TheFirstFrameCountsAsFullyRepainted()
        {
            var tracker = new DirtyRegionTracker();

            var region = tracker.Track(Frame(320, 240, 0xFF));

            Assert.Equal(new PixelRect(0, 0, 320, 240), region);
        }

        [Fact]
        public void AnIdenticalFrameIsNotReadAgain()
        {
            var tracker = new DirtyRegionTracker();
            tracker.Track(Frame(320, 240, 0xFF));

            Assert.Null(tracker.Track(Frame(320, 240, 0xFF)));
        }

        [Fact]
        public void OnlyTheRepaintedPartIsReported()
        {
            var tracker = new DirtyRegionTracker();
            tracker.Track(Frame(640, 480, 0xFF));

            var typed = Frame(640, 480, 0xFF);
            Paint(typed, 200, 300, 40, 20);
            var region = tracker.Track(typed);

            Assert.NotNull(region);
            Assert.True(region!.Value.X <= 200 && region.Value.Y <= 300);
            Assert.True(region.Value.Right >= 240 && region.Value.Bottom >= 320);
            Assert.True(region.Value.Area < 640 * 480, "a small edit must not cost a whole frame");
        }

        [Fact]
        public void ARepaintOfEverythingFallsBackToTheWholeFrame()
        {
            var tracker = new DirtyRegionTracker();
            tracker.Track(Frame(320, 240, 0xFF));

            var region = tracker.Track(Frame(320, 240, 0x20));

            Assert.Equal(new PixelRect(0, 0, 320, 240), region);
        }

        [Fact]
        public void MovingTheWindowInvalidatesEverything()
        {
            var tracker = new DirtyRegionTracker();
            tracker.Track(Frame(320, 240, 0xFF));

            var region = tracker.Track(Frame(320, 240, 0xFF, originX: 100));

            Assert.Equal(new PixelRect(0, 0, 320, 240), region);
        }

        [Fact]
        public void WordsOutsideTheRepaintKeepTheirPreviousReading()
        {
            var known = new[]
            {
                Word("مدرسة", 10, 10),
                Word("كتاب", 400, 400)
            };

            var merged = DirtyRegionTracker.Merge(
                known,
                new[] { Word("التوقيع", 380, 390) },
                new PixelRect(350, 350, 200, 120));

            Assert.Equal(new[] { "مدرسة", "التوقيع" }, merged.Select(w => w.Text));
        }

        [Fact]
        public void ALatinCaptionIsSpreadFromTheLeft()
        {
            var words = TextLayout.SplitLine("Save file", new BoundingBox(100, 50, 90, 20));

            Assert.Equal(new[] { "Save", "file" }, words.Select(w => w.Text));
            Assert.True(words[0].BoundingBox.X < words[1].BoundingBox.X);
            Assert.Equal(50, words[0].BoundingBox.Y);
            Assert.All(words, word => Assert.True(word.BoundingBox.Width > 0));
        }

        [Fact]
        public void AnArabicCaptionIsSpreadFromTheRight()
        {
            var box = new BoundingBox(100, 50, 90, 20);

            var words = TextLayout.SplitLine("حفظ الملف", box);

            Assert.Equal(new[] { "حفظ", "الملف" }, words.Select(w => w.Text));
            Assert.True(words[0].BoundingBox.X > words[1].BoundingBox.X);
            Assert.True(words[0].BoundingBox.Right <= box.Right + 0.001);
            Assert.True(words[1].BoundingBox.X >= box.X - 0.001);
        }

        [Fact]
        public void AnEmptyCaptionYieldsNothing()
        {
            Assert.Empty(TextLayout.SplitLine("   ", new BoundingBox(0, 0, 50, 10)));
            Assert.Empty(TextLayout.SplitLine("word", new BoundingBox(0, 0, 0, 0)));
        }

        private static OcrWord Word(string text, double x, double y) => new()
        {
            Text = text,
            BoundingBox = new BoundingBox(x, y, 60, 18),
            Confidence = 1.0
        };

        private static ScreenFrame Frame(int width, int height, byte value, int originX = 0, int originY = 0)
        {
            var stride = width * 4;
            var pixels = new byte[stride * height];
            Array.Fill(pixels, value);
            return new ScreenFrame(width, height, stride, pixels, originX, originY);
        }

        private static void Paint(ScreenFrame frame, int x, int y, int width, int height)
        {
            for (var row = y; row < y + height; row++)
            {
                for (var column = x; column < x + width; column++)
                {
                    var pixel = row * frame.Stride + column * 4;
                    frame.Pixels[pixel] = 0;
                    frame.Pixels[pixel + 1] = 0;
                    frame.Pixels[pixel + 2] = 0;
                }
            }
        }
    }
}
