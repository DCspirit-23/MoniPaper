using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using DrawingIcon = System.Drawing.Icon;
using Microsoft.Win32;

namespace PaperCare;

public partial class App : Application
{
    private const string MutexName = "Local\\PaperCare.SingleInstance";
    private const string SignalName = "Local\\PaperCare.ShowPanel";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showSignal;
    private Thread? _signalThread;
    private volatile bool _listenForSignal;
    private Forms.NotifyIcon? _trayIcon;
    private DrawingIcon? _trayIconImage;
    private Forms.ContextMenuStrip? _trayMenu;
    private Forms.ToolStripMenuItem? _trayOpen;
    private Forms.ToolStripMenuItem? _trayToggle;
    private Forms.ToolStripMenuItem? _trayPause;
    private Forms.ToolStripMenuItem? _trayResume;
    private HotkeyManager? _hotkeys;
    private OverlayManager? _overlayManager;
    private DispatcherTimer? _clockTimer;
    private DispatcherTimer? _renderDebounceTimer;
    private DateTimeOffset? _nextReminderAt;
    private bool? _lastReminders;
    private int _lastBreakMinutes;
    private bool _isExiting;
    private bool _started;

    public Settings CurrentSettings { get; private set; } = new();
    public PauseState PauseState { get; } = new();
    public new MainWindow? MainWindow { get; private set; }
    public bool IsExiting => _isExiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (e.Args.Any(IsSelfTestArgument))
        {
            var selfTestExitCode = SelfTest.Run(e.Args);
            Shutdown(selfTestExitCode);
            return;
        }

        if (e.Args.Any(IsUiRenderArgument))
        {
            var renderExitCode = UiRenderTest.Run(e.Args);
            Shutdown(renderExitCode);
            return;
        }

        if (!TryAcquireSingleInstance())
        {
            Shutdown(0);
            return;
        }

        CurrentSettings = Settings.Load(out var warning);
        _overlayManager = new OverlayManager();
        MainWindow = new MainWindow(this);
        MainWindow.SetSettingsWarning(warning);

        CreateTrayIcon();
        _hotkeys = new HotkeyManager(HandleHotkey, CurrentSettings.Hotkeys);
        if (_hotkeys.FailedHotkeys.Count > 0)
        {
            MainWindow.SetHotkeyWarning("快捷键无法注册：" + string.Join("、", _hotkeys.FailedHotkeys) + "，可能已被系统或其他程序占用，可继续使用托盘菜单。");
            _trayIcon?.ShowBalloonTip(5000, "MoniPaper", "部分快捷键已被其他程序占用，托盘菜单仍可使用。", Forms.ToolTipIcon.Warning);
        }

        _clockTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += ClockTimerOnTick;
        _clockTimer.Start();

        _renderDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _renderDebounceTimer.Tick += RenderDebounceTimerOnTick;

        SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
        _started = true;
        ApplySettings(persist: false);
        MainWindow.Show();
        MainWindow.Activate();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        base.OnExit(e);
    }

    private static bool IsSelfTestArgument(string argument) =>
        string.Equals(argument, "--self-test", StringComparison.OrdinalIgnoreCase);

    private static bool IsUiRenderArgument(string argument) =>
        string.Equals(argument, "--render-ui", StringComparison.OrdinalIgnoreCase);

    private bool TryAcquireSingleInstance()
    {
        try
        {
            _instanceMutex = new Mutex(true, MutexName, out var createdNew);
            if (!createdNew)
            {
                SignalExistingInstance();
                _instanceMutex.Dispose();
                _instanceMutex = null;
                return false;
            }

            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
            StartSignalListener();
            return true;
        }
        catch (Exception)
        {
            _showSignal?.Dispose();
            _showSignal = null;
            _instanceMutex?.Dispose();
            _instanceMutex = null;
            MessageBox.Show("MoniPaper 无法建立单实例控制，请检查当前 Windows 用户权限。", "MoniPaper", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(SignalName);
            signal.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            // The other process can be between startup and event creation. Its
            // mutex still prevents a second panel, so there is nothing else to do.
        }
        catch (UnauthorizedAccessException)
        {
            // A second launch should never prevent the already running app from working.
        }
    }

    private void StartSignalListener()
    {
        if (_showSignal is null) return;
        _listenForSignal = true;
        _signalThread = new Thread(() =>
        {
            while (_listenForSignal)
            {
                try
                {
                    if (!_showSignal.WaitOne()) break;
                    if (_listenForSignal)
                        Dispatcher.BeginInvoke(new Action(ShowPanel));
                }
                catch (ObjectDisposedException) { break; }
                catch (InvalidOperationException) { break; }
            }
        })
        {
            IsBackground = true,
            Name = "PaperCare single-instance signal"
        };
        _signalThread.Start();
    }

    private void CreateTrayIcon()
    {
        _trayMenu = new Forms.ContextMenuStrip();
        _trayOpen = new Forms.ToolStripMenuItem("打开面板", null, (_, _) => Dispatcher.BeginInvoke(new Action(ShowPanel)));
        _trayToggle = new Forms.ToolStripMenuItem("总开关", null, (_, _) => Dispatcher.BeginInvoke(new Action(ToggleEnabled)));
        _trayPause = new Forms.ToolStripMenuItem("暂停 10 分钟", null, (_, _) => Dispatcher.BeginInvoke(new Action(PauseForTenMinutes)));
        _trayResume = new Forms.ToolStripMenuItem("恢复覆盖", null, (_, _) => Dispatcher.BeginInvoke(new Action(ResumePause)));
        var exit = new Forms.ToolStripMenuItem("退出", null, (_, _) => Dispatcher.BeginInvoke(new Action(ExitApplication)));
        _trayMenu.Items.AddRange(new Forms.ToolStripItem[] { _trayOpen, _trayToggle, _trayPause, _trayResume, new Forms.ToolStripSeparator(), exit });

        _trayIconImage = CreateTrayIconImage();
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = _trayIconImage,
            Text = "MoniPaper",
            Visible = true,
            ContextMenuStrip = _trayMenu
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.BeginInvoke(new Action(ShowPanel));
    }

    private static DrawingIcon CreateTrayIconImage()
    {
        var resource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/Assets/papercare.ico", UriKind.Absolute));
        if (resource is null)
            throw new InvalidOperationException("找不到 MoniPaper 图标资源。");

        using (resource.Stream)
        using (var buffer = new MemoryStream())
        {
            resource.Stream.CopyTo(buffer);
            buffer.Position = 0;
            using var source = new DrawingIcon(buffer);
            return new DrawingIcon(source, source.Size);
        }
    }

    private void ClockTimerOnTick(object? sender, EventArgs e)
    {
        if (!_started || _isExiting) return;
        var now = DateTimeOffset.Now;
        if (PauseState.Until is { } until && now >= until)
        {
            PauseState.Resume();
            ApplySettings(persist: true);
        }

        MainWindow?.UpdatePauseStatus(now, PauseState);

        if (CurrentSettings.Reminders && _nextReminderAt is { } reminderAt && now >= reminderAt)
        {
            _trayIcon?.ShowBalloonTip(5000, "MoniPaper", "休息一下，看看远处，活动肩颈。", Forms.ToolTipIcon.Info);
            do reminderAt = reminderAt.AddMinutes(CurrentSettings.BreakMinutes);
            while (reminderAt <= now);
            _nextReminderAt = reminderAt;
        }

        // Reassert topmost without re-rendering the texture. The expensive bitmap
        // work is performed only by ApplySettings after a real setting change.
        if (_overlayManager?.IsShowing == true && now.Second % 15 == 0)
            _overlayManager.RaiseAll();
    }

    private void RenderDebounceTimerOnTick(object? sender, EventArgs e)
    {
        _renderDebounceTimer?.Stop();
        ApplySettings(persist: true);
    }

    private void DisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (_isExiting) return;
        Dispatcher.BeginInvoke(new Action(() => ApplySettings(persist: false)));
    }

    private void ApplySettings(bool persist)
    {
        if (_isExiting) return;
        CurrentSettings.Normalize();
        var now = DateTimeOffset.Now;
        if (_lastReminders != CurrentSettings.Reminders ||
            (CurrentSettings.Reminders && _lastBreakMinutes != CurrentSettings.BreakMinutes))
        {
            _nextReminderAt = CurrentSettings.Reminders ? now.AddMinutes(CurrentSettings.BreakMinutes) : null;
            _lastReminders = CurrentSettings.Reminders;
            _lastBreakMinutes = CurrentSettings.BreakMinutes;
        }

        if (_overlayManager is not null && !_overlayManager.Apply(CurrentSettings, PauseState.IsPaused(now), out var overlayError))
            MainWindow?.SetSettingsWarning(overlayError);

        if (persist && !CurrentSettings.TrySave(out var saveWarning))
            MainWindow?.SetSettingsWarning(saveWarning);

        UpdateTrayMenu();
        MainWindow?.RefreshFromSettings(CurrentSettings, PauseState);
    }

    private void UpdateTrayMenu()
    {
        if (_trayToggle is not null) _trayToggle.Checked = CurrentSettings.Enabled;
        if (_trayPause is not null) _trayPause.Enabled = CurrentSettings.Enabled && !PauseState.IsPaused(DateTimeOffset.Now);
        if (_trayResume is not null) _trayResume.Enabled = PauseState.IsPaused(DateTimeOffset.Now);
    }

    private void HandleHotkey(HotkeyAction action)
    {
        if (_isExiting || MainWindow?.IsShortcutEditorActive == true) return;
        switch (action)
        {
            case HotkeyAction.ShowPanel:
                MainWindow?.ReturnToMainPanel();
                ShowPanel();
                break;
            case HotkeyAction.ToggleOverlay:
                ToggleEnabled();
                break;
            case HotkeyAction.IncreaseIntensity:
                SetIntensity(CurrentSettings.Intensity + 10);
                break;
            case HotkeyAction.DecreaseIntensity:
                SetIntensity(CurrentSettings.Intensity - 10);
                break;
        }
    }

    public bool TryApplyHotkeys(HotkeyConfiguration candidate, out string? error)
    {
        error = null;
        if (_isExiting)
        {
            error = "应用正在退出，无法修改快捷键。";
            return false;
        }

        if (candidate is null || !candidate.TryValidate(out error))
        {
            MainWindow?.SetHotkeyWarning(error);
            return false;
        }

        if (_hotkeys is null)
        {
            error = "快捷键尚未初始化。";
            MainWindow?.SetHotkeyWarning(error);
            return false;
        }

        var oldSettings = CurrentSettings.Clone();
        var proposedSettings = CurrentSettings.Clone();
        proposedSettings.Hotkeys = candidate.Clone();
        string? saveWarning = null;
        var applied = _hotkeys.TryApply(
            candidate,
            () => proposedSettings.TrySave(out saveWarning),
            () => oldSettings.TrySave(out _),
            out error);

        if (!applied)
        {
            error ??= saveWarning ?? "快捷键修改失败。";
            MainWindow?.SetHotkeyWarning(error);
            return false;
        }

        CurrentSettings.Hotkeys = candidate.Clone();
        MainWindow?.SetHotkeyWarning(null);
        MainWindow?.RefreshFromSettings(CurrentSettings, PauseState, renderPreview: false);
        return true;
    }

    public void SetCloseToTray(bool value)
    {
        if (_isExiting) return;
        CurrentSettings.CloseToTray = value;
        ApplySettings(persist: true);
    }

    internal void ToggleEnabled()
    {
        SetEnabled(!CurrentSettings.Enabled);
    }

    internal void SetEnabled(bool enabled)
    {
        if (CurrentSettings.Enabled == enabled && (enabled || !PauseState.IsPaused(DateTimeOffset.Now))) return;
        CurrentSettings.Enabled = enabled;
        if (!enabled)
            PauseState.Resume();
        ApplySettings(persist: true);
    }

    internal void SetTexture(int texture)
    {
        CurrentSettings.Texture = texture;
        ScheduleRender();
    }

    internal void SetIntensity(int value)
    {
        CurrentSettings.Intensity = Math.Clamp(value, 0, 100);
        ScheduleRender();
    }

    internal void SetWarmth(int value)
    {
        CurrentSettings.Warmth = Math.Clamp(value, 0, 100);
        ScheduleRender();
    }

    internal void SetDim(int value)
    {
        CurrentSettings.Dim = Math.Clamp(value, 0, 50);
        ScheduleRender();
    }

    internal void SetAllScreens(bool allScreens)
    {
        CurrentSettings.AllScreens = allScreens;
        ApplySettings(persist: true);
    }

    internal void SetReminders(bool enabled)
    {
        CurrentSettings.Reminders = enabled;
        ApplySettings(persist: true);
    }

    internal void SetBreakMinutes(int minutes)
    {
        if (!Settings.BreakOptions.Contains(minutes)) return;
        CurrentSettings.BreakMinutes = minutes;
        ApplySettings(persist: true);
    }

    private void ScheduleRender()
    {
        _renderDebounceTimer?.Stop();
        _renderDebounceTimer?.Start();
        MainWindow?.RefreshFromSettings(CurrentSettings, PauseState, renderPreview: false);
    }

    internal void PauseForTenMinutes()
    {
        if (!CurrentSettings.Enabled) return;
        PauseState.Pause(DateTimeOffset.Now);
        ApplySettings(persist: true);
    }

    internal void ResumePause()
    {
        PauseState.Resume();
        ApplySettings(persist: true);
    }

    internal void ShowPanel()
    {
        if (_isExiting || MainWindow is null) return;
        MainWindow.ReturnToMainPanel();
        MainWindow.ShowInTaskbar = true;
        if (MainWindow.WindowState == WindowState.Minimized)
            MainWindow.WindowState = WindowState.Normal;
        if (!MainWindow.IsVisible)
            MainWindow.Show();
        MainWindow.Activate();
        MainWindow.Focus();
    }

    internal void HidePanelToTray()
    {
        if (MainWindow is null) return;
        MainWindow.ShowInTaskbar = false;
        MainWindow.Hide();
    }

    internal void ExitApplication()
    {
        if (_isExiting) return;
        if (_renderDebounceTimer?.IsEnabled == true)
        {
            _renderDebounceTimer.Stop();
            ApplySettings(persist: true);
        }
        _isExiting = true;
        Shutdown(0);
    }

    internal void SetHotkeyWarning(string warning) => MainWindow?.SetHotkeyWarning(warning);

    private void Cleanup()
    {
        if (!_isExiting) _isExiting = true;
        SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
        _clockTimer?.Stop();
        _renderDebounceTimer?.Stop();
        _hotkeys?.Dispose();
        _hotkeys = null;
        _overlayManager?.Dispose();
        _overlayManager = null;

        if (MainWindow is not null)
        {
            MainWindow.AllowApplicationExit = true;
            MainWindow.Close();
            MainWindow = null;
        }

        if (_trayIcon is not null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _trayIconImage?.Dispose();
        _trayIconImage = null;
        _trayMenu?.Dispose();
        _trayMenu = null;

        _listenForSignal = false;
        try { _showSignal?.Set(); } catch (ObjectDisposedException) { }
        if (_signalThread is { IsAlive: true } && !ReferenceEquals(Thread.CurrentThread, _signalThread))
            _signalThread.Join(500);
        _showSignal?.Dispose();
        _showSignal = null;

        if (_instanceMutex is not null)
        {
            try { _instanceMutex.ReleaseMutex(); } catch (ApplicationException) { }
            _instanceMutex.Dispose();
            _instanceMutex = null;
        }
    }
}
