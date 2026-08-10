using System.Runtime.InteropServices.WindowsRuntime;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScreenSpell.Core.Interfaces;
using ScreenSpell.Core.Models;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using OcrWord = ScreenSpell.Core.Models.OcrWord;

namespace ScreenSpell.OCR
{
    /// <summary>
    /// Wraps the OCR engine shipped with Windows 10/11. Recognition for a language is only
    /// possible when its optional "Basic typing / OCR" feature is installed, so the provider
    /// reports <see cref="IsAvailable"/> instead of throwing when Arabic is missing.
    /// One engine is created per configured language and the frame is recognised with each of
    /// them, because a single engine only ever returns text in its own script.
    /// </summary>
    [SupportedOSPlatform("windows10.0.19041.0")]
    public class WindowsOcrProvider : IOcrProvider
    {
        private readonly ILogger<WindowsOcrProvider> _logger;
        private readonly List<OcrEngine> _engines = new();

        public WindowsOcrProvider(AppSettings? settings = null, ILogger<WindowsOcrProvider>? logger = null)
        {
            _logger = logger ?? NullLogger<WindowsOcrProvider>.Instance;

            var primary = string.IsNullOrWhiteSpace(settings?.Language) ? "ar" : settings!.Language;
            var tags = new List<string> { primary };
            if (settings?.AdditionalLanguages is { Count: > 0 } extra)
                tags.AddRange(extra);

            foreach (var tag in tags.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var engine = TryCreateEngine(tag);
                if (engine is not null)
                    _engines.Add(engine);
            }

            if (_engines.Count == 0)
            {
                _logger.LogWarning(
                    "None of the configured OCR language packs ({Languages}) is installed; falling back to the user "
                    + "profile languages. Install one with: Settings > Time & language > Language & region > Add a language.",
                    string.Join(", ", tags));

                var fallback = OcrEngine.TryCreateFromUserProfileLanguages();
                if (fallback is not null)
                    _engines.Add(fallback);
            }

            LanguageTag = string.Join(", ", _engines.Select(e => e.RecognizerLanguage?.LanguageTag).Where(t => t is not null));
        }

        public bool IsAvailable => _engines.Count > 0;

        /// <summary>Languages actually used by the engines, empty when unavailable.</summary>
        public string LanguageTag { get; } = string.Empty;

        public async Task<List<OcrWord>> ExtractTextAsync(ScreenFrame frame, CancellationToken cancellationToken = default)
        {
            var words = new List<OcrWord>();
            if (_engines.Count == 0 || frame is null)
                return words;

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var bitmap = ToSoftwareBitmap(frame);
                // Every engine reads the whole frame, so the same word can come back twice.
                var seen = new HashSet<string>(StringComparer.Ordinal);

                foreach (var engine in _engines)
                {
                    var result = await engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);

                    foreach (var line in result.Lines)
                    {
                        foreach (var word in line.Words)
                        {
                            var rect = word.BoundingRect;
                            if (!seen.Add($"{word.Text}@{(int)rect.X},{(int)rect.Y}"))
                                continue;

                            words.Add(new OcrWord
                            {
                                Text = word.Text,
                                BoundingBox = new BoundingBox(
                                    rect.X + frame.OriginX,
                                    rect.Y + frame.OriginY,
                                    rect.Width,
                                    rect.Height),
                                // Windows.Media.Ocr does not expose a per word score.
                                Confidence = 1.0
                            });
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OCR failed for a {Width}x{Height} frame.", frame.Width, frame.Height);
            }

            return words;
        }

        private OcrEngine? TryCreateEngine(string tag)
        {
            try
            {
                var language = new Language(tag);
                if (!OcrEngine.IsLanguageSupported(language))
                {
                    _logger.LogWarning("The '{Language}' OCR language pack is not installed.", tag);
                    return null;
                }

                return OcrEngine.TryCreateFromLanguage(language);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not create an OCR engine for '{Language}'.", tag);
                return null;
            }
        }

        private static SoftwareBitmap ToSoftwareBitmap(ScreenFrame frame)
        {
            var packedStride = frame.Width * 4;
            byte[] pixels;

            if (frame.Stride == packedStride)
            {
                pixels = frame.Pixels;
            }
            else
            {
                pixels = new byte[packedStride * frame.Height];
                for (var y = 0; y < frame.Height; y++)
                    Buffer.BlockCopy(frame.Pixels, y * frame.Stride, pixels, y * packedStride, packedStride);
            }

            var bitmap = new SoftwareBitmap(BitmapPixelFormat.Bgra8, frame.Width, frame.Height, BitmapAlphaMode.Premultiplied);
            bitmap.CopyFromBuffer(pixels.AsBuffer());
            return bitmap;
        }
    }
}
