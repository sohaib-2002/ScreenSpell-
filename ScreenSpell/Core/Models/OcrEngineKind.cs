namespace ScreenSpell.Core.Models
{
    /// <summary>The engines that can read the screen, from the fastest to the most accurate.</summary>
    public enum OcrEngineKind
    {
        /// <summary>Windows.Media.Ocr: instant, but needs the language packs and misreads small text.</summary>
        Windows,

        /// <summary>Tesseract 5 with the bundled trained data; slower, no Windows language pack needed.</summary>
        Tesseract,

        /// <summary>PP-OCR on ONNX Runtime: the most accurate on Arabic, and the heaviest.</summary>
        Paddle
    }
}
