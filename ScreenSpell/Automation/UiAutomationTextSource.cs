using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScreenSpell.Core.Interfaces;
using ScreenSpell.Core.Models;
using ScreenSpell.Core.Services;

namespace ScreenSpell.Automation
{
    /// <summary>
    /// Reads the foreground window through UI Automation, the same channel screen readers use.
    /// Text comes back as the application stores it, so there is nothing to recognise and no
    /// misread letters: a document exposes a real text pattern and is walked word by word, and
    /// plain controls give their caption plus the rectangle it is drawn in.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class UiAutomationTextSource : ITextSource
    {
        /// <summary>Stops a huge tree (a long page, a spreadsheet) from stalling the loop.</summary>
        private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(120);

        private const int MaxElements = 800;
        private const int MaxWords = 1200;
        private const int MaxWordLength = 64;

        private readonly ILogger<UiAutomationTextSource> _logger;

        public UiAutomationTextSource(ILogger<UiAutomationTextSource>? logger = null)
        {
            _logger = logger ?? NullLogger<UiAutomationTextSource>.Instance;

            try
            {
                IsAvailable = AutomationElement.RootElement is not null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UI Automation is not usable; the screen will be read with OCR only.");
                IsAvailable = false;
            }
        }

        public bool IsAvailable { get; }

        public IReadOnlyList<OcrWord>? TryReadForegroundWindow()
        {
            if (!IsAvailable)
                return null;

            var handle = NativeMethods.GetForegroundWindow();
            if (handle == IntPtr.Zero)
                return null;

            // Reading our own window would only report the words already listed in it.
            NativeMethods.GetWindowThreadProcessId(handle, out var processId);
            if (processId == Environment.ProcessId)
                return null;

            try
            {
                var window = AutomationElement.FromHandle(handle);
                if (window is null)
                    return null;

                return Read(window);
            }
            catch (ElementNotAvailableException)
            {
                return null;
            }
            catch (COMException ex)
            {
                _logger.LogDebug(ex, "The foreground window refused the automation request.");
                return null;
            }
        }

        private List<OcrWord>? Read(AutomationElement window)
        {
            var clock = Stopwatch.StartNew();
            var words = new List<OcrWord>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            // One batched cross-process call: asking each element for its name separately is
            // what makes naive automation code slower than OCR.
            var request = new CacheRequest { AutomationElementMode = AutomationElementMode.Full };
            request.Add(AutomationElement.NameProperty);
            request.Add(AutomationElement.BoundingRectangleProperty);
            request.Add(AutomationElement.IsPasswordProperty);
            request.Add(TextPattern.Pattern);

            AutomationElementCollection elements;
            using (request.Activate())
            {
                elements = window.FindAll(
                    TreeScope.Element | TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.IsOffscreenProperty, false));
            }

            var count = Math.Min(elements.Count, MaxElements);
            for (var index = 0; index < count && words.Count < MaxWords && clock.Elapsed < Budget; index++)
            {
                var element = elements[index];
                try
                {
                    if (Equals(element.GetCachedPropertyValue(AutomationElement.IsPasswordProperty), true))
                        continue;

                    var bounds = (System.Windows.Rect)element.GetCachedPropertyValue(
                        AutomationElement.BoundingRectangleProperty);
                    if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
                        continue;

                    if (element.TryGetCachedPattern(TextPattern.Pattern, out var cached)
                        && cached is TextPattern text)
                    {
                        ReadDocument(text, words, seen, clock);
                        continue;
                    }

                    var name = element.GetCachedPropertyValue(AutomationElement.NameProperty) as string;
                    if (string.IsNullOrWhiteSpace(name) || name.Length > 512)
                        continue;

                    var box = new BoundingBox(bounds.X, bounds.Y, bounds.Width, bounds.Height);
                    foreach (var word in TextLayout.SplitLine(name, box))
                        Add(words, seen, word);
                }
                catch (ElementNotAvailableException)
                {
                    // The window changed while it was being read; whatever is collected still holds.
                }
            }

            return words.Count > 0 ? words : null;
        }

        /// <summary>
        /// Walks the visible part of a text control one word at a time. Every word carries its
        /// own rectangle from the application, so the underline sits exactly under the glyphs
        /// even in a proportional font or a right to left paragraph.
        /// </summary>
        private static void ReadDocument(
            TextPattern pattern,
            List<OcrWord> words,
            HashSet<string> seen,
            Stopwatch clock)
        {
            foreach (var visible in pattern.GetVisibleRanges())
            {
                var cursor = visible.Clone();
                cursor.MoveEndpointByRange(
                    TextPatternRangeEndpoint.End,
                    cursor,
                    TextPatternRangeEndpoint.Start);

                while (words.Count < MaxWords && clock.Elapsed < Budget)
                {
                    cursor.ExpandToEnclosingUnit(TextUnit.Word);
                    if (cursor.CompareEndpoints(TextPatternRangeEndpoint.End, visible, TextPatternRangeEndpoint.End) > 0)
                        break;

                    var value = cursor.GetText(MaxWordLength).Trim();
                    if (value.Length > 0)
                    {
                        // A word wrapped across two lines reports one rectangle per fragment.
                        var rectangles = cursor.GetBoundingRectangles();
                        if (rectangles.Length > 0 && rectangles[0].Width > 0 && rectangles[0].Height > 0)
                        {
                            var first = rectangles[0];
                            Add(words, seen, new OcrWord
                            {
                                Text = value,
                                BoundingBox = new BoundingBox(first.X, first.Y, first.Width, first.Height),
                                Confidence = 1.0
                            });
                        }
                    }

                    if (cursor.Move(TextUnit.Word, 1) != 1)
                        break;
                }
            }
        }

        /// <summary>Containers repeat the caption of their children, so identical boxes are dropped.</summary>
        private static void Add(List<OcrWord> words, HashSet<string> seen, OcrWord word)
        {
            var key = $"{word.Text}@{(int)word.BoundingBox.X},{(int)word.BoundingBox.Y}";
            if (seen.Add(key))
                words.Add(word);
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll")]
            public static extern IntPtr GetForegroundWindow();

            [DllImport("user32.dll")]
            public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int processId);
        }
    }
}
