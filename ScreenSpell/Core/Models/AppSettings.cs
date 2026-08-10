namespace ScreenSpell.Core.Models
{
    public class AppSettings
    {
        /// <summary>Delay between two screen scans, in milliseconds.</summary>
        public int ScanIntervalMs { get; set; } = 800;

        /// <summary>
        /// Scan only the window you are working in instead of the whole screen. This is what
        /// keeps the loop responsive while switching apps; turn it off to underline everything
        /// visible, at a much higher cost per scan.
        /// </summary>
        public bool ScanActiveWindowOnly { get; set; } = true;

        /// <summary>OCR words below this confidence are ignored.</summary>
        public double MinOcrConfidence { get; set; } = 0.5;

        /// <summary>
        /// The frame is enlarged by this factor before recognition. Screen text is small and
        /// the Windows engine misreads letters at native resolution (التوقيع read as التوميع);
        /// 1 disables the upscale.
        /// </summary>
        public double OcrScale { get; set; } = 2.0;

        /// <summary>BCP-47 language tag handed to the OCR engine.</summary>
        public string Language { get; set; } = "ar";

        /// <summary>
        /// Extra BCP-47 tags recognised alongside <see cref="Language"/>. One OCR engine is
        /// created per installed tag and their results are merged, which is what makes Latin
        /// text on an Arabic screen readable.
        /// </summary>
        public List<string> AdditionalLanguages { get; set; } = new() { "en" };

        /// <summary>
        /// Number of consecutive scans a word must stay misspelled before it is underlined,
        /// and how many scans an underline survives after the word disappears. Raising it
        /// steadies the overlay when OCR results flicker; 1 disables the smoothing.
        /// </summary>
        public int StabilityFrames { get; set; } = 2;

        /// <summary>Maximum number of suggestions shown for a misspelled word.</summary>
        public int MaxSuggestions { get; set; } = 5;

        /// <summary>Words shorter than this are never flagged (mostly OCR noise).</summary>
        public int MinWordLength { get; set; } = 3;

        /// <summary>Draw the red squiggles on top of the desktop.</summary>
        public bool ShowOverlay { get; set; } = true;

        /// <summary>Start scanning as soon as the application launches.</summary>
        public bool StartScanningOnLaunch { get; set; }

        /// <summary>Send the main window to the tray instead of closing it.</summary>
        public bool MinimizeToTray { get; set; } = true;

        /// <summary>Folder holding additional word lists (*.txt / *.dic).</summary>
        public string DictionaryDirectory { get; set; } = "Dictionaries";

        /// <summary>Optional ONNX model; the dictionary checker is used when it is absent.</summary>
        public string OnnxModelPath { get; set; } = Path.Combine("Models", "ArabicSpellModel.onnx");

        /// <summary>Words the user explicitly added to their personal dictionary.</summary>
        public List<string> UserDictionary { get; set; } = new();

        /// <summary>Words the user chose to ignore for this installation.</summary>
        public List<string> IgnoredWords { get; set; } = new();

        public AppSettings Clone() => new()
        {
            ScanIntervalMs = ScanIntervalMs,
            ScanActiveWindowOnly = ScanActiveWindowOnly,
            MinOcrConfidence = MinOcrConfidence,
            OcrScale = OcrScale,
            Language = Language,
            AdditionalLanguages = new List<string>(AdditionalLanguages),
            StabilityFrames = StabilityFrames,
            MaxSuggestions = MaxSuggestions,
            MinWordLength = MinWordLength,
            ShowOverlay = ShowOverlay,
            StartScanningOnLaunch = StartScanningOnLaunch,
            MinimizeToTray = MinimizeToTray,
            DictionaryDirectory = DictionaryDirectory,
            OnnxModelPath = OnnxModelPath,
            UserDictionary = new List<string>(UserDictionary),
            IgnoredWords = new List<string>(IgnoredWords),
        };
    }
}
