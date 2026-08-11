namespace ScreenSpell.Core.Models
{
    /// <summary>Whole pixel rectangle, used for the parts of a frame that have to be re-read.</summary>
    public readonly record struct PixelRect(int X, int Y, int Width, int Height)
    {
        public int Right => X + Width;

        public int Bottom => Y + Height;

        public int Area => Width * Height;

        public bool IntersectsWith(BoundingBox box) =>
            box.X < Right && box.Right > X && box.Y < Bottom && box.Bottom > Y;
    }
}
