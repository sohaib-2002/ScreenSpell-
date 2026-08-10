using ScreenSpell.Core.Models;
using ScreenSpell.Engines;
using Xunit;

namespace ScreenSpell.Tests
{
    /// <summary>
    /// Exercises the offline engines on a rendered screenshot holding "التوقيع مدرسة" over
    /// "Signature book", the very words the Windows engine used to misread.
    /// </summary>
    public class OcrEngineTests
    {
        private static readonly string ModelDirectory =
            Path.Combine(AppContext.BaseDirectory, "Models");

        private static readonly string TessdataDirectory =
            Path.Combine(ModelDirectory, "tessdata");

        private static ScreenFrame Sample() =>
            BmpFixture.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.bmp"));

        [Fact]
        public void ResizePreservesTheCorners()
        {
            var frame = Sample();
            var resized = ImageOps.Resize(frame, frame.Width / 2, frame.Height / 2);

            Assert.Equal(frame.Width / 2, resized.Width);
            Assert.Equal(frame.Height / 2, resized.Height);
            // The fixture has a white margin all around.
            Assert.Equal(255, resized.Pixels[0]);
        }

        [Fact]
        public void CropStaysInsideTheFrame()
        {
            var frame = Sample();
            var crop = ImageOps.Crop(frame, -10, -10, 50, 50);

            Assert.True(crop.Width <= 50);
            Assert.True(crop.Height <= 50);
            Assert.Equal(0, crop.OriginX);
        }

        [Fact]
        public async Task PaddleReadsBothScripts()
        {
            using var provider = new PaddleOcrProvider(new AppSettings(), null, ModelDirectory);
            Assert.True(provider.IsAvailable);

            var words = await provider.ExtractTextAsync(Sample());
            var text = string.Join(' ', words.Select(word => word.Text));

            Assert.Contains("التوقيع", text);
            Assert.Contains("مدرسة", text);
            Assert.Contains("Signature", text);
            Assert.All(words, word => Assert.InRange(word.Confidence, 0.0, 1.0));
            Assert.All(words, word => Assert.True(word.BoundingBox.Width > 0));
        }

        [Fact]
        public async Task TesseractReadsBothScripts()
        {
            using var provider = new TesseractOcrProvider(new AppSettings(), null, TessdataDirectory);
            if (!provider.IsAvailable)
                return; // The native Tesseract binaries the NuGet package carries are Windows only.

            var words = await provider.ExtractTextAsync(Sample());
            var text = string.Join(' ', words.Select(word => word.Text));

            Assert.Contains("مدرسة", text);
            Assert.Contains("Signature", text);
        }

        /// <summary>
        /// An engine that cannot load its models must stay quiet instead of throwing, because
        /// the application falls back to the Windows engine on <c>IsAvailable</c>.
        /// </summary>
        [Fact]
        public async Task MissingModelsLeaveTheEnginesUnavailable()
        {
            using var paddle = new PaddleOcrProvider(new AppSettings(), null, Path.Combine(AppContext.BaseDirectory, "NoModels"));
            using var tesseract = new TesseractOcrProvider(new AppSettings(), null, Path.Combine(AppContext.BaseDirectory, "NoModels"));

            Assert.False(paddle.IsAvailable);
            Assert.False(tesseract.IsAvailable);
            Assert.Empty(await paddle.ExtractTextAsync(Sample()));
            Assert.Empty(await tesseract.ExtractTextAsync(Sample()));
        }
    }

    /// <summary>Reads the uncompressed 24bpp bitmap fixtures into a capture frame.</summary>
    internal static class BmpFixture
    {
        public static ScreenFrame Load(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var offset = BitConverter.ToInt32(bytes, 10);
            var width = BitConverter.ToInt32(bytes, 18);
            var height = BitConverter.ToInt32(bytes, 22);
            var bits = BitConverter.ToInt16(bytes, 28);
            if (bits != 24)
                throw new NotSupportedException($"{path} is {bits}bpp; the fixtures are 24bpp.");

            var sourceStride = (width * 3 + 3) / 4 * 4;
            var stride = width * 4;
            var pixels = new byte[stride * height];

            for (var y = 0; y < height; y++)
            {
                // Bitmaps are stored bottom-up.
                var source = offset + (height - 1 - y) * sourceStride;
                var target = y * stride;
                for (var x = 0; x < width; x++)
                {
                    pixels[target + x * 4] = bytes[source + x * 3];
                    pixels[target + x * 4 + 1] = bytes[source + x * 3 + 1];
                    pixels[target + x * 4 + 2] = bytes[source + x * 3 + 2];
                    pixels[target + x * 4 + 3] = 255;
                }
            }

            return new ScreenFrame(width, height, stride, pixels);
        }
    }
}
