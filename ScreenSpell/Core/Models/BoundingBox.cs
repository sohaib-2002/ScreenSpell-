namespace ScreenSpell.Core.Models
{
    /// <summary>
    /// Axis aligned rectangle expressed in physical screen pixels.
    /// </summary>
    public readonly record struct BoundingBox(double X, double Y, double Width, double Height)
    {
        public double Right => X + Width;

        public double Bottom => Y + Height;

        public bool Contains(double x, double y) => x >= X && x <= Right && y >= Y && y <= Bottom;
    }
}
