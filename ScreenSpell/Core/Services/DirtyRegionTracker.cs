using ScreenSpell.Core.Models;

namespace ScreenSpell.Core.Services
{
    /// <summary>
    /// Tells which part of the frame was repainted since the previous one. Typing a word or
    /// opening a menu only touches a handful of tiles, and recognising those tiles instead of
    /// the whole window is what keeps the underlines appearing while you type rather than a
    /// scan later.
    /// </summary>
    public sealed class DirtyRegionTracker
    {
        /// <summary>Tile side in pixels; a line of text is a couple of tiles tall.</summary>
        private const int TileSize = 32;

        /// <summary>Only every fourth pixel of a tile is hashed; a repaint always moves several.</summary>
        private const int SampleStep = 4;

        /// <summary>Grown by this much so glyph edges just outside the repaint are read too.</summary>
        private const int Margin = 24;

        /// <summary>Above this share of the frame a partial pass is not worth its bookkeeping.</summary>
        private const double FullFrameShare = 0.6;

        private uint[] _tiles = Array.Empty<uint>();
        private int _width;
        private int _height;
        private int _originX;
        private int _originY;

        /// <summary>
        /// Region of <paramref name="frame"/> that changed, in frame coordinates: null when the
        /// picture is identical to the previous one, and the whole frame the first time, after a
        /// move or a resize, or when too much of it changed to bother with a partial pass.
        /// </summary>
        public PixelRect? Track(ScreenFrame frame)
        {
            ArgumentNullException.ThrowIfNull(frame);

            var columns = (frame.Width + TileSize - 1) / TileSize;
            var rows = (frame.Height + TileSize - 1) / TileSize;
            var tiles = new uint[columns * rows];

            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                    tiles[row * columns + column] = HashTile(frame, column * TileSize, row * TileSize);
            }

            var whole = new PixelRect(0, 0, frame.Width, frame.Height);
            var moved = frame.Width != _width
                        || frame.Height != _height
                        || frame.OriginX != _originX
                        || frame.OriginY != _originY;

            var previous = _tiles;
            _tiles = tiles;
            _width = frame.Width;
            _height = frame.Height;
            _originX = frame.OriginX;
            _originY = frame.OriginY;

            if (moved || previous.Length != tiles.Length)
                return whole;

            var left = columns;
            var top = rows;
            var right = -1;
            var bottom = -1;

            for (var row = 0; row < rows; row++)
            {
                for (var column = 0; column < columns; column++)
                {
                    if (tiles[row * columns + column] == previous[row * columns + column])
                        continue;

                    left = Math.Min(left, column);
                    right = Math.Max(right, column);
                    top = Math.Min(top, row);
                    bottom = Math.Max(bottom, row);
                }
            }

            if (right < 0)
                return null;

            var x = Math.Max(0, left * TileSize - Margin);
            var y = Math.Max(0, top * TileSize - Margin);
            var width = Math.Min(frame.Width, (right + 1) * TileSize + Margin) - x;
            var height = Math.Min(frame.Height, (bottom + 1) * TileSize + Margin) - y;
            var region = new PixelRect(x, y, width, height);

            return region.Area >= whole.Area * FullFrameShare ? whole : region;
        }

        /// <summary>
        /// Combines the words of a partial pass with the ones already known: everything that
        /// was not repainted keeps its previous reading, and <paramref name="region"/> (in
        /// desktop pixels) is replaced by what was just read.
        /// </summary>
        public static List<OcrWord> Merge(
            IReadOnlyList<OcrWord> previous,
            IEnumerable<OcrWord> fresh,
            PixelRect region)
        {
            ArgumentNullException.ThrowIfNull(previous);
            ArgumentNullException.ThrowIfNull(fresh);

            var merged = previous.Where(word => !region.IntersectsWith(word.BoundingBox)).ToList();
            merged.AddRange(fresh);
            return merged;
        }

        /// <summary>Forgets the previous frame, so the next one counts as fully repainted.</summary>
        public void Reset()
        {
            _tiles = Array.Empty<uint>();
            _width = 0;
            _height = 0;
        }

        private static uint HashTile(ScreenFrame frame, int originX, int originY)
        {
            const uint Prime = 16777619;
            var hash = 2166136261;

            var lastY = Math.Min(originY + TileSize, frame.Height);
            var lastX = Math.Min(originX + TileSize, frame.Width);

            for (var y = originY; y < lastY; y += SampleStep)
            {
                var row = y * frame.Stride;
                for (var x = originX; x < lastX; x += SampleStep)
                {
                    var pixel = row + x * 4;
                    hash = (hash ^ frame.Pixels[pixel]) * Prime;
                    hash = (hash ^ frame.Pixels[pixel + 1]) * Prime;
                    hash = (hash ^ frame.Pixels[pixel + 2]) * Prime;
                }
            }

            return hash;
        }
    }
}
