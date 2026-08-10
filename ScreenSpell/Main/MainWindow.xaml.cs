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
            OverlayCheckBox.IsChecked = settings.ShowOverlay;
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

        private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            _settings.Update(settings =>
            {
                if (int.TryParse(IntervalBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval))
                    settings.ScanIntervalMs = Math.Max(200, interval);

                if (double.TryParse(ConfidenceBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var confidence))
                    settings.MinOcrConfidence = Math.Clamp(confidence, 0, 1);

                settings.ShowOverlay = OverlayCheckBox.IsChecked == true;
            });

            LoadSettingsIntoUi();
            StatusText.Text = "تم حفظ الإعدادات";
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
