using ScreenSpell.Core.Models;

namespace ScreenSpell.Core.Interfaces
{
    public interface IOcrProvider
    {
        /// <summary>True when the underlying engine is usable on this machine.</summary>
        bool IsAvailable { get; }

        Task<List<OcrWord>> ExtractTextAsync(ScreenFrame frame, CancellationToken cancellationToken = default);
    }
}
