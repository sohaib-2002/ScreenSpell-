using Microsoft.Extensions.Logging;
using ScreenSpell.Cache;
using ScreenSpell.Core.Interfaces;
using ScreenSpell.Core.Models;
using ScreenSpell.Core.Services;
using ScreenSpell.Engines;
using ScreenSpell.Text;

namespace ScreenSpell.Main
{
    /// <summary>
    /// The scan loop: watch the screen, read it, spell check every word and hand the issues to
    /// the overlay. The screen is probed at its refresh rate but only fingerprinted, so the
    /// expensive part runs when the picture actually changed and then settled, instead of on
    /// a fixed timer that is either late or wasteful.
    ///
    /// Reading has three speeds, cheapest first: the application is asked for its text through
    /// UI Automation, which is instant and exact; failing that only the repainted part of the
    /// window is recognised and merged with the words already known; and only a move, a resize
    /// or a full repaint costs a whole-window recognition.
    /// </summary>
    public sealed class ScanService : IDisposable
    {
        private readonly IScreenCaptureService _capture;
        private readonly IOcrProvider _ocr;
        private readonly ITextSource? _textSource;
        private readonly ISpellChecker _spellChecker;
        private readonly IOverlayService _overlay;
        private readonly OcrCache _ocrCache;
        private readonly ISettingsService _settings;
        private readonly ILogger<ScanService> _logger;
        private readonly IssueStabilizer _stabilizer = new();
        private readonly DirtyRegionTracker _dirtyRegions = new();
        private IReadOnlyList<OcrWord> _lastWords = Array.Empty<OcrWord>();

        private CancellationTokenSource? _cancellation;
        private Task? _loop;
        private string _lastRendered = string.Empty;
        private string _lastRegion = string.Empty;
        private string _lastProcessed = string.Empty;
        private string? _pendingFingerprint;
        private int _refreshRateHz = 60;

        public ScanService(
            IScreenCaptureService capture,
            IOcrProvider ocr,
            ISpellChecker spellChecker,
            IOverlayService overlay,
            OcrCache ocrCache,
            ISettingsService settings,
            ILogger<ScanService> logger,
            ITextSource? textSource = null)
        {
            _capture = capture;
            _ocr = ocr;
            _textSource = textSource;
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

            if (!_ocr.IsAvailable && !(_settings.Settings.ReadTextDirectly && _textSource is { IsAvailable: true }))
            {
                Report("محرك التعرف الضوئي غير متاح. ثبّت حزمة اللغة العربية من إعدادات Windows.");
                return;
            }

            _stabilizer.Reset();
            _dirtyRegions.Reset();
            _refreshRateHz = Math.Clamp(_capture.RefreshRateHz, 24, 360);
            _cancellation = new CancellationTokenSource();
            _loop = Task.Run(() => RunAsync(_cancellation.Token));
            Report("جارٍ التدقيق…");
        }

        public void Stop()
        {
            _cancellation?.Cancel();
            _stabilizer.Reset();
            _dirtyRegions.Reset();
            _lastWords = Array.Empty<OcrWord>();
            _lastRendered = string.Empty;
            _lastRegion = string.Empty;
            _lastProcessed = string.Empty;
            _pendingFingerprint = null;
            _overlay.Clear();
            Report("متوقف");
        }

