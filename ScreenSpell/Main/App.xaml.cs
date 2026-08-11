using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ScreenSpell.Automation;
using ScreenSpell.Cache;
using ScreenSpell.Capture;
using ScreenSpell.Core.Interfaces;
using ScreenSpell.Core.Models;
using ScreenSpell.Engines;
using ScreenSpell.OCR;
using ScreenSpell.Overlay;
using ScreenSpell.Settings;
using ScreenSpell.SpellCheck;
using ScreenSpell.Tray;
using Serilog;

namespace ScreenSpell.Main
{
    public partial class App : Application
    {
        private readonly IHost _host;
        private SystemTrayManager? _tray;
        private bool _exiting;

        public App()
        {
            _host = Host.CreateDefaultBuilder()
                .UseSerilog((context, services, configuration) => configuration
                    .WriteTo.File(
                        Path.Combine(LogDirectory(), "screenspell-.txt"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 7)
                    .WriteTo.Debug())
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton<ConfigurationManager>();
                    services.AddSingleton<ISettingsService>(sp => sp.GetRequiredService<ConfigurationManager>());
                    services.AddSingleton(sp => sp.GetRequiredService<ISettingsService>().Settings);

                    services.AddSingleton(BuildDictionary);
                    services.AddSingleton<SuggestionService>(sp =>
                    {
                        var suggestions = new SuggestionService();
                        var settings = sp.GetRequiredService<ISettingsService>().Settings;
                        foreach (var word in settings.UserDictionary)
                            suggestions.AddToDictionary(word);
                        foreach (var word in settings.IgnoredWords)
                            suggestions.IgnoreWord(word);
                        return suggestions;
                    });

                    services.AddSingleton<DictionarySpellChecker>();
                    services.AddSingleton<OnnxSpellChecker>(sp => new OnnxSpellChecker(
                        sp.GetRequiredService<DictionarySpellChecker>(),
                        sp.GetRequiredService<AppSettings>(),
                        sp.GetRequiredService<ILogger<OnnxSpellChecker>>()));
                    services.AddSingleton<SpellCheckerService>(sp => new SpellCheckerService(
                        sp.GetRequiredService<OnnxSpellChecker>(),
                        sp.GetRequiredService<SuggestionService>(),
                        sp.GetRequiredService<SpellCache>()));
                    services.AddSingleton<ISpellChecker>(sp => sp.GetRequiredService<SpellCheckerService>());

                    services.AddSingleton<IOcrProvider>(BuildOcrProvider);
                    services.AddSingleton<ITextSource, UiAutomationTextSource>();
                    services.AddSingleton<IScreenCaptureService, GraphicsCaptureService>();
                    services.AddSingleton<IOverlayService>(_ => new OverlayService(Current.Dispatcher));
                    services.AddSingleton<SpellCache>();
                    services.AddSingleton<OcrCache>();

                    services.AddSingleton<ScanService>();
                    services.AddSingleton<MainWindow>();
                })
                .Build();
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            await _host.StartAsync();

            var settings = _host.Services.GetRequiredService<ISettingsService>();
            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;

            // Closing the window only hides it while the tray icon is enabled.
            ShutdownMode = settings.Settings.MinimizeToTray ? ShutdownMode.OnExplicitShutdown : ShutdownMode.OnMainWindowClose;
            if (settings.Settings.MinimizeToTray)
            {
                _tray = new SystemTrayManager();
                _tray.ShowRequested += (_, _) => ShowMainWindow(mainWindow);
                _tray.SettingsRequested += (_, _) => ShowMainWindow(mainWindow);
                _tray.ScanToggleRequested += (_, _) => ToggleScan();
                _tray.ExitRequested += (_, _) => Shutdown();

                mainWindow.Closing += (_, args) =>
                {
                    if (_exiting)
                        return;

                    args.Cancel = true;
                    mainWindow.Hide();
                };
            }

            mainWindow.Show();
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            _exiting = true;
            _tray?.Dispose();

            if (_host.Services.GetService<IOverlayService>() is IDisposable overlay)
                overlay.Dispose();

            using (_host)
            {
                await _host.StopAsync(TimeSpan.FromSeconds(5));
            }

            await Log.CloseAndFlushAsync();
            base.OnExit(e);
        }

        private static string LogDirectory() => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScreenSpell",
            "Logs");

        /// <summary>
        /// Builds the reader the settings ask for. The offline engines carry their own models,
        /// so when one of them cannot start (missing files, unsupported CPU) the Windows engine
        /// takes over instead of leaving the app with no reader at all.
        /// </summary>
        private static IOcrProvider BuildOcrProvider(IServiceProvider services)
        {
            var settings = services.GetRequiredService<ISettingsService>().Settings;
            var loggers = services.GetRequiredService<ILoggerFactory>();

            IOcrProvider? provider = settings.OcrEngine switch
            {
                OcrEngineKind.Tesseract => new TesseractOcrProvider(settings, loggers.CreateLogger<TesseractOcrProvider>()),
                OcrEngineKind.Paddle => new PaddleOcrProvider(settings, loggers.CreateLogger<PaddleOcrProvider>()),
                _ => null
            };

            if (provider is { IsAvailable: true })
                return provider;

            if (provider is not null)
            {
                (provider as IDisposable)?.Dispose();
                loggers.CreateLogger<App>().LogWarning(
                    "The {Engine} engine could not start; falling back to Windows OCR.", settings.OcrEngine);
            }

            return new WindowsOcrProvider(settings, loggers.CreateLogger<WindowsOcrProvider>());
        }

        /// <summary>
        /// Loads the built-in seed list plus anything the user dropped into the dictionary
        /// folder (a full ayaspell "ar.dic" for instance).
        /// </summary>
        private static ArabicDictionary BuildDictionary(IServiceProvider services)
        {
            var settings = services.GetRequiredService<ISettingsService>().Settings;
            var dictionary = new ArabicDictionary();

            var configured = settings.DictionaryDirectory;
            var directory = Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(AppContext.BaseDirectory, configured);

            dictionary.LoadDirectory(directory);

            var userDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ScreenSpell",
                "Dictionaries");
            dictionary.LoadDirectory(userDirectory);

            services.GetRequiredService<ILogger<App>>()
                .LogInformation("Dictionary loaded with {Count} words from {Directory}.", dictionary.Count, directory);

            return dictionary;
        }

        private void ShowMainWindow(Window window)
        {
            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        }

        private void ToggleScan()
        {
            var scanner = _host.Services.GetRequiredService<ScanService>();
            if (scanner.IsScanning)
                scanner.Stop();
            else
                scanner.Start();

            _tray?.SetScanning(scanner.IsScanning);
        }
    }
}
