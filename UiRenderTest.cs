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
        var allPassed = true;

        var defaults = new Settings
        {
            Enabled = false,
            Texture = 0,
            Intensity = 30,
            Warmth = 10,
            Dim = 0,
            AllScreens = true,
            Reminders = false,
            BreakMinutes = 20
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

        RunRender(results, "main-minimum", Path.Combine(outputDirectory, "ui-redesign-main-minimum.png"),
            defaults, new PauseState(), settingsPage: false, width: 400, viewportHeight: 500, windowHeight: 540, ref allPassed);

        RunRender(results, "main-long-error", Path.Combine(outputDirectory, "ui-redesign-main-long-error.png"),
            defaults, new PauseState(), settingsPage: false, width: 460, viewportHeight: 520, windowHeight: 560, ref allPassed,
            longErrors: true);

        var result = new
        {
            product = "PaperCare",
            executedAt = DateTimeOffset.Now,
            passed = allPassed,
            outputDirectory,
            renders = results
        };
        var resultPath = Path.Combine(outputDirectory, "ui-redesign-render-results.json");
        File.WriteAllText(resultPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PaperCare UI render: {(allPassed ? "PASS" : "FAIL")}");
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
        bool longErrors = false)
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
                window.SetHotkeyWarning("快捷键不可用：Ctrl + Alt + P、Ctrl + Alt + ↑ / ↓ 可能已被其他程序占用，请继续使用托盘菜单操作。 ");
                window.SetSettingsWarning("设置无法保存，请检查 PaperCare 文件夹的写入权限；当前修改会在下次成功保存后保留。 ");
            }
            if (settingsPage)
                window.ShowSettingsPageForRender();

            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(width, viewportHeight));
            root.Arrange(new Rect(0, 0, width, viewportHeight));
            root.UpdateLayout();
            var layoutPassed = window.HasCompleteRenderLayout(settingsPage, width, viewportHeight);
            var statePassed = window.HasExpectedRenderState(settings, pause, settingsPage, longErrors);
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

    private static string GetOutputDirectory(IEnumerable<string> args)
    {
        const string prefix = "--render-ui-output=";
        var requested = args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (requested is not null && requested.Length > prefix.Length)
            return Path.GetFullPath(requested.Substring(prefix.Length).Trim('"'));
        return Path.Combine(Environment.CurrentDirectory, "artifacts");
    }

    private sealed record RenderResult(string Name, bool Passed, bool LayoutPassed, bool StatePassed, string OutputPath, string? Error);
}
