using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PaperCare;

/// <summary>
/// A RegisterHotKey-compatible keyboard gesture. Modifiers use the Win32
/// MOD_ALT, MOD_CONTROL and MOD_SHIFT values; the Windows key is deliberately
/// not part of this model.
/// </summary>
public sealed record ShortcutGesture(uint Modifiers, uint Key)
{
    public const uint ModifierAlt = 0x0001;
    public const uint ModifierControl = 0x0002;
    public const uint ModifierShift = 0x0004;

    private const uint VirtualKeyBackspace = 0x08;
    private const uint VirtualKeyTab = 0x09;
    private const uint VirtualKeyEnter = 0x0D;
    private const uint VirtualKeyShift = 0x10;
    private const uint VirtualKeyControl = 0x11;
    private const uint VirtualKeyAlt = 0x12;
    private const uint VirtualKeyEscape = 0x1B;
    private const uint VirtualKeySpace = 0x20;
    private const uint VirtualKeyPageUp = 0x21;
    private const uint VirtualKeyPageDown = 0x22;
    private const uint VirtualKeyEnd = 0x23;
    private const uint VirtualKeyHome = 0x24;
    private const uint VirtualKeyLeft = 0x25;
    private const uint VirtualKeyUp = 0x26;
    private const uint VirtualKeyRight = 0x27;
    private const uint VirtualKeyDown = 0x28;
    private const uint VirtualKeyInsert = 0x2D;
    private const uint VirtualKeyDelete = 0x2E;
    private const uint VirtualKeyF1 = 0x70;
    private const uint VirtualKeyF12 = 0x7B;

    [JsonIgnore]
    public string DisplayText => BuildDisplayText(Modifiers, Key);

    public static bool IsSupportedKey(uint key) =>
        key is >= 0x30 and <= 0x39 ||
        key is >= 0x41 and <= 0x5A ||
        key is >= VirtualKeyF1 and <= VirtualKeyF12 ||
        key is VirtualKeyBackspace or VirtualKeyTab or VirtualKeyEnter or VirtualKeyEscape or VirtualKeySpace or
            VirtualKeyPageUp or VirtualKeyPageDown or VirtualKeyEnd or VirtualKeyHome or
            VirtualKeyLeft or VirtualKeyUp or VirtualKeyRight or VirtualKeyDown or VirtualKeyInsert or VirtualKeyDelete;

    public static bool HasOnlySupportedModifiers(uint modifiers) =>
        (modifiers & ~(ModifierAlt | ModifierControl | ModifierShift)) == 0;

    public static bool HasSystemReservedCombination(uint modifiers, uint key)
    {
        // These shell/security combinations have a meaning outside the app.
        if ((modifiers & ModifierAlt) != 0 && key == VirtualKeyTab) return true;
        if (modifiers == ModifierAlt && key == VirtualKeyEscape) return true;
        if (modifiers == ModifierControl && key == VirtualKeyEscape) return true;
        if (modifiers == (ModifierControl | ModifierShift) && key == VirtualKeyEscape) return true;
        if (modifiers == ModifierAlt && key == 0x73) return true; // Alt + F4
        if (modifiers == ModifierAlt && key == VirtualKeySpace) return true;
        if (modifiers == (ModifierControl | ModifierAlt) && key == VirtualKeyDelete) return true;
        return key == VirtualKeyF12;
    }

    private static string BuildDisplayText(uint modifiers, uint key)
    {
        var parts = new List<string>(4);
        if ((modifiers & ModifierControl) != 0) parts.Add("Ctrl");
        if ((modifiers & ModifierAlt) != 0) parts.Add("Alt");
        if ((modifiers & ModifierShift) != 0) parts.Add("Shift");
        if ((modifiers & 0x0008) != 0) parts.Add("Win");
        parts.Add(KeyText(key));
        return string.Join(" + ", parts);
    }

