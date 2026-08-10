using Microsoft.Extensions.Logging;
using ScreenSpell.Cache;
using ScreenSpell.Core.Interfaces;
using ScreenSpell.Core.Models;
using ScreenSpell.Text;

namespace ScreenSpell.Main
{
    /// <summary>
    /// The scan loop: capture the screen, OCR it, spell check every word and hand the issues
    /// to the overlay. One iteration is skipped entirely when the screen did not change.
    /// </summary>
    public sealed class ScanService : IDisposable
    {
        private readonly IScreenCaptureService _capture;
        private readonly IOcrProvider _ocr;
        private readonly ISpellChecker _spellChecker;
        private readonly IOverlayService _overlay;
        private readonly OcrCache _ocrCache;
        private readonly ISettingsService _settings;
        private readonly ILogger<ScanService> _logger;

        private CancellationTokenSource? _cancellation;
        private Task? _loop;

        public ScanService(
            IScreenCaptureService capture,
            IOcrProvider ocr,
            ISpellChecker spellChecker,
            IOverlayService overlay,
            OcrCache ocrCache,
            ISettingsService settings,
            ILogger<ScanService> logger)
        {
            _capture = capture;
            _ocr = ocr;
            _spellChecker = spellChecker;
            _overlay = overlay;
            _ocrCache = ocrCache;
            _settings = settings;
            _logger = logger;
        }

        public event EventHandler<IReadOnlyList<SpellIssue>>? IssuesUpdated;

        public event EventHandler<string>? StatusChanged;

        public bool IsScanning => _loop is { IsCompleted: false };

        public void Start()
        {
            if (IsScanning)
                return;

            if (!_ocr.IsAvailable)
            {
                Report("محرك التعرف الضوئي غير متاح. ثبّت حزمة اللغة العربية من إعدادات Windows.");
                return;
            }

            _cancellation = new CancellationTokenSource();
            _loop = Task.Run(() => RunAsync(_cancellation.Token));
            Report("جارٍ التدقيق…");
        }

        public void Stop()
        {
            _cancellation?.Cancel();
            _overlay.Clear();
            Report("متوقف");
        }

        /// <summary>Runs a single pass, used by the "فحص الآن" button.</summary>
        public async Task ScanOnceAsync(CancellationToken cancellationToken = default)
        {
            _ocrCache.Invalidate();
            await ScanAsync(cancellationToken).ConfigureAwait(false);
        }

        public void Dispose()
        {
            _cancellation?.Cancel();
            _cancellation?.Dispose();
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await ScanAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Scan iteration failed.");
                    Report($"خطأ أثناء التدقيق: {ex.Message}");
                }

                var interval = Math.Max(200, _settings.Settings.ScanIntervalMs);
                try
                {
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task ScanAsync(CancellationToken cancellationToken)
        {
            var settings = _settings.Settings;
            var frame = _capture.CaptureScreen();

            if (_ocrCache.TryGetCachedFrame(frame, out var cachedWords))
            {
                Publish(BuildIssues(cachedWords, settings));
                return;
            }

            var words = await _ocr.ExtractTextAsync(frame, cancellationToken).ConfigureAwait(false);
            _ocrCache.UpdateCache(words);

            Publish(BuildIssues(words, settings));
        }

        private List<SpellIssue> BuildIssues(IEnumerable<OcrWord> words, AppSettings settings)
        {
            var issues = new List<SpellIssue>();

            foreach (var word in words)
            {
                if (word.Confidence < settings.MinOcrConfidence)
                    continue;

                var text = ArabicNormalizer.TrimPunctuation(word.Text);
                if (text.Length < settings.MinWordLength || !ArabicNormalizer.IsArabicWord(text))
                    continue;

                var result = _spellChecker.CheckWord(text);
                if (!result.IsError)
                    continue;

                issues.Add(new SpellIssue
                {
                    Word = text,
                    BoundingBox = word.BoundingBox,
                    Suggestions = result.Suggestions.Take(settings.MaxSuggestions).ToList(),
                    Confidence = result.Confidence
                });
            }

            return issues;
        }

        private void Publish(IReadOnlyList<SpellIssue> issues)
        {
            if (_settings.Settings.ShowOverlay)
                _overlay.Render(issues);
            else
                _overlay.Clear();

            IssuesUpdated?.Invoke(this, issues);
            Report($"تم العثور على {issues.Count} كلمة مشكوك بها");
        }

        private void Report(string status) => StatusChanged?.Invoke(this, status);
    }
}
