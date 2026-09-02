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
    private readonly TextBlock[] _textureChecks;
    private bool _updating = true;
    private bool _settingsPageVisible;

    internal bool AllowApplicationExit { get; set; }
    internal bool IsSettingsPageVisible => _settingsPageVisible;

    public MainWindow(App app)
    {
        _app = app;
        InitializeComponent();
        _textureCards = new[] { TextureCard0, TextureCard1, TextureCard2, TextureCard3 };
        _texturePreviews = new[] { TexturePreview0, TexturePreview1, TexturePreview2, TexturePreview3 };
        _textureChecks = new[] { TextureCheck0, TextureCheck1, TextureCheck2, TextureCheck3 };
        SetTextureCardPreviews();
        RefreshFromSettings(_app.CurrentSettings, _app.PauseState);
    }

    internal void RefreshFromSettings(Settings settings, PauseState pause, bool renderPreview = true)
    {
        _updating = true;
        try
        {
            EnabledToggle.IsChecked = settings.Enabled;
            IntensitySlider.Value = settings.Intensity;
            WarmthSlider.Value = settings.Warmth;
            DimSlider.Value = settings.Dim;
            AllScreensRadio.IsChecked = settings.AllScreens;
            PrimaryScreenRadio.IsChecked = !settings.AllScreens;
            ReminderToggle.IsChecked = settings.Reminders;
            BreakComboBox.SelectedItem = BreakComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), settings.BreakMinutes.ToString(), StringComparison.Ordinal));
            IntensityValue.Text = settings.Intensity + "%";
            WarmthValue.Text = settings.Warmth + "%";
            DimValue.Text = settings.Dim + "%";
            BreakComboBox.IsEnabled = settings.Reminders;
            UpdatePauseStatus(DateTimeOffset.Now, pause, settings.Enabled);
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
        UpdatePauseStatus(now, pause, _app.CurrentSettings.Enabled);
    }

    private void UpdatePauseStatus(DateTimeOffset now, PauseState pause, bool enabled)
    {
        if (!enabled)
        {
            HeaderStatus.Text = "未开启";
            PauseActionButton.Content = "暂停 10 分钟";
            PauseActionButton.IsEnabled = false;
            return;
        }

        var paused = pause.IsPaused(now) && pause.Until is { };
        if (paused && pause.Until is { } until)
        {
            var remaining = until - now;
            var totalSeconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
            HeaderStatus.Text = $"暂停中 · {totalSeconds / 60:00}:{totalSeconds % 60:00}";
            PauseActionButton.Content = "恢复护眼";
            PauseActionButton.IsEnabled = true;
        }
        else
        {
            HeaderStatus.Text = "已开启";
            PauseActionButton.Content = "暂停 10 分钟";
            PauseActionButton.IsEnabled = true;
        }
    }

    internal void SetSettingsWarning(string? warning)
    {
        SettingsWarning.Text = warning ?? string.Empty;
        SettingsPageWarning.Text = warning ?? string.Empty;
        var visible = !string.IsNullOrWhiteSpace(warning);
        SettingsWarning.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        SettingsPageWarning.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        UpdateWarningPanels();
    }

    internal void SetHotkeyWarning(string? warning)
    {
        HotkeyWarning.Text = warning ?? string.Empty;
        SettingsPageHotkeyWarning.Text = warning ?? string.Empty;
        var visible = !string.IsNullOrWhiteSpace(warning);
        HotkeyWarning.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        SettingsPageHotkeyWarning.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        UpdateWarningPanels();
    }

    private void UpdateWarningPanels()
    {
        var mainVisible = HotkeyWarning.Visibility == Visibility.Visible || SettingsWarning.Visibility == Visibility.Visible;
        var settingsVisible = SettingsPageHotkeyWarning.Visibility == Visibility.Visible || SettingsPageWarning.Visibility == Visibility.Visible;
        MainWarningPanel.Visibility = mainVisible ? Visibility.Visible : Visibility.Collapsed;
        SettingsWarningPanel.Visibility = settingsVisible ? Visibility.Visible : Visibility.Collapsed;
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
            _textureCards[i].BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(33, 79, 64)) : new SolidColorBrush(Color.FromRgb(230, 232, 227));
            _textureCards[i].BorderThickness = selected ? new Thickness(2) : new Thickness(1);
            _textureCards[i].Background = selected ? new SolidColorBrush(Color.FromRgb(237, 243, 236)) : new SolidColorBrush(Color.FromRgb(255, 255, 255));
            _textureChecks[i].Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void EnabledToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_updating) return;
        _app.SetEnabled(EnabledToggle.IsChecked == true);
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

    private void ReminderToggle_OnChanged(object sender, RoutedEventArgs e)
    {
        BreakComboBox.IsEnabled = ReminderToggle.IsChecked == true;
        if (!_updating) _app.SetReminders(ReminderToggle.IsChecked == true);
    }

    private void BreakComboBox_OnChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updating && BreakComboBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var minutes))
            _app.SetBreakMinutes(minutes);
    }

    private void PauseActionButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_app.PauseState.IsPaused(DateTimeOffset.Now))
            _app.ResumePause();
        else
            _app.PauseForTenMinutes();
    }

    private void MoreSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        ShowSettingsPageForRender();
        Dispatcher.BeginInvoke(new Action(() => BackButton.Focus()), System.Windows.Threading.DispatcherPriority.Input);
    }

    internal void ShowSettingsPageForRender()
    {
        _settingsPageVisible = true;
        MainPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Visible;
    }

    internal bool HasCompleteRenderLayout(bool settingsPage, int width, int height)
    {
        var root = (FrameworkElement)Content;
        if (root.ActualWidth < width - 1 || root.ActualHeight < height - 1)
            return false;

        var elements = settingsPage
            ? new FrameworkElement[] { BackButton, WarmthSlider, DimSlider, AllScreensRadio, PrimaryScreenRadio, ReminderToggle, BreakComboBox, SettingsExitButton }
            : new FrameworkElement[] { HeaderStatus, EnabledToggle, ReadingPreviewFrame, ReadingTextureOverlay, TextureCard0, TextureCard1, TextureCard2, TextureCard3, IntensitySlider, PauseActionButton, MoreSettingsButton };

        if (elements.Any(element => element.Visibility != Visibility.Visible || element.ActualWidth < 1 || element.ActualHeight < 1))
            return false;

        var fixedChrome = settingsPage
            ? new FrameworkElement[] { BackButton, SettingsExitButton }
            : new FrameworkElement[] { HeaderStatus, EnabledToggle, PauseActionButton, MoreSettingsButton };
        return fixedChrome.All(element => IsInside(root, element, width, height));
    }

    internal bool HasExpectedRenderState(Settings settings, PauseState pause, bool settingsPage, bool errorsVisible)
    {
        if (settingsPage != _settingsPageVisible ||
            (settingsPage && (SettingsPage.Visibility != Visibility.Visible || MainPage.Visibility != Visibility.Collapsed)) ||
            (!settingsPage && (MainPage.Visibility != Visibility.Visible || SettingsPage.Visibility != Visibility.Collapsed)))
            return false;

        if (settingsPage)
            return ReminderToggle.IsChecked == settings.Reminders && BreakComboBox.IsEnabled == settings.Reminders;

        var paused = settings.Enabled && pause.IsPaused(DateTimeOffset.Now);
        var expectedStatus = !settings.Enabled ? "未开启" : paused ? "暂停中" : "已开启";
        var expectedAction = paused ? "恢复护眼" : "暂停 10 分钟";
        var statusMatches = HeaderStatus.Text.StartsWith(expectedStatus, StringComparison.Ordinal);
        var actionMatches = string.Equals(PauseActionButton.Content?.ToString(), expectedAction, StringComparison.Ordinal);
        var actionEnabled = PauseActionButton.IsEnabled == settings.Enabled;
        var warningMatches = !errorsVisible || MainWarningPanel.Visibility == Visibility.Visible;
        return statusMatches && actionMatches && actionEnabled && warningMatches;
    }

    private static bool IsInside(FrameworkElement root, FrameworkElement element, int width, int height)
    {
        try
        {
            var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            return bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= width + 1 && bounds.Bottom <= height + 1;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private void BackButton_OnClick(object sender, RoutedEventArgs e)
    {
        _settingsPageVisible = false;
        SettingsPage.Visibility = Visibility.Collapsed;
        MainPage.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(new Action(() => MoreSettingsButton.Focus()), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void ExitButton_OnClick(object sender, RoutedEventArgs e) => _app.ExitApplication();

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        if (AllowApplicationExit || _app.IsExiting) return;
        e.Cancel = true;
        _app.HidePanelToTray();
    }
}
