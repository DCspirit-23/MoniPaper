using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PaperCare;

public partial class MainWindow : Window
{
    private enum ShortcutSlot
    {
        ShowPanel,
        ToggleOverlay,
        IncreaseIntensity,
        DecreaseIntensity
    }

    private readonly App _app;
    private readonly Button[] _textureCards;
    private readonly Border[] _texturePreviews;
    private readonly TextBlock[] _textureChecks;
    private bool _updating = true;
    private bool _settingsPageVisible;
    private bool _shortcutEditorVisible;
    private bool _shortcutCaptureActive;
    private ShortcutSlot? _shortcutCaptureSlot;
    private ShortcutCaptureSession? _shortcutCaptureSession;
    private HotkeyConfiguration? _shortcutDraft;
    private string? _shortcutEditorError;
    private string? _shortcutEditorSuccess;

    internal bool AllowApplicationExit { get; set; }
    internal bool IsSettingsPageVisible => _settingsPageVisible;
    internal bool IsShortcutEditorActive => _shortcutEditorVisible && ShortcutEditorPage.Visibility == Visibility.Visible && IsActive;

    public MainWindow(App app)
    {
        _app = app;
        InitializeComponent();
        _textureCards = new[] { TextureCard0, TextureCard1, TextureCard2, TextureCard3 };
        _texturePreviews = new[] { TexturePreview0, TexturePreview1, TexturePreview2, TexturePreview3 };
        _textureChecks = new[] { TextureCheck0, TextureCheck1, TextureCheck2, TextureCheck3 };
        SetTextureCardPreviews();
        Deactivated += Window_OnDeactivated;
        IsVisibleChanged += Window_OnIsVisibleChanged;
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
            CloseToTrayRadio.IsChecked = settings.CloseToTray;
            CloseToExitRadio.IsChecked = !settings.CloseToTray;
            CloseBehaviorHint.Text = settings.CloseToTray ? "关闭窗口后继续在系统托盘运行。" : "关闭窗口后退出 MoniPaper。";
            if (!_shortcutEditorVisible)
                _shortcutDraft = settings.Hotkeys.Clone();
            UpdateShortcutSummary(settings.Hotkeys);
            UpdatePauseStatus(DateTimeOffset.Now, pause, settings.Enabled);
        }
        finally
        {
            _updating = false;
        }

        UpdateTextureSelection(settings.Texture);
        if (renderPreview)
            ReadingTextureOverlay.Background = TextureRenderer.Brush(settings);
        if (_shortcutEditorVisible)
            UpdateShortcutEditorView();
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

    private HotkeyConfiguration GetShortcutDraft() => _shortcutDraft ??= _app.CurrentSettings.Hotkeys.Clone();

    private void UpdateShortcutSummary(HotkeyConfiguration hotkeys)
    {
        ShowPanelShortcutText.Text = hotkeys.ShowPanel.DisplayText;
        ToggleOverlayShortcutText.Text = hotkeys.ToggleOverlay.DisplayText;
        IntensityShortcutText.Text = $"增加 {hotkeys.IncreaseIntensity.DisplayText}　减少 {hotkeys.DecreaseIntensity.DisplayText}";
    }

    private void UpdateShortcutEditorView()
    {
        var draft = GetShortcutDraft();
        SetShortcutButtonContent(RecordShowPanelButton, _shortcutCaptureSlot == ShortcutSlot.ShowPanel ? "按下组合键…" : draft.ShowPanel.DisplayText);
        SetShortcutButtonContent(RecordToggleOverlayButton, _shortcutCaptureSlot == ShortcutSlot.ToggleOverlay ? "按下组合键…" : draft.ToggleOverlay.DisplayText);
        SetShortcutButtonContent(RecordIncreaseIntensityButton, _shortcutCaptureSlot == ShortcutSlot.IncreaseIntensity ? "按下组合键…" : draft.IncreaseIntensity.DisplayText);
        SetShortcutButtonContent(RecordDecreaseIntensityButton, _shortcutCaptureSlot == ShortcutSlot.DecreaseIntensity ? "按下组合键…" : draft.DecreaseIntensity.DisplayText);

        ShortcutEditorError.Text = _shortcutEditorError ?? string.Empty;
        ShortcutEditorError.Visibility = string.IsNullOrWhiteSpace(_shortcutEditorError) ? Visibility.Collapsed : Visibility.Visible;
        ShortcutEditorSuccess.Text = _shortcutEditorSuccess ?? string.Empty;
        ShortcutEditorSuccess.Visibility = string.IsNullOrWhiteSpace(_shortcutEditorSuccess) ? Visibility.Collapsed : Visibility.Visible;
        ShortcutEditorMessagePanel.Visibility = ShortcutEditorError.Visibility == Visibility.Visible || ShortcutEditorSuccess.Visibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private static void SetShortcutButtonContent(Button button, string text)
    {
        button.Content = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 194
        };
    }