    private static string KeyText(uint key) => key switch
    {
        0x30 => "0",
        0x31 => "1",
        0x32 => "2",
        0x33 => "3",
        0x34 => "4",
        0x35 => "5",
        0x36 => "6",
        0x37 => "7",
        0x38 => "8",
        0x39 => "9",
        VirtualKeyBackspace => "Backspace",
        VirtualKeyTab => "Tab",
        VirtualKeyEnter => "Enter",
        VirtualKeyEscape => "Esc",
        VirtualKeySpace => "Space",
        VirtualKeyPageUp => "Page Up",
        VirtualKeyPageDown => "Page Down",
        VirtualKeyEnd => "End",
        VirtualKeyHome => "Home",
        VirtualKeyLeft => "←",
        VirtualKeyUp => "↑",
        VirtualKeyRight => "→",
        VirtualKeyDown => "↓",
        VirtualKeyInsert => "Insert",
        VirtualKeyDelete => "Delete",
        >= 0x41 and <= 0x5A => ((char)key).ToString(),
        >= VirtualKeyF1 and <= VirtualKeyF12 => "F" + (key - VirtualKeyF1 + 1),
        VirtualKeyShift => "Shift",
        VirtualKeyControl => "Ctrl",
        VirtualKeyAlt => "Alt",
        _ => $"0x{key:X2}"
    };
}

public sealed class HotkeyConfiguration
{
    public ShortcutGesture ShowPanel { get; set; } = new(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierAlt, 0x4F);
    public ShortcutGesture ToggleOverlay { get; set; } = new(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierAlt, 0x50);
    public ShortcutGesture IncreaseIntensity { get; set; } = new(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierAlt, 0x26);
    public ShortcutGesture DecreaseIntensity { get; set; } = new(ShortcutGesture.ModifierControl | ShortcutGesture.ModifierAlt, 0x28);

    public HotkeyConfiguration Clone() => new()
    {
        ShowPanel = ShowPanel,
        ToggleOverlay = ToggleOverlay,
        IncreaseIntensity = IncreaseIntensity,
        DecreaseIntensity = DecreaseIntensity
    };

    public bool TryValidate(out string? error)
    {
        error = null;
        var seen = new Dictionary<ShortcutGesture, string>();
        var entries = new[]
        {
            (Name: "打开面板", Gesture: ShowPanel),
            (Name: "开关覆盖", Gesture: ToggleOverlay),
            (Name: "增强", Gesture: IncreaseIntensity),
            (Name: "减弱", Gesture: DecreaseIntensity)
        };

        foreach (var entry in entries)
        {
            if (entry.Gesture is null)
            {
                error = $"{entry.Name}快捷键不能为空。";
                return false;
            }

            if ((entry.Gesture.Modifiers & 0x0008) != 0)
            {
                error = $"{entry.Name}快捷键不能使用 Windows 键（{entry.Gesture.DisplayText}）。";
                return false;
            }

            if (!ShortcutGesture.HasOnlySupportedModifiers(entry.Gesture.Modifiers))
            {
                error = $"{entry.Name}快捷键含有不支持的修饰键（{entry.Gesture.DisplayText}）。";
                return false;
            }

            if (entry.Gesture.Modifiers == 0)
            {
                error = $"{entry.Name}快捷键至少需要一个 Ctrl、Alt 或 Shift 修饰键。";
                return false;
            }

            if (!ShortcutGesture.IsSupportedKey(entry.Gesture.Key))
            {
                error = $"{entry.Name}快捷键的按键无效（{entry.Gesture.DisplayText}）。";
                return false;
            }

            if (ShortcutGesture.HasSystemReservedCombination(entry.Gesture.Modifiers, entry.Gesture.Key))
            {
                error = $"{entry.Name}快捷键为系统保留组合（{entry.Gesture.DisplayText}）。";
                return false;
            }

            if (seen.TryGetValue(entry.Gesture, out var previousName))
            {
                error = $"快捷键冲突：{previousName}和{entry.Name}都使用{entry.Gesture.DisplayText}。";
                return false;
            }

            seen.Add(entry.Gesture, entry.Name);
        }

        return true;
    }
}