        /// <summary>Runs a single pass, used by the "فحص الآن" button.</summary>
        public async Task ScanOnceAsync(CancellationToken cancellationToken = default)
        {
            _ocrCache.Invalidate();
            _dirtyRegions.Reset();
            _lastProcessed = string.Empty;
            await ScanAsync(cancellationToken, stabilize: false, requireSettled: false).ConfigureAwait(false);
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
                var settings = _settings.Settings;
                try
                {
                    await ScanAsync(cancellationToken, requireSettled: settings.SyncToRefreshRate)
                        .ConfigureAwait(false);
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

                // In refresh mode a tick only costs a capture and a hash, so it can run as
                // often as the display is redrawn.
                var interval = settings.SyncToRefreshRate
                    ? Math.Max(8, 1000 / _refreshRateHz)
                    : Math.Max(200, settings.ScanIntervalMs);

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

        private async Task ScanAsync(
            CancellationToken cancellationToken,
            bool stabilize = true,
            bool requireSettled = false)
        {
            var settings = _settings.Settings;
            var frame = settings.ScanActiveWindowOnly
                ? _capture.CaptureActiveWindow()
                : _capture.CaptureScreen();

            // Nothing to read (our own window is in front, or everything is minimized): keep
            // whatever is already on screen instead of clearing and re-drawing it.
            if (frame is null)
            {
                _pendingFingerprint = null;
                return;
            }

            // Switching or moving a window invalidates every underline we are showing, so drop
            // them now instead of leaving them over unrelated content until the next scan.
            var region = $"{frame.OriginX},{frame.OriginY},{frame.Width}x{frame.Height}";
            if (region != _lastRegion)
            {
                _lastRegion = region;
                _stabilizer.Reset();
                _lastRendered = string.Empty;
                _lastProcessed = string.Empty;
                _lastWords = Array.Empty<OcrWord>();
                _ocrCache.Invalidate();
                _dirtyRegions.Reset();
                _overlay.Clear();
            }

            var fingerprint = OcrCache.FingerprintOf(frame);
            if (fingerprint == _lastProcessed)
            {
                _pendingFingerprint = null;
                return;
            }

            // The application knows its own text, so there is nothing to wait for and nothing
            // to recognise: the words are published on the very frame that changed.
            if (settings.ReadTextDirectly && _textSource is { IsAvailable: true })
            {
                var exposed = _textSource.TryReadForegroundWindow();
                if (exposed is { Count: > 0 })
                {
                    _pendingFingerprint = null;
                    _lastProcessed = fingerprint;
                    _lastWords = exposed;
                    _dirtyRegions.Reset();

                    // Exact text does not flicker between two passes, so there is nothing to
                    // smooth out and the underline appears on the frame the word was typed in.
                    Publish(BuildIssues(exposed, settings), stabilize: false);
                    return;
                }
            }

            if (!_ocr.IsAvailable)
                return;

            if (requireSettled)
            {
                // Recognising a frame while the window is still scrolling or animating only
                // produces garbage, so wait until two probes in a row look the same.
                if (fingerprint != _pendingFingerprint)
                {
                    _pendingFingerprint = fingerprint;
                    return;
                }
            }

            _pendingFingerprint = null;
            _lastProcessed = fingerprint;

            if (_ocrCache.TryGetCachedFrame(fingerprint, out var cachedWords))
            {
                Publish(BuildIssues(cachedWords, settings), stabilize);
                return;
            }

            var words = await RecognizeAsync(frame, settings, cancellationToken).ConfigureAwait(false);
            _lastWords = words;
            _ocrCache.UpdateCache(words);

            Publish(BuildIssues(words, settings), stabilize);
        }

        /// <summary>
        /// Recognises the frame, or just the repainted part of it. Typing a word repaints a
        /// couple of lines, and reading those costs a fraction of a full window pass, so the
        /// underline follows the caret instead of arriving a scan later.
        /// </summary>
        private async Task<List<OcrWord>> RecognizeAsync(
            ScreenFrame frame,
            AppSettings settings,
            CancellationToken cancellationToken)
        {
            var dirty = settings.IncrementalScan ? _dirtyRegions.Track(frame) : null;
            var partial = dirty is { } region
                          && _lastWords.Count > 0
                          && (region.Width < frame.Width || region.Height < frame.Height);

            var target = partial
                ? ImageOps.Crop(frame, dirty!.Value.X, dirty.Value.Y, dirty.Value.Width, dirty.Value.Height)
                : frame;

            var source = settings.EnhanceContrast ? FramePreprocessor.Enhance(target) : target;
            var words = await _ocr.ExtractTextAsync(source, cancellationToken).ConfigureAwait(false);

            if (!partial)
                return words;

            var desktopRegion = new PixelRect(
                frame.OriginX + dirty!.Value.X,
                frame.OriginY + dirty.Value.Y,
                dirty.Value.Width,
                dirty.Value.Height);

            return DirtyRegionTracker.Merge(_lastWords, words, desktopRegion);
        }

        private List<SpellIssue> BuildIssues(IEnumerable<OcrWord> words, AppSettings settings)
        {
            var issues = new List<SpellIssue>();

            foreach (var word in words)
            {
                // Text drawn this small is where the engine starts inventing letters.
                if (word.BoundingBox.Height > 0 && word.BoundingBox.Height < settings.MinTextHeight)
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
