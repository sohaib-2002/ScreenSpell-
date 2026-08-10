namespace ScreenSpell.Core.Models
{
    public class AppSettings
    {
        /// <summary>Delay between two screen scans, in milliseconds.</summary>
        public int ScanIntervalMs { get; set; } = 1500;

        /// <summary>OCR words below this confidence are ignored.</summary>
        public double MinOcrConfidence { get; set; } = 0.5;

        /// <summary>BCP-47 language tag handed to the OCR engine.</summary>
        public string Language { get; set; } = "ar";

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
            MinOcrConfidence = MinOcrConfidence,
            Language = Language,
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
