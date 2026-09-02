using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace PaperCare;

public sealed class Settings
{
    public static readonly int[] BreakOptions = { 20, 30, 45, 60 };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public bool Enabled { get; set; }
    public int Texture { get; set; }
    public int Intensity { get; set; } = 30;
    public int Warmth { get; set; } = 10;
    public int Dim { get; set; }
    public bool AllScreens { get; set; } = true;
    public bool Reminders { get; set; }
    public int BreakMinutes { get; set; } = 20;
    public HotkeyConfiguration Hotkeys { get; set; } = new();
    public bool CloseToTray { get; set; } = true;

    public void Normalize()
    {
        Texture = Math.Clamp(Texture, 0, 3);
        Intensity = Math.Clamp(Intensity, 0, 100);
        Warmth = Math.Clamp(Warmth, 0, 100);
        Dim = Math.Clamp(Dim, 0, 50);
        if (Array.IndexOf(BreakOptions, BreakMinutes) < 0)
            BreakMinutes = 20;
        if ((object?)Hotkeys is null)
            Hotkeys = new HotkeyConfiguration();
    }

    public Settings Clone() => new()
    {
        Enabled = Enabled,
        Texture = Texture,
        Intensity = Intensity,
        Warmth = Warmth,
        Dim = Dim,
        AllScreens = AllScreens,
        Reminders = Reminders,
        BreakMinutes = BreakMinutes,
        Hotkeys = Hotkeys?.Clone() ?? new HotkeyConfiguration(),
        CloseToTray = CloseToTray
    };

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaperCare");
    public static string FilePath => Path.Combine(Folder, "settings.json");

    public static Settings Load(out string? warning)
        => LoadFromPath(FilePath, out warning);

    internal static Settings LoadFromPath(string path, out string? warning)
    {
        warning = null;
        try
        {
            if (!File.Exists(path)) return new Settings();

            var json = File.ReadAllText(path);
            if (JsonNode.Parse(json) is not JsonObject document)
            {
                warning = "设置文件格式无效，本次使用默认设置。";
                return new Settings();
            }

            JsonNode? hotkeyNode = null;
            string? hotkeyPropertyName = document
                .Select(pair => pair.Key)
                .FirstOrDefault(key => string.Equals(key, nameof(Hotkeys), StringComparison.OrdinalIgnoreCase));
            var hotkeysPresent = hotkeyPropertyName is not null;
            if (hotkeysPresent)
            {
                hotkeyNode = document[hotkeyPropertyName!]?.DeepClone();
                document.Remove(hotkeyPropertyName!);
            }

            // Deserialize the established settings independently of Hotkeys so
            // one malformed new field cannot discard older user preferences.
            var value = JsonSerializer.Deserialize<Settings>(document.ToJsonString(), JsonOptions) ?? new Settings();
            value.Normalize();

            if (!hotkeysPresent) return value;

            HotkeyConfiguration? hotkeys = null;
            try
            {
                hotkeys = hotkeyNode is null ? null : hotkeyNode.Deserialize<HotkeyConfiguration>(JsonOptions);
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException)
            {
                // The warning below distinguishes an invalid hotkey field from
                // a failure to read the entire settings document.
            }

            string? hotkeyError = null;
            var hotkeysValid = hotkeys is not null && hotkeys.TryValidate(out hotkeyError);
            if (!hotkeysValid)
            {
                value.Hotkeys = new HotkeyConfiguration();
                warning = FormatHotkeyWarning(hotkeyError);
            }
            else
            {
                value.Hotkeys = hotkeys!;
            }

            return value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            warning = "设置文件无法读取，本次使用默认设置。";
            return new Settings();
        }
    }

    public bool TrySave(out string? warning)
        => TrySaveToPath(FilePath, out warning);

    internal bool TrySaveToPath(string path, out string? warning)
    {
        warning = null;
        var temp = path + ".tmp";
        try
        {
            Normalize();
            if (!Hotkeys.TryValidate(out var hotkeyError))
            {
                warning = FormatHotkeySaveWarning(hotkeyError);
                return false;
            }

            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
                throw new IOException("配置目录无效。");
            temp = path + ".tmp";
            Directory.CreateDirectory(directory);
            File.WriteAllText(temp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(temp, path, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            warning = "设置无法保存，请检查配置目录的写入权限。";
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException)
            {
                // The original settings remain intact when the temporary file cannot be removed.
            }
            return false;
        }
    }

    private static string FormatHotkeyWarning(string? hotkeyError) =>
        string.IsNullOrWhiteSpace(hotkeyError)
            ? "快捷键配置无效，已使用默认快捷键。"
            : $"快捷键配置无效（{hotkeyError}），已使用默认快捷键。";

    private static string FormatHotkeySaveWarning(string? hotkeyError) =>
        string.IsNullOrWhiteSpace(hotkeyError)
            ? "快捷键配置无效，设置未保存。"
            : $"快捷键配置无效（{hotkeyError}），设置未保存。";

    public void Save()
    {
        if (!TrySave(out var warning))
            throw new IOException(warning);
    }
}

public sealed class PauseState
{
    public DateTimeOffset? Until { get; private set; }
    public bool IsPaused(DateTimeOffset now) => Until is { } end && now < end;
    public void Pause(DateTimeOffset now) => Until = now.AddMinutes(10);
    public void Resume() => Until = null;
}
