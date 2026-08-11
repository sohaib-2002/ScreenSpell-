using System.Security.Cryptography;
using ScreenSpell.Core.Models;

namespace ScreenSpell.Cache
{
    /// <summary>
    /// Skips the OCR pass while the screen does not change. The frame is sub-sampled before
    /// hashing so an unchanged 4K desktop costs a few hundred microseconds instead of a full
    /// recognition round trip.
    /// </summary>
    public class OcrCache
    {
        private const int SampleStep = 8;

        private readonly object _gate = new();
        private string _lastFrameHash = string.Empty;
        private List<OcrWord> _lastWords = new();

        /// <summary>
        /// Fingerprint of a frame, cheap enough to run at the refresh rate of the display.
        /// </summary>
        public static string FingerprintOf(ScreenFrame frame)
        {
            ArgumentNullException.ThrowIfNull(frame);
            return ComputeHash(frame);
        }

        public bool TryGetCachedFrame(ScreenFrame? frame, out List<OcrWord> cachedWords)
        {
            if (frame is null)
            {
                cachedWords = new List<OcrWord>();
                return false;
            }

            return TryGetCachedFrame(ComputeHash(frame), out cachedWords);
        }

        /// <summary>Same as <see cref="TryGetCachedFrame(ScreenFrame,out List{OcrWord})"/> for an already computed fingerprint.</summary>
        public bool TryGetCachedFrame(string hash, out List<OcrWord> cachedWords)
        {
            lock (_gate)
            {
                if (hash.Length > 0 && hash == _lastFrameHash)
                {
                    cachedWords = _lastWords;
                    return true;
                }

                _lastFrameHash = hash;
                cachedWords = new List<OcrWord>();
                return false;
            }
        }

        public void UpdateCache(List<OcrWord> words)
        {
            lock (_gate)
            {
                _lastWords = words ?? new List<OcrWord>();
            }
        }

        public void Invalidate()
        {
            lock (_gate)
            {
                _lastFrameHash = string.Empty;
                _lastWords = new List<OcrWord>();
            }
        }

        private static string ComputeHash(ScreenFrame frame)
        {
            var sampledRows = (frame.Height + SampleStep - 1) / SampleStep;
            var sampledColumns = (frame.Width + SampleStep - 1) / SampleStep;
            var buffer = new byte[sampledRows * sampledColumns * 4];

            var offset = 0;
            for (var y = 0; y < frame.Height; y += SampleStep)
            {
                var rowStart = y * frame.Stride;
                for (var x = 0; x < frame.Width; x += SampleStep)
                {
                    var pixel = rowStart + x * 4;
                    buffer[offset++] = frame.Pixels[pixel];
                    buffer[offset++] = frame.Pixels[pixel + 1];
                    buffer[offset++] = frame.Pixels[pixel + 2];
                    buffer[offset++] = frame.Pixels[pixel + 3];
                }
            }

            return Convert.ToBase64String(SHA256.HashData(buffer));
        }
    }
}
