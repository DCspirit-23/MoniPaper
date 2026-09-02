using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using Forms = System.Windows.Forms;

namespace PaperCare;

internal static class SelfTest
{
    public static int Run(string[] args)
    {
        var cases = new List<object>();
        var allPassed = true;
        RunCase(cases, "settings-normalize", () =>
        {
            var value = new Settings { Texture = 99, Intensity = -5, Warmth = 101, Dim = 99, BreakMinutes = 35 };
            value.Normalize();
            return value.Texture == 3 && value.Intensity == 0 && value.Warmth == 100 && value.Dim == 50 && value.BreakMinutes == 20;
        }, ref allPassed);

        RunCase(cases, "settings-json-roundtrip", () =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "PaperCare-self-test-" + Guid.NewGuid().ToString("N"));
            var path = Path.Combine(directory, "settings.json");
            try
            {
                Directory.CreateDirectory(directory);
                var expected = new Settings { Enabled = true, Texture = 2, Intensity = 67, Warmth = 41, Dim = 18, AllScreens = false, Reminders = true, BreakMinutes = 45 };
                File.WriteAllText(path, JsonSerializer.Serialize(expected));
                var actual = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path));
                actual?.Normalize();
                return actual is not null && actual.Enabled == expected.Enabled && actual.Texture == expected.Texture &&
                       actual.Intensity == expected.Intensity && actual.Warmth == expected.Warmth && actual.Dim == expected.Dim &&
                       actual.AllScreens == expected.AllScreens && actual.Reminders == expected.Reminders && actual.BreakMinutes == expected.BreakMinutes;
            }
            finally
            {
                try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }, ref allPassed);

        RunCase(cases, "pause-boundary", () =>
        {
            var now = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.FromHours(9));
            var pause = new PauseState();
            pause.Pause(now);
            var until = pause.Until;
            return until is not null && pause.IsPaused(until.Value.AddTicks(-1)) && !pause.IsPaused(until.Value);
        }, ref allPassed);

        RunCase(cases, "texture-transparent-and-premultiplied", () =>
        {
            var empty = new Settings { Intensity = 0, Warmth = 0, Dim = 0 };
            var emptyTile = TextureRenderer.Tile(empty);
            if (!TextureRenderer.IsFullyTransparent(emptyTile) || emptyTile.Any(value => value != 0)) return false;

            var settings = new Settings { Texture = 1, Intensity = 86, Warmth = 73, Dim = 32 };
            var tile = TextureRenderer.Tile(settings);
            for (var i = 0; i < tile.Length; i += 4)
                if (tile[i] > tile[i + 3] || tile[i + 1] > tile[i + 3] || tile[i + 2] > tile[i + 3]) return false;
            for (var i = 3; i < tile.Length; i += 4)
                if (tile[i] > 0) return true;
            return false;
        }, ref allPassed);

        RunCase(cases, "display-enumeration", () =>
        {
            var screens = Forms.Screen.AllScreens;
            return screens.Length > 0 && Forms.Screen.PrimaryScreen is not null;
        }, ref allPassed);

        RunCase(cases, "small-overlay-lifecycle", () =>
        {
            var settings = new Settings { Texture = 0, Intensity = 35, Warmth = 0, Dim = 0 };
            var tile = TextureRenderer.Tile(settings);
            var bounds = new Rectangle(0, 0, 64, 48);
            var overlay = new Overlay(bounds);
            try
            {
                overlay.Render(tile);
                var handle = overlay.Handle;
                var style = Native.GetWindowLong(handle, -20);
                const int ExLayered = 0x00080000;
                const int ExTransparent = 0x00000020;
                const int ExNoActivate = 0x08000000;
                const int ExToolWindow = 0x00000080;
                var flagsPresent = (style & ExLayered) != 0 && (style & ExTransparent) != 0 &&
                                    (style & ExNoActivate) != 0 && (style & ExToolWindow) != 0;
                var existsWhileShown = Native.IsWindow(handle);
                overlay.Dispose();
                var removed = !Native.IsWindow(handle);
                return flagsPresent && existsWhileShown && removed;
            }
            finally
            {
                overlay.Dispose();
            }
        }, ref allPassed);

        var outputPath = GetOutputPath(args);
        var result = new
        {
            product = "PaperCare",
            executedAt = DateTimeOffset.Now,
            passed = allPassed,
            tests = cases
        };
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        var wroteResult = TryWriteResult(outputPath, json, out var actualPath);
        if (!wroteResult) allPassed = false;

        // Keep the result itself useful even when the requested location is not writable.
        Console.WriteLine($"PaperCare self-test: {(allPassed ? "PASS" : "FAIL")}");
        Console.WriteLine($"Result: {actualPath}");
        return allPassed && wroteResult ? 0 : 1;
    }

    private static void RunCase(ICollection<object> cases, string name, Func<bool> test, ref bool allPassed)
    {
        try
        {
            var passed = test();
            cases.Add(new { name, passed, error = (string?)null });
            if (!passed) allPassed = false;
        }
        catch (Exception ex)
        {
            cases.Add(new { name, passed = false, error = ex.GetType().Name + ": " + ex.Message });
            allPassed = false;
        }
    }

    private static string GetOutputPath(IEnumerable<string> args)
    {
        const string prefix = "--self-test-output=";
        var requested = args.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?.Substring(prefix.Length);
        return string.IsNullOrWhiteSpace(requested)
            ? Path.Combine(AppContext.BaseDirectory, "self-test-results.json")
            : Path.GetFullPath(requested);
    }

    private static bool TryWriteResult(string requestedPath, string json, out string actualPath)
    {
        try
        {
            File.WriteAllText(requestedPath, json);
            actualPath = requestedPath;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            try
            {
                actualPath = Path.Combine(Path.GetTempPath(), "PaperCare-self-test-results.json");
                File.WriteAllText(actualPath, json);
                return true;
            }
            catch (Exception fallbackError) when (fallbackError is IOException or UnauthorizedAccessException or ArgumentException)
            {
                actualPath = requestedPath;
                return false;
            }
        }
    }
}
