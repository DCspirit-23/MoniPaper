using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PaperCare;

internal static class UiRenderTest
{
    public static int Run(string[] args)
    {
        var outputDirectory = GetOutputDirectory(args);
        Directory.CreateDirectory(outputDirectory);
        var results = new List<RenderResult>();
        var captureTests = new List<CaptureResult>();
        var allPassed = true;

        RunCaptureTests(captureTests, ref allPassed);

        var defaults = new Settings
        {
            Enabled = false,
            Texture = 0,
            Intensity = 30,
            Warmth = 10,
            Dim = 0,
            AllScreens = true,
            Reminders = false,
            BreakMinutes = 20,
            CloseToTray = true
        };

        RunRender(results, "main-default", Path.Combine(outputDirectory, "ui-redesign-main-default.png"),
            defaults, new PauseState(), settingsPage: false, width: 460, viewportHeight: 520, windowHeight: 560, ref allPassed);

        var paused = new PauseState();
        paused.Pause(DateTimeOffset.Now);
        var enabled = new Settings
        {
            Enabled = true,
            Texture = 1,
            Intensity = 48,
            Warmth = 18,
            Dim = 5,
            AllScreens = true,
            Reminders = false,
            BreakMinutes = 20
        };
        RunRender(results, "main-enabled", Path.Combine(outputDirectory, "ui-redesign-main-enabled.png"),
            enabled, new PauseState(), settingsPage: false, width: 460, viewportHeight: 520, windowHeight: 560, ref allPassed);
        RunRender(results, "main-paused", Path.Combine(outputDirectory, "ui-redesign-main-paused.png"),
            enabled, paused, settingsPage: false, width: 460, viewportHeight: 520, windowHeight: 560, ref allPassed);

        RunRender(results, "settings", Path.Combine(outputDirectory, "ui-redesign-settings.png"),
            enabled, new PauseState(), settingsPage: true, width: 460, viewportHeight: 520, windowHeight: 560, ref allPassed);

        var settingsTray = defaults.Clone();
        settingsTray.CloseToTray = true;
        RunRender(results, "settings-bottom-tray", Path.Combine(outputDirectory, "ui-redesign-settings-bottom-tray.png"),
            settingsTray, new PauseState(), settingsPage: true, width: 460, viewportHeight: 520, windowHeight: 560, ref allPassed,
            scrollToEnd: true);

        var settingsExit = defaults.Clone();
        settingsExit.CloseToTray = false;
        RunRender(results, "settings-bottom-exit", Path.Combine(outputDirectory, "ui-redesign-settings-bottom-exit.png"),
            settingsExit, new PauseState(), settingsPage: true, width: 460, viewportHeight: 520, windowHeight: 560, ref allPassed,
            scrollToEnd: true);

        RunRender(results, "main-minimum", Path.Combine(outputDirectory, "ui-redesign-main-minimum.png"),
            defaults, new PauseState(), settingsPage: false, width: 400, viewportHeight: 500, windowHeight: 540, ref allPassed);

        RunRender(results, "main-long-error", Path.Combine(outputDirectory, "ui-redesign-main-long-error.png"),
            defaults, new PauseState(), settingsPage: false, width: 460, viewportHeight: 520, windowHeight: 560, ref allPassed,
            longErrors: true);

        RunRender(results, "shortcut-editor", Path.Combine(outputDirectory, "ui-redesign-shortcut-editor.png"),
            defaults, new PauseState(), settingsPage: false, width: 460, viewportHeight: 520, windowHeight: 560, ref allPassed,
            shortcutEditor: true);

        var duplicateHotkeys = defaults.Hotkeys.Clone();
        duplicateHotkeys.ToggleOverlay = duplicateHotkeys.ShowPanel;
        var duplicateSettings = defaults.Clone();
        duplicateSettings.Hotkeys = duplicateHotkeys;
        RunRender(results, "shortcut-editor-duplicate", Path.Combine(outputDirectory, "ui-redesign-shortcut-editor-duplicate.png"),
            duplicateSettings, new PauseState(), settingsPage: false, width: 460, viewportHeight: 520, windowHeight: 560, ref allPassed,
            shortcutEditor: true, validateShortcutDraft: true);

        var longHotkeys = defaults.Hotkeys.Clone();
        const uint longModifiers = ShortcutGesture.ModifierControl | ShortcutGesture.ModifierAlt | ShortcutGesture.ModifierShift;
        longHotkeys.ShowPanel = new ShortcutGesture(longModifiers, 0x22);
        longHotkeys.ToggleOverlay = new ShortcutGesture(longModifiers, 0x24);
        longHotkeys.IncreaseIntensity = new ShortcutGesture(longModifiers, 0x21);
        longHotkeys.DecreaseIntensity = new ShortcutGesture(longModifiers, 0x23);
        var longSettings = defaults.Clone();
        longSettings.Hotkeys = longHotkeys;
        RunRender(results, "shortcut-editor-long-small", Path.Combine(outputDirectory, "ui-redesign-shortcut-editor-long-small.png"),
            longSettings, new PauseState(), settingsPage: false, width: 400, viewportHeight: 500, windowHeight: 540, ref allPassed,
            shortcutEditor: true);

        var result = new
        {
            product = "MoniPaper",
            executedAt = DateTimeOffset.Now,
            passed = allPassed,
            outputDirectory,
            renders = results,
            captureTests
        };
        var resultPath = Path.Combine(outputDirectory, "ui-redesign-render-results.json");
        File.WriteAllText(resultPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"MoniPaper UI render: {(allPassed ? "PASS" : "FAIL")}");
        Console.WriteLine($"Result: {resultPath}");
        return allPassed ? 0 : 1;
    }

