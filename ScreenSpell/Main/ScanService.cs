using Microsoft.Extensions.Logging;
using ScreenSpell.Cache;
using ScreenSpell.Core.Interfaces;
using ScreenSpell.Core.Models;
using ScreenSpell.Core.Services;
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
        private readonly IssueStabilizer _stabilizer = new();

        private CancellationTokenSource? _cancellation;
        private Task? _loop;
        private string _lastRendered = string.Empty;
        private string _lastRegion = string.Empty;

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

            _stabilizer.Reset();
            _cancellation = new CancellationTokenSource();
            _loop = Task.Run(() => RunAsync(_cancellation.Token));
            Report("جارٍ التدقيق…");
        }

        public void Stop()
        {
            _cancellation?.Cancel();
            _stabilizer.Reset();
            _lastRendered = string.Empty;
            _lastRegion = string.Empty;
            _overlay.Clear();
            Report("متوقف");
        }

        /// <summary>Runs a single pass, used by the "فحص الآن" button.</summary>
        public async Task ScanOnceAsync(CancellationToken cancellationToken = default)
        {
            _ocrCache.Invalidate();
            await ScanAsync(cancellationToken, stabilize: false).ConfigureAwait(false);
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

        private async Task ScanAsync(CancellationToken cancellationToken, bool stabilize = true)
        {
            var settings = _settings.Settings;
            var frame = settings.ScanActiveWindowOnly
                ? _capture.CaptureActiveWindow()
                : _capture.CaptureScreen();

            // Nothing to read (our own window is in front, or everything is minimized): keep
            // whatever is already on screen instead of clearing and re-drawing it.
            if (frame is null)
                return;

            // Switching or moving a window invalidates every underline we are showing, so drop
            // them now instead of leaving them over unrelated content until the next scan.
            var region = $"{frame.OriginX},{frame.OriginY},{frame.Width}x{frame.Height}";
            if (region != _lastRegion)
            {
                _lastRegion = region;
                _stabilizer.Reset();
                _lastRendered = string.Empty;
                _ocrCache.Invalidate();
                _overlay.Clear();
            }

            if (_ocrCache.TryGetCachedFrame(frame, out var cachedWords))
            {
                Publish(BuildIssues(cachedWords, settings), stabilize);
                return;
            }

            var words = await _ocr.ExtractTextAsync(frame, cancellationToken).ConfigureAwait(false);
            _ocrCache.UpdateCache(words);

            Publish(BuildIssues(words, settings), stabilize);
        }

        private List<SpellIssue> BuildIssues(IEnumerable<OcrWord> words, AppSettings settings)
        {
            var issues = new List<SpellIssue>();

            foreach (var word in words)
            {
                if (word.Confidence < settings.MinOcrConfidence)
                    continue;

                var text = ArabicNormalizer.TrimPunctuation(word.Text);
                if (text.Length < settings.MinWordLength || !ArabicNormalizer.IsCheckableWord(text))
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

        private void Publish(IReadOnlyList<SpellIssue> raw, bool stabilize)
        {
            // A one-off scan has no history to smooth against, so it shows what it found.
            var issues = stabilize
                ? _stabilizer.Stabilize(raw, _settings.Settings.StabilityFrames)
                : raw;

            if (!_settings.Settings.ShowOverlay)
            {
                _lastRendered = string.Empty;
                _overlay.Clear();
            }
            else
            {
                // Re-drawing an identical set is what makes the squiggles blink.
                var fingerprint = Fingerprint(issues);
                if (fingerprint != _lastRendered)
                {
                    _overlay.Render(issues);
                    _lastRendered = fingerprint;
                }
            }

            IssuesUpdated?.Invoke(this, issues);
            Report($"تم العثور على {issues.Count} كلمة مشكوك بها");
        }

        private static string Fingerprint(IReadOnlyList<SpellIssue> issues) =>
            string.Join(
                "|",
                issues.Select(i => $"{i.Word}:{(int)i.BoundingBox.X},{(int)i.BoundingBox.Y},{(int)i.BoundingBox.Width}"));

        private void Report(string status) => StatusChanged?.Invoke(this, status);
    }
}