    private void SetShortcutEditorError(string? error)
    {
        _shortcutEditorError = string.IsNullOrWhiteSpace(error) ? null : error;
        if (_shortcutEditorError is not null) _shortcutEditorSuccess = null;
        UpdateShortcutEditorView();
    }

    private void SetShortcutEditorSuccess(string? message)
    {
        _shortcutEditorSuccess = string.IsNullOrWhiteSpace(message) ? null : message;
        UpdateShortcutEditorView();
    }

    private bool ValidateShortcutDraft()
    {
        var draft = GetShortcutDraft();
        if (draft.TryValidate(out var error))
        {
            SetShortcutEditorError(null);
            return true;
        }
        SetShortcutEditorError(error);
        return false;
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
        CancelShortcutCapture();
        ShortcutApplyStatus.Text = string.Empty;
        ShortcutApplyStatus.Visibility = Visibility.Collapsed;
        _shortcutEditorVisible = false;
        _settingsPageVisible = true;
        MainPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Visible;
        ShortcutEditorPage.Visibility = Visibility.Collapsed;
    }

    internal void ShowShortcutEditorForRender(HotkeyConfiguration draft, bool validateDraft = false)
    {
        CancelShortcutCapture();
        _shortcutDraft = draft.Clone();
        _shortcutEditorError = null;
        _shortcutEditorSuccess = null;
        _settingsPageVisible = false;
        _shortcutEditorVisible = true;
        MainPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        ShortcutEditorPage.Visibility = Visibility.Visible;
        UpdateShortcutEditorView();
        if (validateDraft)
            ValidateShortcutDraft();
    }

    internal void ScrollSettingsToEndForRender()
    {
        SettingsScrollViewer.UpdateLayout();
        SettingsScrollViewer.ScrollToEnd();
    }

    internal void ScrollShortcutEditorToEndForRender()
    {
        ShortcutEditorScrollViewer.UpdateLayout();
        ShortcutEditorScrollViewer.ScrollToEnd();
    }

    private void CustomizeShortcutsButton_OnClick(object sender, RoutedEventArgs e)
    {
        CancelShortcutCapture();
        _shortcutDraft = _app.CurrentSettings.Hotkeys.Clone();
        ShortcutApplyStatus.Text = string.Empty;
        ShortcutApplyStatus.Visibility = Visibility.Collapsed;
        _shortcutEditorError = null;
        _shortcutEditorSuccess = null;
        _settingsPageVisible = false;
        _shortcutEditorVisible = true;
        MainPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        ShortcutEditorPage.Visibility = Visibility.Visible;
        UpdateShortcutEditorView();
        Dispatcher.BeginInvoke(new Action(() => RecordShowPanelButton.Focus()), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void RecordShortcutButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !Enum.TryParse<ShortcutSlot>(button.Tag?.ToString(), true, out var slot))
            return;

        CancelShortcutCapture();
        _shortcutCaptureSlot = slot;
        _shortcutEditorSuccess = null;
        _shortcutEditorError = null;
        button.Focus();
        _shortcutCaptureSession = new ShortcutCaptureSession(
            () => IsShortcutEditorActive,
            ShortcutCaptureOnCaptured,
            ShortcutCaptureOnFinished,
            ShortcutCaptureOnCancelled,
            ShortcutCaptureOnError,
            ShortcutCaptureOnModifiersChanged);
        if (!_shortcutCaptureSession.Start(out var startError))
        {
            _shortcutCaptureSession.Dispose();
            _shortcutCaptureSession = null;
            _shortcutCaptureSlot = null;
            SetShortcutEditorError(startError);
            return;
        }
        _shortcutCaptureActive = true;
        UpdateShortcutEditorView();
        e.Handled = true;
    }

    private void RestoreDefaultShortcutsButton_OnClick(object sender, RoutedEventArgs e)
    {
        CancelShortcutCapture();
        _shortcutDraft = new HotkeyConfiguration();
        _shortcutEditorError = null;
        SetShortcutEditorSuccess("默认快捷键已载入，点击“应用”后才会生效。");
        UpdateShortcutEditorView();
    }

    private void ApplyShortcutsButton_OnClick(object sender, RoutedEventArgs e)
    {
        CancelShortcutCapture();
        if (!ValidateShortcutDraft()) return;

        var candidate = GetShortcutDraft().Clone();
        if (!_app.TryApplyHotkeys(candidate, out var error))
        {
            SetShortcutEditorError(string.IsNullOrWhiteSpace(error) ? "快捷键无法应用，请修正后重试。" : error);
            return;
        }

        _shortcutDraft = _app.CurrentSettings.Hotkeys.Clone();
        UpdateShortcutSummary(_app.CurrentSettings.Hotkeys);
        ReturnToSettingsPage("快捷键已应用。");
    }

