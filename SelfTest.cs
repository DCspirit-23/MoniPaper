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
            var directory = Path.Combine(Path.GetTempPath(), "MoniPaper-self-test-" + Guid.NewGuid().ToString("N"));
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

        RunCase(cases, "hotkey-validation", TestHotkeyValidation, ref allPassed);
        RunCase(cases, "settings-legacy-json-compatibility", TestLegacyJsonCompatibility, ref allPassed);
        RunCase(cases, "settings-custom-hotkeys-and-close-behavior-roundtrip", TestCustomSettingsRoundtrip, ref allPassed);
        RunCase(cases, "settings-invalid-hotkeys-preserve-legacy-values", TestInvalidHotkeysPreserveLegacyValues, ref allPassed);
        RunCase(cases, "hotkey-successful-rebind", TestSuccessfulRebind, ref allPassed);
        RunCase(cases, "hotkey-internal-swap", TestInternalSwap, ref allPassed);
        RunCase(cases, "hotkey-registration-conflict-keeps-old-binding", TestRegistrationConflictKeepsOldBinding, ref allPassed);
        RunCase(cases, "hotkey-persistence-failure-keeps-old-binding", TestPersistenceFailureKeepsOldBinding, ref allPassed);
        RunCase(cases, "hotkey-show-panel-dispatch", TestShowPanelDispatch, ref allPassed);
        RunCase(cases, "hotkey-dispose-releases-registrations", TestDisposeReleasesRegistrations, ref allPassed);
        RunCase(cases, "hotkey-real-win32-conflict", TestRealWin32Conflict, ref allPassed);

        var outputPath = GetOutputPath(args);
        var result = new
        {
            product = "MoniPaper",
            executedAt = DateTimeOffset.Now,
            passed = allPassed,
            tests = cases
        };
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        var wroteResult = TryWriteResult(outputPath, json, out var actualPath);
        if (!wroteResult) allPassed = false;

        // Keep the result itself useful even when the requested location is not writable.
        Console.WriteLine($"MoniPaper self-test: {(allPassed ? "PASS" : "FAIL")}");
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

    private static bool TestHotkeyValidation()
    {
        var defaults = new HotkeyConfiguration();
        var defaultsValid = defaults.TryValidate(out var defaultsError) && defaultsError is null &&
                             defaults.ShowPanel.DisplayText == "Ctrl + Alt + O" &&
                             defaults.ToggleOverlay.DisplayText == "Ctrl + Alt + P" &&
                             defaults.IncreaseIntensity.DisplayText == "Ctrl + Alt + ↑" &&
                             defaults.DecreaseIntensity.DisplayText == "Ctrl + Alt + ↓";

        var duplicate = defaults.Clone();
        duplicate.ToggleOverlay = duplicate.ShowPanel;
        var duplicateRejected = !duplicate.TryValidate(out var duplicateError) &&
                                duplicateError is not null && duplicateError.Contains("打开面板", StringComparison.Ordinal) &&
                                duplicateError.Contains("开关覆盖", StringComparison.Ordinal) &&
                                duplicateError.Contains(defaults.ShowPanel.DisplayText, StringComparison.Ordinal);

        var f12 = defaults.Clone();
        f12.ShowPanel = new ShortcutGesture(ShortcutGesture.ModifierControl, 0x7B);
        var f12Rejected = !f12.TryValidate(out var f12Error) && f12Error?.Contains("系统保留", StringComparison.Ordinal) == true;

        var windowsKey = defaults.Clone();
        windowsKey.ShowPanel = new ShortcutGesture(0x0008 | ShortcutGesture.ModifierControl, 0x41);
        var windowsRejected = !windowsKey.TryValidate(out var windowsError) && windowsError?.Contains("Windows", StringComparison.Ordinal) == true;

        var noModifier = defaults.Clone();
        noModifier.ShowPanel = new ShortcutGesture(0, 0x41);
        var noModifierRejected = !noModifier.TryValidate(out var noModifierError) && noModifierError?.Contains("至少需要", StringComparison.Ordinal) == true;

        var invalidKey = defaults.Clone();
        invalidKey.ShowPanel = new ShortcutGesture(ShortcutGesture.ModifierControl, 0x01);
        var invalidKeyRejected = !invalidKey.TryValidate(out var invalidKeyError) && invalidKeyError?.Contains("按键无效", StringComparison.Ordinal) == true;

        var reserved = defaults.Clone();
        reserved.ShowPanel = new ShortcutGesture(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierShift, 0x1B);
        var reservedRejected = !reserved.TryValidate(out var reservedError) && reservedError?.Contains("系统保留", StringComparison.Ordinal) == true;

        return defaultsValid && duplicateRejected && f12Rejected && windowsRejected && noModifierRejected &&
               invalidKeyRejected && reservedRejected;
    }

    private static bool TestLegacyJsonCompatibility()
    {
        return WithTemporarySettingsPath(path =>
        {
            File.WriteAllText(path, "{\"Enabled\":true,\"Texture\":2,\"Intensity\":67,\"Warmth\":41,\"Dim\":18,\"AllScreens\":false,\"Reminders\":true,\"BreakMinutes\":45}");
            var actual = Settings.LoadFromPath(path, out var warning);
            var defaults = new HotkeyConfiguration();
            return warning is null && actual.Enabled && actual.Texture == 2 && actual.Intensity == 67 &&
                   actual.Warmth == 41 && actual.Dim == 18 && !actual.AllScreens && actual.Reminders &&
                   actual.BreakMinutes == 45 && actual.CloseToTray && actual.Hotkeys.ShowPanel == defaults.ShowPanel &&
                   actual.Hotkeys.ToggleOverlay == defaults.ToggleOverlay &&
                   actual.Hotkeys.IncreaseIntensity == defaults.IncreaseIntensity &&
                   actual.Hotkeys.DecreaseIntensity == defaults.DecreaseIntensity;
        });
    }

    private static bool TestCustomSettingsRoundtrip()
    {
        return WithTemporarySettingsPath(path =>
        {
            var expected = new Settings
            {
                Enabled = true,
                Texture = 3,
                Intensity = 67,
                Warmth = 41,
                Dim = 18,
                AllScreens = false,
                Reminders = true,
                BreakMinutes = 45,
                CloseToTray = false,
                Hotkeys = CreateAlternativeHotkeys()
            };
            if (!expected.TrySaveToPath(path, out var saveWarning) || saveWarning is not null) return false;
            var serialized = File.ReadAllText(path);
            if (serialized.Contains("DisplayText", StringComparison.Ordinal)) return false;
            var actual = Settings.LoadFromPath(path, out var loadWarning);
            return loadWarning is null && actual.Enabled == expected.Enabled && actual.Texture == expected.Texture &&
                   actual.Intensity == expected.Intensity && actual.Warmth == expected.Warmth && actual.Dim == expected.Dim &&
                   actual.AllScreens == expected.AllScreens && actual.Reminders == expected.Reminders &&
                   actual.BreakMinutes == expected.BreakMinutes && actual.CloseToTray == expected.CloseToTray &&
                   actual.Hotkeys.ShowPanel == expected.Hotkeys.ShowPanel &&
                   actual.Hotkeys.ToggleOverlay == expected.Hotkeys.ToggleOverlay &&
                   actual.Hotkeys.IncreaseIntensity == expected.Hotkeys.IncreaseIntensity &&
                   actual.Hotkeys.DecreaseIntensity == expected.Hotkeys.DecreaseIntensity;
        });
    }

    private static bool TestInvalidHotkeysPreserveLegacyValues()
    {
        return WithTemporarySettingsPath(path =>
        {
            File.WriteAllText(path,
                "{\"Enabled\":true,\"Texture\":1,\"Intensity\":77,\"Warmth\":33,\"Dim\":12,\"AllScreens\":false,\"Reminders\":true,\"BreakMinutes\":60,\"CloseToTray\":false,\"Hotkeys\":{\"ShowPanel\":{\"Modifiers\":0,\"Key\":79}}}");
            var actual = Settings.LoadFromPath(path, out var warning);
            var defaults = new HotkeyConfiguration();
            var invalidPreservesLegacy = warning?.Contains("快捷键配置无效", StringComparison.Ordinal) == true && actual.Enabled &&
                   actual.Texture == 1 && actual.Intensity == 77 && actual.Warmth == 33 && actual.Dim == 12 &&
                   !actual.AllScreens && actual.Reminders && actual.BreakMinutes == 60 && !actual.CloseToTray &&
                   actual.Hotkeys.ShowPanel == defaults.ShowPanel;

            var malformedPath = path + ".malformed";
            File.WriteAllText(malformedPath,
                "{\"Enabled\":true,\"Intensity\":66,\"CloseToTray\":false,\"Hotkeys\":{\"ShowPanel\":\"not-an-object\"}}");
            var malformed = Settings.LoadFromPath(malformedPath, out var malformedWarning);
            var malformedPreservesLegacy = malformedWarning?.Contains("快捷键配置无效", StringComparison.Ordinal) == true &&
                                           malformed.Enabled && malformed.Intensity == 66 && !malformed.CloseToTray &&
                                           malformed.Hotkeys.ShowPanel == defaults.ShowPanel;
            return invalidPreservesLegacy && malformedPreservesLegacy;
        });
    }

    private static bool TestSuccessfulRebind()
    {
        var fake = new FakeHotkeyRegistration();
        var manager = new HotkeyManager(_ => { }, new HotkeyConfiguration(), fake);
        var oldShowPanel = new ShortcutGesture(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierAlt, 0x4F);
        var candidate = new HotkeyConfiguration
        {
            ShowPanel = new ShortcutGesture(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierShift, 0x70),
            ToggleOverlay = new ShortcutGesture(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierAlt, 0x50),
            IncreaseIntensity = new ShortcutGesture(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierAlt, 0x26),
            DecreaseIntensity = new ShortcutGesture(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierAlt, 0x28)
        };
        var oldToggleId = fake.GetId(candidate.ToggleOverlay);
        var order = new List<string>();
        var applied = manager.TryApply(candidate,
            () => { order.Add("commit"); return true; },
            () => { order.Add("rollback"); return true; },
            out var error);
        var newShowPanelId = fake.GetId(candidate.ShowPanel);
        var success = applied && error is null && oldToggleId > 0 && newShowPanelId > 0 &&
                      newShowPanelId != fake.GetId(oldShowPanel) &&
                      fake.GetId(oldShowPanel) == 0 &&
                      fake.Events.IndexOf("unregister:1") > fake.Events.IndexOf("register:" + newShowPanelId) &&
                      order.SequenceEqual(new[] { "commit" });
        manager.Dispose();
        return success && fake.Active.Count == 0;
    }

    private static bool TestInternalSwap()
    {
        var pressed = new List<HotkeyAction>();
        var fake = new FakeHotkeyRegistration();
        var defaults = new HotkeyConfiguration();
        var manager = new HotkeyManager(pressed.Add, defaults, fake);
        var toggleId = fake.GetId(defaults.ToggleOverlay);
        var increaseId = fake.GetId(defaults.IncreaseIntensity);
        var registerCount = fake.RegisterCount;
        var candidate = defaults.Clone();
        candidate.ToggleOverlay = defaults.IncreaseIntensity;
        candidate.IncreaseIntensity = defaults.ToggleOverlay;
        var applied = manager.TryApply(candidate, () => true, () => true, out var error);
        manager.DispatchForTest(toggleId);
        var success = applied && error is null && fake.RegisterCount == registerCount &&
                      fake.GetId(candidate.ToggleOverlay) == increaseId && pressed.Count == 1 &&
                      pressed[0] == HotkeyAction.IncreaseIntensity;
        manager.Dispose();
        return success && fake.Active.Count == 0;
    }

    private static bool TestRegistrationConflictKeepsOldBinding()
    {
        var pressed = new List<HotkeyAction>();
        var fake = new FakeHotkeyRegistration();
        var defaults = new HotkeyConfiguration();
        var manager = new HotkeyManager(pressed.Add, defaults, fake);
        var oldIds = fake.Active.ToDictionary(pair => pair.Value, pair => pair.Key);
        var blocked = new ShortcutGesture(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierShift, 0x75);
        fake.Blocked.Add(blocked);
        var candidate = defaults.Clone();
        candidate.ShowPanel = blocked;
        var commits = 0;
        var applied = manager.TryApply(candidate, () => { commits++; return true; }, () => true, out var error);
        manager.DispatchForTest(oldIds[defaults.ShowPanel]);
        var preserved = fake.Active.Count == oldIds.Count && oldIds.All(pair => fake.Active.TryGetValue(pair.Value, out var gesture) && gesture == pair.Key);
        var success = !applied && commits == 0 && error?.Contains(blocked.DisplayText, StringComparison.Ordinal) == true &&
                      pressed.Count == 1 && pressed[0] == HotkeyAction.ShowPanel && preserved;
        manager.Dispose();
        return success && fake.Active.Count == 0;
    }

    private static bool TestPersistenceFailureKeepsOldBinding()
    {
        return WithTemporarySettingsPath(path =>
        {
            var oldSettings = new Settings { Hotkeys = new HotkeyConfiguration() };
            if (!oldSettings.TrySaveToPath(path, out var oldWarning) || oldWarning is not null) return false;
            var oldJson = File.ReadAllText(path);
            var blockedParent = Path.Combine(Path.GetDirectoryName(path)!, "blocked");
            File.WriteAllText(blockedParent, "occupied");
            var invalidPath = Path.Combine(blockedParent, "settings.json");

            var fake = new FakeHotkeyRegistration();
            var defaults = new HotkeyConfiguration();
            var manager = new HotkeyManager(_ => { }, defaults, fake);
            var oldIds = fake.Active.ToDictionary(pair => pair.Value, pair => pair.Key);
            var candidate = defaults.Clone();
            candidate.ShowPanel = new ShortcutGesture(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierShift, 0x76);
            var proposed = oldSettings.Clone();
            proposed.Hotkeys = candidate;
            string? saveWarning = null;
            var applied = manager.TryApply(candidate,
                () => proposed.TrySaveToPath(invalidPath, out saveWarning),
                () => throw new InvalidOperationException("rollback must not be needed"),
                out var error);
            var preserved = fake.Active.Count == oldIds.Count && oldIds.All(pair => fake.Active.TryGetValue(pair.Value, out var gesture) && gesture == pair.Key);
            var filePreserved = File.ReadAllText(path) == oldJson;
            manager.Dispose();
            return !applied && error is not null && error.Contains("保存", StringComparison.Ordinal) &&
                   saveWarning?.Contains("设置无法保存", StringComparison.Ordinal) == true && preserved && filePreserved && fake.Active.Count == 0;
        });
    }

    private static bool TestShowPanelDispatch()
    {
        var pressed = new List<HotkeyAction>();
        var fake = new FakeHotkeyRegistration();
        var manager = new HotkeyManager(pressed.Add, new HotkeyConfiguration(), fake);
        manager.DispatchForTest(fake.GetId(new ShortcutGesture(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierAlt, 0x4F)));
        manager.DispatchForTest(9999);
        var success = pressed.Count == 1 && pressed[0] == HotkeyAction.ShowPanel;
        manager.Dispose();
        return success && fake.Active.Count == 0;
    }

    private static bool TestDisposeReleasesRegistrations()
    {
        var fake = new FakeHotkeyRegistration();
        var manager = new HotkeyManager(_ => { }, new HotkeyConfiguration(), fake);
        if (fake.Active.Count != 4) return false;
        manager.Dispose();
        manager.Dispose();
        return fake.Active.Count == 0 && fake.UnregisterCount == 4;
    }

    private static bool TestRealWin32Conflict()
    {
        using var blocker = new NativeHotkeyWindow();
        var pool = new[]
        {
            new ShortcutGesture(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierShift, 0x70),
            new ShortcutGesture(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierShift, 0x71),
            new ShortcutGesture(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierShift, 0x72),
            new ShortcutGesture(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierShift, 0x73),
            new ShortcutGesture(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierShift, 0x74),
            new ShortcutGesture(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierShift, 0x75),
            new ShortcutGesture(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierShift, 0x76),
            new ShortcutGesture(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierShift, 0x77),
            new ShortcutGesture(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierShift, 0x78),
            new ShortcutGesture(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierShift, 0x79),
            new ShortcutGesture(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierShift, 0x7A)
        };
        var available = new List<ShortcutGesture>();
        var heldIds = new List<int>();
        var nextId = 9001;
        try
        {
            foreach (var gesture in pool)
            {
                var id = nextId++;
                if (!Native.RegisterHotKey(blocker.Handle, id, gesture.Modifiers | 0x4000, gesture.Key))
                    continue;
                available.Add(gesture);
                heldIds.Add(id);
                if (available.Count == 4) break;
                if (available.Count > 1)
                {
                    Native.UnregisterHotKey(blocker.Handle, id);
                    heldIds.RemoveAt(heldIds.Count - 1);
                }
            }

            if (available.Count < 4)
                throw new InvalidOperationException("未找到可用于真实 Win32 冲突验证的空闲组合。");

            var blocked = available[0];
            var candidate = new HotkeyConfiguration
            {
                ShowPanel = blocked,
                ToggleOverlay = available[1],
                IncreaseIntensity = available[2],
                DecreaseIntensity = available[3]
            };
            var manager = new HotkeyManager(_ => { }, candidate);
            try
            {
                return manager.FailedHotkeys.Any(value => value.Contains(blocked.DisplayText, StringComparison.Ordinal));
            }
            finally
            {
                manager.Dispose();
            }
        }
        finally
        {
            foreach (var id in heldIds)
                Native.UnregisterHotKey(blocker.Handle, id);
        }
    }

    private static HotkeyConfiguration CreateAlternativeHotkeys() => new()
    {
        ShowPanel = new ShortcutGesture(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierShift, 0x70),
        ToggleOverlay = new ShortcutGesture(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierShift, 0x71),
        IncreaseIntensity = new ShortcutGesture(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierShift, 0x72),
        DecreaseIntensity = new ShortcutGesture(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierShift, 0x73)
    };

    private static bool WithTemporarySettingsPath(Func<string, bool> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), "MoniPaper-self-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "settings.json");
        try
        {
            Directory.CreateDirectory(directory);
            return test(path);
        }
        finally
        {
            try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class FakeHotkeyRegistration : IHotkeyRegistration
    {
        public Dictionary<int, ShortcutGesture> Active { get; } = new();
        public HashSet<ShortcutGesture> Blocked { get; } = new();
        public List<string> Events { get; } = new();
        public int RegisterCount { get; private set; }
        public int UnregisterCount { get; private set; }

        public bool Register(int id, ShortcutGesture gesture)
        {
            Events.Add($"register:{id}");
            RegisterCount++;
            if (Blocked.Contains(gesture) || Active.ContainsKey(id) || Active.Values.Contains(gesture)) return false;
            Active.Add(id, gesture);
            return true;
        }

        public bool Unregister(int id)
        {
            Events.Add($"unregister:{id}");
            UnregisterCount++;
            return Active.Remove(id);
        }

        public int GetId(ShortcutGesture gesture) =>
            Active.FirstOrDefault(pair => pair.Value == gesture).Key;
    }

    private sealed class NativeHotkeyWindow : Forms.NativeWindow, IDisposable
    {
        public NativeHotkeyWindow()
        {
            CreateHandle(new Forms.CreateParams
            {
                Caption = "MoniPaper self-test hotkey blocker",
                ClassName = "STATIC"
            });
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
                DestroyHandle();
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
                actualPath = Path.Combine(Path.GetTempPath(), "MoniPaper-self-test-results.json");
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
