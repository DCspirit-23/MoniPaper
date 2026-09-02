using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PaperCare;

public partial class MainWindow : Window
{
    private readonly App _app;
    private readonly Button[] _textureCards;
    private readonly Border[] _texturePreviews;
    private bool _updating = true;

    internal bool AllowApplicationExit { get; set; }

    public MainWindow(App app)
    {
        _app = app;
        InitializeComponent();
        _textureCards = new[] { TextureCard0, TextureCard1, TextureCard2, TextureCard3 };
        _texturePreviews = new[] { TexturePreview0, TexturePreview1, TexturePreview2, TexturePreview3 };
        SetTextureCardPreviews();
        RefreshFromSettings(_app.CurrentSettings, _app.PauseState);
    }

    internal void RefreshFromSettings(Settings settings, PauseState pause, bool renderPreview = true)
    {
        _updating = true;
        try
        {
            EnabledCheckBox.IsChecked = settings.Enabled;
            IntensitySlider.Value = settings.Intensity;
            WarmthSlider.Value = settings.Warmth;
            DimSlider.Value = settings.Dim;
            AllScreensRadio.IsChecked = settings.AllScreens;
            PrimaryScreenRadio.IsChecked = !settings.AllScreens;
            ReminderCheckBox.IsChecked = settings.Reminders;
            BreakComboBox.SelectedItem = BreakComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), settings.BreakMinutes.ToString(), StringComparison.Ordinal));
            IntensityValue.Text = settings.Intensity + "%";
            WarmthValue.Text = settings.Warmth + "%";
            DimValue.Text = settings.Dim + "%";
            UpdatePauseStatus(DateTimeOffset.Now, pause);
        }
        finally
        {
            _updating = false;
        }

        UpdateTextureSelection(settings.Texture);
        if (renderPreview)
            ReadingTextureOverlay.Background = TextureRenderer.Brush(settings);
    }

    internal void UpdatePauseStatus(DateTimeOffset now, PauseState pause)
    {
        if (pause.IsPaused(now) && pause.Until is { } until)
        {
            var remaining = until - now;
            var totalSeconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
            StatusText.Text = $"已暂停 · {totalSeconds / 60:00}:{totalSeconds % 60:00} 后恢复";
            PauseButton.IsEnabled = false;
            ResumeButton.IsEnabled = true;
        }
        else
        {
            StatusText.Text = _app.CurrentSettings.Enabled ? "覆盖已开启" : "覆盖已关闭";
            PauseButton.IsEnabled = _app.CurrentSettings.Enabled;
            ResumeButton.IsEnabled = false;
        }
    }

    internal void SetSettingsWarning(string? warning)
    {
        SettingsWarning.Text = warning ?? string.Empty;
        SettingsWarning.Visibility = string.IsNullOrWhiteSpace(warning) ? Visibility.Collapsed : Visibility.Visible;
    }

    internal void SetHotkeyWarning(string? warning)
    {
        HotkeyWarning.Text = warning ?? string.Empty;
        HotkeyWarning.Visibility = string.IsNullOrWhiteSpace(warning) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetTextureCardPreviews()
    {
        for (var i = 0; i < _texturePreviews.Length; i++)
        {
            var preview = new Settings
            {
                Texture = i,
                Intensity = 42,
                Warmth = 14,
                Dim = i == 3 ? 10 : 0
            };
            _texturePreviews[i].Background = TextureRenderer.Brush(preview);
        }
    }

    private void UpdateTextureSelection(int texture)
    {
        for (var i = 0; i < _textureCards.Length; i++)
        {
            var selected = i == texture;
            _textureCards[i].BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(31, 83, 66)) : new SolidColorBrush(Color.FromRgb(229, 220, 203));
            _textureCards[i].BorderThickness = selected ? new Thickness(2) : new Thickness(1);
            _textureCards[i].Background = selected ? new SolidColorBrush(Color.FromRgb(231, 242, 232)) : new SolidColorBrush(Color.FromRgb(250, 247, 239));
        }
    }

    private void EnabledCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_updating) _app.SetEnabled(EnabledCheckBox.IsChecked == true);
    }

    private void TextureCard_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && int.TryParse(button.Tag?.ToString(), out var texture))
            _app.SetTexture(texture);
    }

    private void IntensitySlider_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        IntensityValue.Text = $"{(int)Math.Round(IntensitySlider.Value)}%";
        if (!_updating) _app.SetIntensity((int)Math.Round(IntensitySlider.Value));
    }

    private void WarmthSlider_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        WarmthValue.Text = $"{(int)Math.Round(WarmthSlider.Value)}%";
        if (!_updating) _app.SetWarmth((int)Math.Round(WarmthSlider.Value));
    }

    private void DimSlider_OnChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        DimValue.Text = $"{(int)Math.Round(DimSlider.Value)}%";
        if (!_updating) _app.SetDim((int)Math.Round(DimSlider.Value));
    }

    private void AllScreensRadio_OnChecked(object sender, RoutedEventArgs e)
    {
        if (!_updating && AllScreensRadio.IsChecked == true) _app.SetAllScreens(true);
    }

    private void PrimaryScreenRadio_OnChecked(object sender, RoutedEventArgs e)
    {
        if (!_updating && PrimaryScreenRadio.IsChecked == true) _app.SetAllScreens(false);
    }

    private void ReminderCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (!_updating) _app.SetReminders(ReminderCheckBox.IsChecked == true);
    }

    private void BreakComboBox_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating && BreakComboBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var minutes))
            _app.SetBreakMinutes(minutes);
    }

    private void PauseButton_OnClick(object sender, RoutedEventArgs e) => _app.PauseForTenMinutes();

    private void ResumeButton_OnClick(object sender, RoutedEventArgs e) => _app.ResumePause();

    private void ExitButton_OnClick(object sender, RoutedEventArgs e) => _app.ExitApplication();

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        if (AllowApplicationExit || _app.IsExiting) return;
        e.Cancel = true;
        _app.HidePanelToTray();
    }
}