    private static void RunRender(
        ICollection<RenderResult> results,
        string name,
        string outputPath,
        Settings settings,
        PauseState pause,
        bool settingsPage,
        int width,
        int viewportHeight,
        int windowHeight,
        ref bool allPassed,
        bool longErrors = false,
        bool shortcutEditor = false,
        bool scrollToEnd = false,
        bool validateShortcutDraft = false)
    {
        try
        {
            var app = (App)Application.Current;
            var window = new MainWindow(app)
            {
                Width = width,
                Height = windowHeight
            };
            window.RefreshFromSettings(settings, pause);
            if (longErrors)
            {
                window.SetHotkeyWarning("快捷键不可用：Ctrl + Alt + O、Ctrl + Alt + P、Ctrl + Alt + ↑ / ↓ 可能已被其他程序占用，请继续使用托盘菜单操作。 ");
                window.SetSettingsWarning("设置无法保存，请检查配置目录的写入权限；当前修改会在下次成功保存后保留。 ");
            }
            if (shortcutEditor)
                window.ShowShortcutEditorForRender(settings.Hotkeys, validateShortcutDraft);
            else if (settingsPage)
                window.ShowSettingsPageForRender();

            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(width, viewportHeight));
            root.Arrange(new Rect(0, 0, width, viewportHeight));
            root.UpdateLayout();
            if (scrollToEnd)
            {
                if (settingsPage) window.ScrollSettingsToEndForRender();
                if (shortcutEditor) window.ScrollShortcutEditorToEndForRender();
                root.UpdateLayout();
            }
            var layoutPassed = window.HasCompleteRenderLayout(settingsPage, width, viewportHeight, shortcutEditor);
            var statePassed = window.HasExpectedRenderState(settings, pause, settingsPage, longErrors, shortcutEditor);
            var passed = layoutPassed && statePassed;
            SavePng(root, width, viewportHeight, outputPath);
            results.Add(new RenderResult(name, passed, layoutPassed, statePassed, outputPath, null));
            if (!passed) allPassed = false;
        }
        catch (Exception ex)
        {
            results.Add(new RenderResult(name, false, false, false, outputPath, ex.GetType().Name + ": " + ex.Message));
            allPassed = false;
        }
    }

    private static void SavePng(FrameworkElement root, int width, int height, string outputPath)
    {
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    private static void RunCaptureTests(ICollection<CaptureResult> results, ref bool allPassed)
    {
        RunCaptureCase(results, "capture-sequence-and-keyup", () =>
        {
            var captured = new List<ShortcutGesture>();
            var finished = 0;
            using var session = new ShortcutCaptureSession(
                () => true,
                captured.Add,
                () => finished++,
                () => { },
                _ => { },
                _ => { });

            // Ctrl + Alt + P, a repeated P, and its key-up must produce one
            // gesture while keeping the candidate suppressed until release.
            session.ProcessKeyForTest(0xA2, true);
            session.ProcessKeyForTest(0xA4, true);
            session.ProcessKeyForTest(0x50, true);
            session.ProcessKeyForTest(0x50, true);
            session.ProcessKeyForTest(0x50, false);
            return captured.Count == 1 && finished == 1 && captured[0] == new ShortcutGesture(3, 0x50) && !session.IsActive;
        }, ref allPassed);

        RunCaptureCase(results, "capture-cancel-and-invalid-navigation", () =>
        {
            var cancelled = 0;
            var errors = new List<string>();
            using var session = new ShortcutCaptureSession(
                () => true,
                _ => { },
                () => { },
                () => cancelled++,
                errors.Add,
                _ => { });

            session.ProcessKeyForTest(0x1B, true);
            var escapeCancelled = cancelled == 1;
            session.ProcessKeyForTest(0xA4, true);
            session.ProcessKeyForTest(0x09, true);
            var altTabRejected = errors.Count == 1 && cancelled == 1;
            session.Stop();
            return escapeCancelled && altTabRejected;
        }, ref allPassed);

        RunCaptureCase(results, "capture-tab-and-focus-loss", () =>
        {
            var cancelledByTab = 0;
            using var tabSession = new ShortcutCaptureSession(() => true, _ => { }, () => { }, () => cancelledByTab++, _ => { }, _ => { });
            tabSession.ProcessKeyForTest(0xA0, true);
            tabSession.ProcessKeyForTest(0x09, true);
            var tabCancelled = cancelledByTab == 1;

            var active = true;
            var cancelledByFocus = 0;
            using var focusSession = new ShortcutCaptureSession(() => active, _ => { }, () => { }, () => cancelledByFocus++, _ => { }, _ => { });
            active = false;
            focusSession.ProcessKeyForTest(0x50, true);
            return tabCancelled && cancelledByFocus == 1 && !focusSession.IsActive;
        }, ref allPassed);

        RunCaptureCase(results, "capture-hook-install-and-release", () =>
        {
            using var session = new ShortcutCaptureSession(() => true, _ => { }, () => { }, () => { }, _ => { }, _ => { });
            var started = session.Start(out _);
            var active = session.IsActive;
            session.Stop();
            return started && active && !session.IsActive;
        }, ref allPassed);
    }

    private static void RunCaptureCase(ICollection<CaptureResult> results, string name, Func<bool> test, ref bool allPassed)
    {
        try
        {
            var passed = test();
            results.Add(new CaptureResult(name, passed, null));
            if (!passed) allPassed = false;
        }
        catch (Exception ex)
        {
            results.Add(new CaptureResult(name, false, ex.GetType().Name + ": " + ex.Message));
            allPassed = false;
        }
    }

    private static string GetOutputDirectory(IEnumerable<string> args)
    {
        const string prefix = "--render-ui-output=";
        var requested = args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (requested is not null && requested.Length > prefix.Length)
            return Path.GetFullPath(requested.Substring(prefix.Length).Trim('"'));
        return Path.Combine(Environment.CurrentDirectory, "artifacts");
    }

    private sealed record RenderResult(string Name, bool Passed, bool LayoutPassed, bool StatePassed, string OutputPath, string? Error);
    private sealed record CaptureResult(string Name, bool Passed, string? Error);
}
