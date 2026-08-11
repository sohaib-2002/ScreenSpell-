using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScreenSpell.Core.Interfaces;
using ScreenSpell.Core.Models;
using Tesseract;
using OcrWord = ScreenSpell.Core.Models.OcrWord;

namespace ScreenSpell.Engines
{
    /// <summary>
    /// Tesseract 5 reading from the trained data bundled with the app. One engine handles all
    /// configured languages at once ("ara+eng"), and it reports a real per word confidence.
    /// </summary>
    public sealed class TesseractOcrProvider : IOcrProvider, IDisposable
    {
        private readonly ILogger _logger;
        private readonly TesseractEngine? _engine;
        private readonly double _scale;
        private readonly SemaphoreSlim _gate = new(1, 1);

        public TesseractOcrProvider(
            AppSettings? settings = null,
            ILogger<TesseractOcrProvider>? logger = null,
            string? dataDirectory = null)
        {
            _logger = logger ?? NullLogger<TesseractOcrProvider>.Instance;
            _scale = Math.Clamp(settings?.OcrScale ?? 2.0, 1.0, 4.0);

            var directory = dataDirectory ?? Path.Combine(AppContext.BaseDirectory, "Models", "tessdata");
            var languages = Languages(settings, directory);

            if (languages.Length == 0)
            {
                _logger.LogWarning("No Tesseract trained data found in {Directory}.", directory);
                return;
            }

            try
            {
                _engine = new TesseractEngine(directory, languages, EngineMode.LstmOnly);
                _engine.DefaultPageSegMode = PageSegMode.SparseText;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not start Tesseract with '{Languages}' from {Directory}.", languages, directory);
                _engine = null;
            }
        }

        public bool IsAvailable => _engine is not null;

        public async Task<List<OcrWord>> ExtractTextAsync(ScreenFrame frame, CancellationToken cancellationToken = default)
        {
            if (_engine is null || frame is null)
                return new List<OcrWord>();

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await Task.Run(() => Recognise(frame, cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tesseract failed on a {Width}x{Height} frame.", frame.Width, frame.Height);
                return new List<OcrWord>();
            }
            finally
            {
                _gate.Release();
            }
        }

        private List<OcrWord> Recognise(ScreenFrame frame, CancellationToken cancellationToken)
        {
            var words = new List<OcrWord>();

            // Small UI text is what Tesseract struggles with, so it reads an enlarged copy.
            var scaled = _scale > 1.0
                ? ImageOps.Resize(frame, (int)(frame.Width * _scale), (int)(frame.Height * _scale))
                : frame;

            using var image = Pix.LoadFromMemory(ImageOps.ToBmp(scaled));
            using var page = _engine!.Process(image);
            using var iterator = page.GetIterator();
            iterator.Begin();

            do
            {
                cancellationToken.ThrowIfCancellationRequested();

                var text = iterator.GetText(PageIteratorLevel.Word);
                if (string.IsNullOrWhiteSpace(text) || !iterator.TryGetBoundingBox(PageIteratorLevel.Word, out var box))
                    continue;

                words.Add(new OcrWord
                {
                    Text = text.Trim(),
                    Confidence = iterator.GetConfidence(PageIteratorLevel.Word) / 100.0,
                    BoundingBox = new BoundingBox(
                        frame.OriginX + box.X1 / _scale,
                        frame.OriginY + box.Y1 / _scale,
                        box.Width / _scale,
                        box.Height / _scale)
                });
            }
            while (iterator.Next(PageIteratorLevel.Word));

            return words;
        }

        /// <summary>Keeps the configured languages that actually have trained data on disk.</summary>
        private static string Languages(AppSettings? settings, string directory)
        {
            if (!Directory.Exists(directory))
                return string.Empty;

            var tags = new List<string> { string.IsNullOrWhiteSpace(settings?.Language) ? "ar" : settings!.Language };
            if (settings?.AdditionalLanguages is { Count: > 0 } extra)
                tags.AddRange(extra);

            var available = tags
                .Select(ToTesseractCode)
                .Where(code => code.Length > 0 && File.Exists(Path.Combine(directory, code + ".traineddata")))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            return string.Join('+', available);
        }

        /// <summary>Maps a BCP 47 tag such as "ar-EG" to the three letter code Tesseract uses.</summary>
        private static string ToTesseractCode(string tag) =>
            (tag ?? string.Empty).Split('-')[0].ToLowerInvariant() switch
            {
                "ar" => "ara",
                "en" => "eng",
                "fr" => "fra",
                "de" => "deu",
                "es" => "spa",
                "tr" => "tur",
                "fa" => "fas",
                "ur" => "urd",
                var other => other
            };

        public void Dispose()
        {
            _engine?.Dispose();
            _gate.Dispose();
        }
    }
}