    private void CancelShortcutButton_OnClick(object sender, RoutedEventArgs e) => ReturnToSettingsPage();

    private void ShortcutEditorBackButton_OnClick(object sender, RoutedEventArgs e) => ReturnToSettingsPage();

    private void ReturnToSettingsPage(string? applyMessage = null)
    {
        CancelShortcutCapture();
        _shortcutDraft = null;
        _shortcutEditorError = null;
        _shortcutEditorSuccess = null;
        _shortcutEditorVisible = false;
        _settingsPageVisible = true;
        ShortcutEditorPage.Visibility = Visibility.Collapsed;
        MainPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Visible;
        ShortcutApplyStatus.Text = applyMessage ?? string.Empty;
        ShortcutApplyStatus.Visibility = string.IsNullOrWhiteSpace(applyMessage) ? Visibility.Collapsed : Visibility.Visible;
        Dispatcher.BeginInvoke(new Action(() => CustomizeShortcutsButton.Focus()), System.Windows.Threading.DispatcherPriority.Input);
    }

    internal void ReturnToMainPanel()
    {
        CancelShortcutCapture();
        _shortcutDraft = null;
        _shortcutEditorError = null;
        _shortcutEditorSuccess = null;
        ShortcutApplyStatus.Text = string.Empty;
        ShortcutApplyStatus.Visibility = Visibility.Collapsed;
        _shortcutEditorVisible = false;
        _settingsPageVisible = false;
        ShortcutEditorPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Collapsed;
        MainPage.Visibility = Visibility.Visible;
    }

    private void CloseToTrayRadio_OnChecked(object sender, RoutedEventArgs e)
    {
        if (!_updating && CloseToTrayRadio.IsChecked == true)
            _app.SetCloseToTray(true);
    }

    private void CloseToExitRadio_OnChecked(object sender, RoutedEventArgs e)
    {
        if (!_updating && CloseToExitRadio.IsChecked == true)
            _app.SetCloseToTray(false);
    }

    private void ShortcutCaptureOnCaptured(ShortcutGesture gesture)
    {
        SetDraftGesture(gesture);
        _shortcutCaptureActive = false;
        _shortcutCaptureSlot = null;
        _shortcutEditorError = null;
        _shortcutEditorSuccess = $"已录制 {gesture.DisplayText}，点击“应用”后生效。";
        ValidateShortcutDraft();
        UpdateShortcutEditorView();
    }

    private void ShortcutCaptureOnFinished()
    {
        _shortcutCaptureSession?.Dispose();
        _shortcutCaptureSession = null;
        _shortcutCaptureActive = false;
        _shortcutCaptureSlot = null;
    }

    private void ShortcutCaptureOnCancelled()
    {
        _shortcutCaptureSession?.Dispose();
        _shortcutCaptureSession = null;
        _shortcutCaptureActive = false;
        _shortcutCaptureSlot = null;
        _shortcutEditorSuccess = null;
        if (_shortcutEditorError is null)
            ValidateShortcutDraft();
        else
            UpdateShortcutEditorView();
    }

    private void ShortcutCaptureOnError(string error)
    {
        if (_shortcutCaptureActive)
            SetShortcutEditorError(error);
    }

    private void ShortcutCaptureOnModifiersChanged(uint modifiers)
    {
        if (_shortcutCaptureActive)
            SetShortcutEditorSuccess($"已按 {FormatShortcutModifiers(modifiers)}，请继续按字母、数字、方向键或 F 键。");
    }

    private void SetDraftGesture(ShortcutGesture gesture)
    {
        var draft = GetShortcutDraft();
        switch (_shortcutCaptureSlot)
        {
            case ShortcutSlot.ShowPanel:
                draft.ShowPanel = gesture;
                break;
            case ShortcutSlot.ToggleOverlay:
                draft.ToggleOverlay = gesture;
                break;
            case ShortcutSlot.IncreaseIntensity:
                draft.IncreaseIntensity = gesture;
                break;
            case ShortcutSlot.DecreaseIntensity:
                draft.DecreaseIntensity = gesture;
                break;
        }
    }

    private static string FormatShortcutModifiers(uint modifiers)
    {
        var parts = new System.Collections.Generic.List<string>(3);
        if ((modifiers & ShortcutGesture.ModifierControl) != 0) parts.Add("Ctrl");
        if ((modifiers & ShortcutGesture.ModifierAlt) != 0) parts.Add("Alt");
        if ((modifiers & ShortcutGesture.ModifierShift) != 0) parts.Add("Shift");
        return parts.Count == 0 ? "修饰键" : string.Join(" + ", parts);
    }

