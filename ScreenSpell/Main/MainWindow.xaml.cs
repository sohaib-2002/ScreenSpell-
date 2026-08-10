using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ScreenSpell.Core.Interfaces;
using ScreenSpell.Core.Models;
using ScreenSpell.SpellCheck;

namespace ScreenSpell.Main
{
    public partial class MainWindow : Window
    {
        private readonly ScanService _scanService;
        private readonly ISettingsService _settings;
        private readonly SpellCheckerService _spellChecker;
        private readonly IOcrProvider _ocr;

        public MainWindow(
            ScanService scanService,
            ISettingsService settings,
            SpellCheckerService spellChecker,
            IOcrProvider ocr)
        {
            InitializeComponent();

            _scanService = scanService;
            _settings = settings;
            _spellChecker = spellChecker;
            _ocr = ocr;

            _scanService.IssuesUpdated += OnIssuesUpdated;
            _scanService.StatusChanged += OnStatusChanged;

            LoadSettingsIntoUi();

            EngineText.Text = _ocr.IsAvailable
                ? "محرك التعرف الضوئي: Windows OCR"
                : "محرك التعرف الضوئي غير متاح - ثبّت حزمة اللغة العربية (الإعدادات > الوقت واللغة)";

            if (_settings.Settings.StartScanningOnLaunch)
                StartScanning();
        }

        private void OnIssuesUpdated(object? sender, IReadOnlyList<SpellIssue> issues) =>
            Dispatcher.Invoke(() => IssuesList.ItemsSource = issues);

        private void OnStatusChanged(object? sender, string status) =>
            Dispatcher.Invoke(() => StatusText.Text = status);

        private void LoadSettingsIntoUi()
        {
            var settings = _settings.Settings;
            IntervalBox.Text = settings.ScanIntervalMs.ToString(CultureInfo.InvariantCulture);
            ConfidenceBox.Text = settings.MinOcrConfidence.ToString(CultureInfo.InvariantCulture);
            StabilityBox.Text = settings.StabilityFrames.ToString(CultureInfo.InvariantCulture);
            MinWordLengthBox.Text = settings.MinWordLength.ToString(CultureInfo.InvariantCulture);
            MaxSuggestionsBox.Text = settings.MaxSuggestions.ToString(CultureInfo.InvariantCulture);
            OcrScaleBox.Text = settings.OcrScale.ToString(CultureInfo.InvariantCulture);
            LanguageBox.Text = settings.Language;
            AdditionalLanguagesBox.Text = string.Join(", ", settings.AdditionalLanguages);
            OverlayCheckBox.IsChecked = settings.ShowOverlay;
            ActiveWindowCheckBox.IsChecked = settings.ScanActiveWindowOnly;
            StartOnLaunchCheckBox.IsChecked = settings.StartScanningOnLaunch;
            TrayCheckBox.IsChecked = settings.MinimizeToTray;
        }

        private void StartScanning()
        {
            _scanService.Start();
            StartButton.IsEnabled = false;
            StopButton.IsEnabled = _scanService.IsScanning;
        }

        private void StartButton_Click(object sender, RoutedEventArgs e) => StartScanning();

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            _scanService.Stop();
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
        }

        private async void ScanOnceButton_Click(object sender, RoutedEventArgs e)
        {
            ScanOnceButton.IsEnabled = false;
            try
            {
                await _scanService.ScanOnceAsync();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"خطأ: {ex.Message}";
            }
            finally
            {
                ScanOnceButton.IsEnabled = true;
            }
        }

        /// <summary>Toolbar toggles are saved immediately so they take effect on the next scan.</summary>
        private void QuickToggle_Click(object sender, RoutedEventArgs e)
        {
            _settings.Update(settings =>
            {
                settings.ShowOverlay = OverlayCheckBox.IsChecked == true;
                settings.ScanActiveWindowOnly = ActiveWindowCheckBox.IsChecked == true;
            });
        }

        private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var needsRestart = false;

            _settings.Update(settings =>
            {
                if (int.TryParse(IntervalBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval))
                    settings.ScanIntervalMs = Math.Max(200, interval);

                if (double.TryParse(ConfidenceBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence))
                    settings.MinOcrConfidence = Math.Clamp(confidence, 0, 1);

                if (int.TryParse(StabilityBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var frames))
                    settings.StabilityFrames = Math.Clamp(frames, 1, 10);

                if (int.TryParse(MinWordLengthBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var length))
                    settings.MinWordLength = Math.Clamp(length, 1, 20);

                if (int.TryParse(MaxSuggestionsBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var suggestions))
                    settings.MaxSuggestions = Math.Clamp(suggestions, 1, 20);

                if (double.TryParse(OcrScaleBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale))
                {
                    var clamped = Math.Clamp(scale, 1.0, 4.0);
                    needsRestart |= Math.Abs(clamped - settings.OcrScale) > 0.001;
                    settings.OcrScale = clamped;
                }

                var language = LanguageBox.Text.Trim();
                if (language.Length > 0)
                {
                    needsRestart |= !string.Equals(language, settings.Language, StringComparison.OrdinalIgnoreCase);
                    settings.Language = language;
                }

                var additional = AdditionalLanguagesBox.Text
                    .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();

                needsRestart |= !additional.SequenceEqual(settings.AdditionalLanguages, StringComparer.OrdinalIgnoreCase);
                settings.AdditionalLanguages = additional;

                settings.ShowOverlay = OverlayCheckBox.IsChecked == true;
                settings.ScanActiveWindowOnly = ActiveWindowCheckBox.IsChecked == true;
                settings.StartScanningOnLaunch = StartOnLaunchCheckBox.IsChecked == true;
                settings.MinimizeToTray = TrayCheckBox.IsChecked == true;
            });

            LoadSettingsIntoUi();
            StatusText.Text = needsRestart
                ? "تم حفظ الإعدادات - أعد تشغيل التطبيق لتطبيق التكبير واللغات"
                : "تم حفظ الإعدادات";
        }

        private void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var defaults = new AppSettings();

            _settings.Update(settings =>
            {
                settings.ScanIntervalMs = defaults.ScanIntervalMs;
                settings.MinOcrConfidence = defaults.MinOcrConfidence;
                settings.StabilityFrames = defaults.StabilityFrames;
                settings.MinWordLength = defaults.MinWordLength;
                settings.MaxSuggestions = defaults.MaxSuggestions;
                settings.OcrScale = defaults.OcrScale;
                settings.Language = defaults.Language;
                settings.AdditionalLanguages = new List<string>(defaults.AdditionalLanguages);
                settings.ShowOverlay = defaults.ShowOverlay;
                settings.ScanActiveWindowOnly = defaults.ScanActiveWindowOnly;
                settings.StartScanningOnLaunch = defaults.StartScanningOnLaunch;
                settings.MinimizeToTray = defaults.MinimizeToTray;
            });

            LoadSettingsIntoUi();
            StatusText.Text = "تمت استعادة الإعدادات الافتراضية";
        }

        private void IgnoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: string word })
            {
                _spellChecker.IgnoreWord(word);
                RemoveIssue(word);
            }
        }

        private void AddToDictionaryButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { Tag: string word })
                return;

            _spellChecker.AddToDictionary(word);
            _settings.Update(settings =>
            {
                if (!settings.UserDictionary.Contains(word))
                    settings.UserDictionary.Add(word);
            });

            RemoveIssue(word);
        }

        private void RemoveIssue(string word)
        {
            if (IssuesList.ItemsSource is not IReadOnlyList<SpellIssue> issues)
                return;

            IssuesList.ItemsSource = issues.Where(issue => issue.Word != word).ToList();
        }
    }
}
