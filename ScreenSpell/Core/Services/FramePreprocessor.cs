using ScreenSpell.Core.Models;

namespace ScreenSpell.Core.Services
{
    /// <summary>
    /// Prepares a captured frame for recognition. Screen text is thin and often drawn with
    /// sub-pixel antialiasing on a low contrast background, which is where the Windows engine
    /// starts confusing letters; converting to grey and stretching the contrast so the darkest
    /// and lightest pixels become black and white makes the glyph edges unambiguous.
    /// </summary>
    public static class FramePreprocessor
    {
        /// <summary>Ignore the extremes of the histogram so a single stray pixel cannot set the range.</summary>
        private const double ClipFraction = 0.005;

        /// <summary>A frame flatter than this carries no text worth sharpening.</summary>
        private const int MinRange = 16;

        public static ScreenFrame Enhance(ScreenFrame frame)
        {
            ArgumentNullException.ThrowIfNull(frame);

            var luma = new byte[frame.Width * frame.Height];
            var histogram = new int[256];

            for (var y = 0; y < frame.Height; y++)
            {
                var row = y * frame.Stride;
                var target = y * frame.Width;

                for (var x = 0; x < frame.Width; x++)
                {
                    var pixel = row + x * 4;
                    var value = (byte)((frame.Pixels[pixel] * 29 +
                                        frame.Pixels[pixel + 1] * 150 +
                                        frame.Pixels[pixel + 2] * 77) >> 8);

                    luma[target + x] = value;
                    histogram[value]++;
                }
            }

            var (low, high) = Range(histogram, luma.Length);
            var pixels = new byte[frame.Stride * frame.Height];
            var scale = high > low ? 255.0 / (high - low) : 0.0;

            for (var y = 0; y < frame.Height; y++)
            {
                var row = y * frame.Stride;
                var source = y * frame.Width;

                for (var x = 0; x < frame.Width; x++)
                {
                    var value = luma[source + x];
                    if (scale > 0)
                        value = (byte)Math.Clamp((value - low) * scale, 0, 255);

                    var pixel = row + x * 4;
                    pixels[pixel] = value;
                    pixels[pixel + 1] = value;
                    pixels[pixel + 2] = value;
                    pixels[pixel + 3] = 255;
                }
            }

            return new ScreenFrame(frame.Width, frame.Height, frame.Stride, pixels, frame.OriginX, frame.OriginY);
        }

        private static (int Low, int High) Range(int[] histogram, int pixelCount)
        {
            var clip = (int)(pixelCount * ClipFraction);

            var low = 0;
            var seen = 0;
            while (low < 255 && (seen += histogram[low]) <= clip)
                low++;

            var high = 255;
            seen = 0;
            while (high > low && (seen += histogram[high]) <= clip)
                high--;

            return high - low < MinRange ? (0, 255) : (low, high);
        }
    }
}
