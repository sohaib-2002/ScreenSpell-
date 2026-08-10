namespace ScreenSpell.Core.Models
{
    public class OcrWord
    {
        public required string Text { get; set; }

        public BoundingBox BoundingBox { get; set; }

        public double Confidence { get; set; }
    }
}
