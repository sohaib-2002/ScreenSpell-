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
    /// </summary>
    [SupportedOSPlatform("windows10.0.19041.0")]
    public class WindowsOcrProvider : IOcrProvider
    {
        private readonly ILogger<WindowsOcrProvider> _logger;
        private readonly OcrEngine? _engine;

        public WindowsOcrProvider(AppSettings? settings = null, ILogger<WindowsOcrProvider>? logger = null)
        {
            _logger = logger ?? NullLogger<WindowsOcrProvider>.Instance;

            var languageTag = string.IsNullOrWhiteSpace(settings?.Language) ? "ar" : settings!.Language;
            try
            {
                var language = new Language(languageTag);
                if (OcrEngine.IsLanguageSupported(language))
                {
                    _engine = OcrEngine.TryCreateFromLanguage(language);
                    LanguageTag = languageTag;
                }
                else
                {
                    _logger.LogWarning(
                        "The '{Language}' OCR language pack is not installed; falling back to the user profile languages. "
                        + "Install it with: Settings > Time & language > Language & region > Add a language.",
                        languageTag);
                    _engine = OcrEngine.TryCreateFromUserProfileLanguages();
                    LanguageTag = _engine?.RecognizerLanguage?.LanguageTag ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Windows OCR could not be initialised.");
                _engine = null;
            }
        }

        public bool IsAvailable => _engine is not null;

        /// <summary>Language actually used by the engine, empty when unavailable.</summary>
        public string LanguageTag { get; } = string.Empty;

        public async Task<List<OcrWord>> ExtractTextAsync(ScreenFrame frame, CancellationToken cancellationToken = default)
        {
            var words = new List<OcrWord>();
            if (_engine is null || frame is null)
                return words;

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var bitmap = ToSoftwareBitmap(frame);
                var result = await _engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);

                foreach (var line in result.Lines)
                {
                    foreach (var word in line.Words)
                    {
                        var rect = word.BoundingRect;
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
