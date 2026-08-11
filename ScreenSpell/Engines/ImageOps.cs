using ScreenSpell.Core.Models;

namespace ScreenSpell.Engines
{
    /// <summary>
    /// The few pixel operations the offline engines need. Kept dependency free so the
    /// recognition pipeline can be exercised without a windowing system.
    /// </summary>
    public static class ImageOps
    {
        /// <summary>Bilinear resample of a BGRA frame, origin and alpha preserved.</summary>
        public static ScreenFrame Resize(ScreenFrame frame, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(frame);
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

            if (width == frame.Width && height == frame.Height)
                return frame;

            var stride = width * 4;
            var pixels = new byte[stride * height];
            var scaleX = (double)frame.Width / width;
            var scaleY = (double)frame.Height / height;

            for (var y = 0; y < height; y++)
            {
                var sourceY = Math.Clamp((y + 0.5) * scaleY - 0.5, 0, frame.Height - 1);
                var y0 = (int)sourceY;
                var y1 = Math.Min(y0 + 1, frame.Height - 1);
                var wy = sourceY - y0;

                for (var x = 0; x < width; x++)
                {
                    var sourceX = Math.Clamp((x + 0.5) * scaleX - 0.5, 0, frame.Width - 1);
                    var x0 = (int)sourceX;
                    var x1 = Math.Min(x0 + 1, frame.Width - 1);
                    var wx = sourceX - x0;

                    var target = y * stride + x * 4;
                    for (var channel = 0; channel < 4; channel++)
                    {
                        var top = frame.Pixels[y0 * frame.Stride + x0 * 4 + channel] * (1 - wx) +
                                  frame.Pixels[y0 * frame.Stride + x1 * 4 + channel] * wx;
                        var bottom = frame.Pixels[y1 * frame.Stride + x0 * 4 + channel] * (1 - wx) +
                                     frame.Pixels[y1 * frame.Stride + x1 * 4 + channel] * wx;

                        pixels[target + channel] = (byte)Math.Clamp(top * (1 - wy) + bottom * wy, 0, 255);
                    }
                }
            }

            return new ScreenFrame(width, height, stride, pixels, frame.OriginX, frame.OriginY);
        }

        /// <summary>Copies a rectangle out of a frame; the rectangle is clamped to the frame.</summary>
        public static ScreenFrame Crop(ScreenFrame frame, int x, int y, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(frame);

            x = Math.Clamp(x, 0, frame.Width - 1);
            y = Math.Clamp(y, 0, frame.Height - 1);
            width = Math.Clamp(width, 1, frame.Width - x);
            height = Math.Clamp(height, 1, frame.Height - y);

            var stride = width * 4;
            var pixels = new byte[stride * height];

            for (var row = 0; row < height; row++)
                Buffer.BlockCopy(frame.Pixels, (y + row) * frame.Stride + x * 4, pixels, row * stride, stride);

            return new ScreenFrame(width, height, stride, pixels, frame.OriginX + x, frame.OriginY + y);
        }

        /// <summary>
        /// Encodes the frame as a 24 bit bottom-up BMP, the format every Leptonica build reads.
        /// </summary>
        public static byte[] ToBmp(ScreenFrame frame)
        {
            ArgumentNullException.ThrowIfNull(frame);

            var rowSize = (frame.Width * 3 + 3) / 4 * 4;
            var imageSize = rowSize * frame.Height;
            var bytes = new byte[54 + imageSize];

            bytes[0] = (byte)'B';
            bytes[1] = (byte)'M';
            WriteInt32(bytes, 2, bytes.Length);
            WriteInt32(bytes, 10, 54);
            WriteInt32(bytes, 14, 40);
            WriteInt32(bytes, 18, frame.Width);
            WriteInt32(bytes, 22, frame.Height);
            bytes[26] = 1;
            bytes[28] = 24;
            WriteInt32(bytes, 34, imageSize);
            WriteInt32(bytes, 38, 2835);
            WriteInt32(bytes, 42, 2835);

            for (var y = 0; y < frame.Height; y++)
            {
                var source = y * frame.Stride;
                var target = 54 + (frame.Height - 1 - y) * rowSize;

                for (var x = 0; x < frame.Width; x++)
                {
                    bytes[target + x * 3] = frame.Pixels[source + x * 4];
                    bytes[target + x * 3 + 1] = frame.Pixels[source + x * 4 + 1];
                    bytes[target + x * 3 + 2] = frame.Pixels[source + x * 4 + 2];
                }
            }

            return bytes;
        }

        private static void WriteInt32(byte[] bytes, int offset, int value)
        {
            bytes[offset] = (byte)value;
            bytes[offset + 1] = (byte)(value >> 8);
            bytes[offset + 2] = (byte)(value >> 16);
            bytes[offset + 3] = (byte)(value >> 24);
        }
    }
}
