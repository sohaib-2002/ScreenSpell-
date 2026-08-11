using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ScreenSpell.Overlay
{
    /// <summary>Draws the red squiggles for the misspelled words onto a canvas.</summary>
    public class OverlayRenderer
    {
        private const double WaveHalfPeriod = 3;
        private const double WaveAmplitude = 2;

        private readonly Canvas _canvas;

        public OverlayRenderer(Canvas canvas)
        {
            _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        }

        public Brush Stroke { get; set; } = Brushes.Red;

        public double StrokeThickness { get; set; } = 1.6;

        /// <summary>Replaces the current drawing with one squiggle per issue.</summary>
        public void Draw(IEnumerable<Rect> underlines)
        {
            Clear();
            foreach (var rect in underlines)
                DrawWaveUnderline(rect.Left, rect.Bottom, rect.Width);
        }

        public void DrawUnderline(double x, double y, double width)
        {
            _canvas.Children.Add(new Line
            {
                X1 = x,
                Y1 = y,
                X2 = x + width,
                Y2 = y,
                Stroke = Stroke,
                StrokeThickness = StrokeThickness,
                SnapsToDevicePixels = true
            });
        }

        public void DrawWaveUnderline(double x, double y, double width)
        {
            if (width <= 0)
                return;

            var segments = new PathSegmentCollection();
            var up = true;
            for (var offset = WaveHalfPeriod; offset <= width; offset += WaveHalfPeriod)
            {
                segments.Add(new LineSegment(new Point(x + offset, y + (up ? -WaveAmplitude : 0)), true));
                up = !up;
            }

            var geometry = new PathGeometry();
            geometry.Figures.Add(new PathFigure(new Point(x, y), segments, false));

            _canvas.Children.Add(new Path
            {
                Stroke = Stroke,
                StrokeThickness = StrokeThickness,
                Data = geometry,
                SnapsToDevicePixels = true
            });
        }

        public void Clear() => _canvas.Children.Clear();
    }
}
