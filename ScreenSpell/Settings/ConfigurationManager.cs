using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ScreenSpell.Core.Interfaces;
using ScreenSpell.Core.Models;

namespace ScreenSpell.Settings
{
    /// <summary>
    /// Loads and saves <see cref="AppSettings"/> as JSON under the per-user application data
    /// folder (%LOCALAPPDATA%\ScreenSpell\settings.json on Windows) so the installation
    /// directory stays read only.
    /// </summary>
    public class ConfigurationManager : ISettingsService
    {
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly ILogger<ConfigurationManager> _logger;
        private readonly object _gate = new();

        public ConfigurationManager(ILogger<ConfigurationManager>? logger = null, string? settingsPath = null)
        {
            _logger = logger ?? NullLogger<ConfigurationManager>.Instance;
            SettingsPath = settingsPath ?? DefaultSettingsPath();
            Settings = Load();
        }

        public event EventHandler<AppSettings>? SettingsChanged;

        public string SettingsPath { get; }

        public AppSettings Settings { get; private set; }

        public static string DefaultSettingsPath() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScreenSpell",
            "settings.json");

        public void Save()
        {
            try
            {
                lock (_gate)
                {
                    var directory = Path.GetDirectoryName(SettingsPath);
                    if (!string.IsNullOrEmpty(directory))
                        Directory.CreateDirectory(directory);

                    File.WriteAllText(SettingsPath, JsonSerializer.Serialize(Settings, SerializerOptions));
                }

                _logger.LogInformation("Saved settings to {Path}.", SettingsPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not save settings to {Path}.", SettingsPath);
            }
        }

        public void Update(Action<AppSettings> change)
        {
            ArgumentNullException.ThrowIfNull(change);

            var updated = Settings.Clone();
            change(updated);
            Settings = updated;

            Save();
            SettingsChanged?.Invoke(this, updated);
        }

        /// <summary>Rewrites the settings file with the built-in defaults.</summary>
        public void ResetToDefaults()
        {
            Settings = new AppSettings();
            Save();
            SettingsChanged?.Invoke(this, Settings);
        }

        private AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var loaded = JsonSerializer.Deserialize<AppSettings>(json, SerializerOptions);
                    if (loaded is not null)
                    {
                        _logger.LogInformation("Loaded settings from {Path}.", SettingsPath);
                        return loaded;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Settings at {Path} could not be read; falling back to defaults.", SettingsPath);
            }

            return new AppSettings();
        }
    }
}
