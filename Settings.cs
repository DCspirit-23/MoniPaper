using System;
using System.IO;
using System.Text.Json;

namespace PaperCare;

public sealed class Settings
{
    public static readonly int[] BreakOptions = { 20, 30, 45, 60 };

    public bool Enabled { get; set; }
    public int Texture { get; set; }
    public int Intensity { get; set; } = 30;
    public int Warmth { get; set; } = 10;
    public int Dim { get; set; }
    public bool AllScreens { get; set; } = true;
    public bool Reminders { get; set; }
    public int BreakMinutes { get; set; } = 20;

    public void Normalize()
    {
        Texture = Math.Clamp(Texture, 0, 3);
        Intensity = Math.Clamp(Intensity, 0, 100);
        Warmth = Math.Clamp(Warmth, 0, 100);
        Dim = Math.Clamp(Dim, 0, 50);
        if (Array.IndexOf(BreakOptions, BreakMinutes) < 0)
            BreakMinutes = 20;
    }

    public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PaperCare");
    public static string FilePath => Path.Combine(Folder, "settings.json");

    public static Settings Load(out string? warning)
    {
        warning = null;
        try
        {
            var value = File.Exists(FilePath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new() : new();
            value.Normalize();
            return value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            warning = "原设置无法读取，本次使用默认设置。";
            return new();
        }
    }

    public bool TrySave(out string? warning)
    {
        warning = null;
        var temp = FilePath + ".tmp";
        try
        {
            Normalize();
            Directory.CreateDirectory(Folder);
            File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            warning = "设置无法保存，请检查 PaperCare 文件夹的写入权限。";
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