    private void CancelShortcutCapture()
    {
        if (!_shortcutCaptureActive && _shortcutCaptureSlot is null && _shortcutCaptureSession is null) return;
        _shortcutCaptureSession?.Dispose();
        _shortcutCaptureSession = null;
        _shortcutCaptureActive = false;
        _shortcutCaptureSlot = null;
        if (_shortcutEditorVisible)
            UpdateShortcutEditorView();
    }

    private void Window_OnDeactivated(object? sender, EventArgs e) => CancelShortcutCapture();

    private void Window_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false) CancelShortcutCapture();
    }

    internal bool HasCompleteRenderLayout(bool settingsPage, int width, int height, bool shortcutEditor = false)
    {
        var root = (FrameworkElement)Content;
        if (root.ActualWidth < width - 1 || root.ActualHeight < height - 1)
            return false;

        var elements = shortcutEditor
            ? new FrameworkElement[] { ShortcutEditorBackButton, RecordShowPanelButton, RecordToggleOverlayButton, RecordIncreaseIntensityButton, RecordDecreaseIntensityButton, RestoreDefaultShortcutsButton, CancelShortcutButton, ApplyShortcutsButton }
            : settingsPage
                ? new FrameworkElement[] { BackButton, WarmthSlider, DimSlider, AllScreensRadio, PrimaryScreenRadio, ReminderToggle, BreakComboBox, ShowPanelShortcutText, ToggleOverlayShortcutText, IntensityShortcutText, CustomizeShortcutsButton, CloseToTrayRadio, CloseToExitRadio, SettingsExitButton }
                : new FrameworkElement[] { HeaderStatus, EnabledToggle, ReadingPreviewFrame, ReadingTextureOverlay, TextureCard0, TextureCard1, TextureCard2, TextureCard3, IntensitySlider, PauseActionButton, MoreSettingsButton };

        if (elements.Any(element => element.Visibility != Visibility.Visible || element.ActualWidth < 1 || element.ActualHeight < 1))
            return false;

        var fixedChrome = shortcutEditor
            ? new FrameworkElement[] { ShortcutEditorBackButton, RestoreDefaultShortcutsButton, CancelShortcutButton, ApplyShortcutsButton }
            : settingsPage
                ? new FrameworkElement[] { BackButton, SettingsExitButton }
                : new FrameworkElement[] { HeaderStatus, EnabledToggle, PauseActionButton, MoreSettingsButton };
        return fixedChrome.All(element => IsInside(root, element, width, height));
    }

    internal bool HasExpectedRenderState(Settings settings, PauseState pause, bool settingsPage, bool errorsVisible, bool shortcutEditor = false)
    {
        if (shortcutEditor)
        {
            var editorVisible = _shortcutEditorVisible && ShortcutEditorPage.Visibility == Visibility.Visible &&
                                SettingsPage.Visibility == Visibility.Collapsed && MainPage.Visibility == Visibility.Collapsed;
            return editorVisible && _shortcutDraft is not null && (!errorsVisible || ShortcutEditorError.Visibility == Visibility.Visible);
        }

        if (settingsPage != _settingsPageVisible ||
            (settingsPage && (SettingsPage.Visibility != Visibility.Visible || MainPage.Visibility != Visibility.Collapsed || ShortcutEditorPage.Visibility != Visibility.Collapsed)) ||
            (!settingsPage && (MainPage.Visibility != Visibility.Visible || SettingsPage.Visibility != Visibility.Collapsed || ShortcutEditorPage.Visibility != Visibility.Collapsed)))
            return false;

        if (settingsPage)
            return ReminderToggle.IsChecked == settings.Reminders && BreakComboBox.IsEnabled == settings.Reminders &&
                   CloseToTrayRadio.IsChecked == settings.CloseToTray;

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
        CancelShortcutCapture();
        _shortcutDraft = null;
        _shortcutEditorVisible = false;
        _settingsPageVisible = false;
        SettingsPage.Visibility = Visibility.Collapsed;
        ShortcutEditorPage.Visibility = Visibility.Collapsed;
        MainPage.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(new Action(() => MoreSettingsButton.Focus()), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void ExitButton_OnClick(object sender, RoutedEventArgs e) => _app.ExitApplication();

    private void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        CancelShortcutCapture();
        if (AllowApplicationExit || _app.IsExiting) return;
        e.Cancel = true;
        if (_app.CurrentSettings.CloseToTray)
            _app.HidePanelToTray();
        else
            Dispatcher.BeginInvoke(new Action(_app.ExitApplication), System.Windows.Threading.DispatcherPriority.Normal);
    }
}
