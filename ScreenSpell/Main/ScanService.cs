using System.Diagnostics;
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
        private readonly IUserWordList? _userWords;
        private readonly IOverlayService _overlay;
        private readonly OcrCache _ocrCache;
        private readonly ISettingsService _settings;
        private readonly ILogger<ScanService> _logger;
        private readonly IssueStabilizer _stabilizer = new();
        private readonly DirtyRegionTracker _dirtyRegions = new();
        private IReadOnlyList<OcrWord> _lastWords = Array.Empty<OcrWord>();

        private readonly SemaphoreSlim _oneScanAtATime = new(1, 1);
        private readonly Stopwatch _sinceDirectRead = Stopwatch.StartNew();

        private CancellationTokenSource? _cancellation;
        private Task? _loop;
        private int _idleTicks;
        private bool _windowExposesText;
        private string _lastRendered = string.Empty;
        private string _lastRegion = string.Empty;
        private string _lastProcessed = string.Empty;
        private string? _pendingFingerprint;
        private int _refreshRateHz = 60;

        /// <summary>Walking the whole automation tree more often than this is wasted work.</summary>
        private static readonly TimeSpan DirectReadInterval = TimeSpan.FromMilliseconds(200);

        /// <summary>A still screen is probed this slowly, and instantly back at full pace once it moves.</summary>
        private const int IdleIntervalMs = 250;

        private const int TicksBeforeIdle = 40;

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
            _userWords = spellChecker as IUserWordList;

            _overlay.WordActionRequested += OnOverlayWordAction;
        }

        /// <summary>
        /// A word settled from the menu on the overlay: nothing here should send the user back
        /// to the main window, so the decision is applied and the word stops being reported
        /// from the next pass on.
        /// </summary>
        private void OnOverlayWordAction(object? sender, OverlayWordAction action)
        {
            switch (action.Kind)
            {
                case OverlayActionKind.Ignore:
                    _userWords?.IgnoreWord(action.Word);
                    _settings.Update(settings =>
                    {
                        if (!settings.IgnoredWords.Contains(action.Word))
                            settings.IgnoredWords.Add(action.Word);
                    });
                    Report($"تم تجاهل «{action.Word}»");
                    break;

                case OverlayActionKind.AddToDictionary:
                    _userWords?.AddToDictionary(action.Word);
                    _settings.Update(settings =>
                    {
                        if (!settings.UserDictionary.Contains(action.Word))
                            settings.UserDictionary.Add(action.Word);
                    });
                    Report($"تمت إضافة «{action.Word}» إلى القاموس");
                    break;

                case OverlayActionKind.Suggestion when action.Suggestion is { Length: > 0 } suggestion:
                    // We cannot type into someone else's window, so the correction is put where
                    // any application can take it: one paste away.
                    CopyToClipboard(suggestion);
                    Report($"تم نسخ «{suggestion}» — الصقها مكان الكلمة");
                    break;
            }

            // The published set changed underneath us, so the next identical scan must draw.
            _lastRendered = string.Empty;
            _lastProcessed = string.Empty;
        }

        private void CopyToClipboard(string text)
        {
            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher is null)
                    return;

                dispatcher.Invoke(() => System.Windows.Clipboard.SetText(text));
            }
            catch (Exception ex)
            {
                // Another process can hold the clipboard open; losing a copy is not worth a crash.
                _logger.LogWarning(ex, "Could not copy the suggestion to the clipboard.");
            }
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
            _idleTicks = 0;
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
            _windowExposesText = false;
            _idleTicks = 0;
            _overlay.Clear();
            Report("متوقف");
        }

        /// <summary>Runs a single pass, used by the "فحص الآن" button.</summary>
        public async Task ScanOnceAsync(CancellationToken cancellationToken = default)
        {
            _ocrCache.Invalidate();
            _dirtyRegions.Reset();
            _lastProcessed = string.Empty;
            _sinceDirectRead.Restart();
            await ScanAsync(cancellationToken, stabilize: false, requireSettled: false, waitForTurn: true)
                .ConfigureAwait(false);
        }

        public void Dispose()
        {
            _overlay.WordActionRequested -= OnOverlayWordAction;
            _cancellation?.Cancel();
            _cancellation?.Dispose();
            _oneScanAtATime.Dispose();
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

                // In refresh mode a tick only costs a downscaled probe and a hash, so it can
                // run as often as the display is redrawn - but a screen nobody is touching is
                // not worth watching sixty times a second, so the pace drops until it moves.
                var interval = settings.SyncToRefreshRate
                    ? _idleTicks >= TicksBeforeIdle
                        ? IdleIntervalMs
                        : Math.Max(8, 1000 / _refreshRateHz)
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
            bool requireSettled = false,
            bool waitForTurn = false)
        {
            // A pass that is still recognising must not be joined by the next tick, or the
            // machine ends up running several recognitions over the same window at once.
            if (waitForTurn)
                await _oneScanAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
            else if (!await _oneScanAtATime.WaitAsync(0, cancellationToken).ConfigureAwait(false))
                return;

            try
            {
                await ScanCoreAsync(cancellationToken, stabilize, requireSettled).ConfigureAwait(false);
            }
            finally
            {
                _oneScanAtATime.Release();
            }
        }

        private async Task ScanCoreAsync(
            CancellationToken cancellationToken,
            bool stabilize,
            bool requireSettled)
        {
            var settings = _settings.Settings;

            // Watching costs a downscaled grab, not a full one: a tick that finds the screen
            // unchanged - which is almost every tick - never touches a megabyte of pixels.
            var probe = _capture.CaptureProbe(settings.ScanActiveWindowOnly);

            // Nothing to read (our own window is in front, or everything is minimized): keep
            // whatever is already on screen instead of clearing and re-drawing it.
            if (probe is null)
            {
                _pendingFingerprint = null;
                return;
            }

            // Switching or moving a window invalidates every underline we are showing, so drop
            // them now instead of leaving them over unrelated content until the next scan.
            var region = $"{probe.OriginX},{probe.OriginY},{probe.Width}x{probe.Height}";
            if (region != _lastRegion)
            {
                _lastRegion = region;
                _stabilizer.Reset();
                _lastRendered = string.Empty;
                _lastProcessed = string.Empty;
                _lastWords = Array.Empty<OcrWord>();
                _windowExposesText = false;
                _ocrCache.Invalidate();
                _dirtyRegions.Reset();
                _overlay.Clear();
            }

            var fingerprint = OcrCache.FingerprintOf(probe);
            if (fingerprint == _lastProcessed)
            {
                _pendingFingerprint = null;
                _idleTicks++;
                return;
            }

            _idleTicks = 0;

            // The application knows its own text, so there is nothing to wait for and nothing
            // to recognise: the words are published on the very frame that changed. Walking
            // the automation tree is cheap but not free, so a window that keeps changing is
            // read at a steady pace instead of on every refresh.
            if (settings.ReadTextDirectly && _textSource is { IsAvailable: true })
            {
                if (_windowExposesText && _sinceDirectRead.Elapsed < DirectReadInterval)
                    return;

                _sinceDirectRead.Restart();
                var exposed = _textSource.TryReadForegroundWindow();
                _windowExposesText = exposed is { Count: > 0 };

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

            // Only now, with something actually worth recognising, is the window grabbed at
            // full resolution.
            var frame = settings.ScanActiveWindowOnly
                ? _capture.CaptureActiveWindow()
                : _capture.CaptureScreen();

            if (frame is null)
                return;

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
