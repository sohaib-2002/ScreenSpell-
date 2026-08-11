using ScreenSpell.Core.Models;

namespace ScreenSpell.Core.Services
{
    /// <summary>
    /// Turns issues into the thin strips the overlay is allowed to catch the mouse in. Only
    /// the underline itself is clickable: everywhere else the click belongs to the
    /// application underneath and must reach it untouched.
    /// </summary>
    public static class OverlayHitTester
    {
        /// <summary>How far above and below the squiggle a click still counts, in pixels.</summary>
        public const int BandRadius = 4;

        /// <summary>The clickable strip under a word, in the same space as the bounding box.</summary>
        public static PixelRect BandOf(BoundingBox box)
        {
            var top = (int)Math.Round(box.Bottom) - BandRadius;
            var width = Math.Max(1, (int)Math.Round(box.Width));

            return new PixelRect((int)Math.Round(box.X), top, width, BandRadius * 2);
        }

        /// <summary>
        /// The issue whose underline sits under the point, or <c>null</c> when the click
        /// landed on bare screen. Overlapping strips resolve to the narrowest one, which is
        /// the word the user was aiming at.
        /// </summary>
        public static SpellIssue? IssueAt(IReadOnlyList<SpellIssue> issues, int x, int y)
        {
            ArgumentNullException.ThrowIfNull(issues);

            SpellIssue? best = null;
            var bestWidth = int.MaxValue;

            foreach (var issue in issues)
            {
                var band = BandOf(issue.BoundingBox);
                if (x < band.X || x >= band.X + band.Width || y < band.Y || y >= band.Y + band.Height)
                    continue;

                if (band.Width >= bestWidth)
                    continue;

                best = issue;
                bestWidth = band.Width;
            }

            return best;
        }
    }
}
