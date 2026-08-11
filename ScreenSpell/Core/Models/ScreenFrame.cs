namespace ScreenSpell.Core.Models
{
    /// <summary>
    /// A captured screen image in 32bpp BGRA, kept free of any UI framework type so the
    /// capture, OCR and cache layers can be exercised without a windowing system.
    /// </summary>
    public sealed class ScreenFrame
    {
        public ScreenFrame(int width, int height, int stride, byte[] pixels, int originX = 0, int originY = 0)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            if (stride < width * 4) throw new ArgumentOutOfRangeException(nameof(stride));
            ArgumentNullException.ThrowIfNull(pixels);
            if (pixels.Length < stride * height) throw new ArgumentException("Pixel buffer is smaller than stride * height.", nameof(pixels));

            Width = width;
            Height = height;
            Stride = stride;
            Pixels = pixels;
            OriginX = originX;
            OriginY = originY;
        }

        public int Width { get; }

        public int Height { get; }

        public int Stride { get; }

        /// <summary>Raw BGRA pixels, <see cref="Stride"/> bytes per row.</summary>
        public byte[] Pixels { get; }

        /// <summary>Left coordinate of the frame inside the virtual desktop.</summary>
        public int OriginX { get; }

        /// <summary>Top coordinate of the frame inside the virtual desktop.</summary>
        public int OriginY { get; }
    }
}
