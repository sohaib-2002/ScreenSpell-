using ScreenSpell.Core.Models;

namespace ScreenSpell.Core.Interfaces
{
    public interface ISettingsService
    {
        AppSettings Settings { get; }

        event EventHandler<AppSettings>? SettingsChanged;

        void Save();

        void Update(Action<AppSettings> change);
    }
}
