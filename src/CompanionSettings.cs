using System.Security.Cryptography;
using System.Text.Json;

namespace Pulse.Remote;

public sealed class CompanionSettings
{
    public string HostName { get; set; } = "";
    public bool StartHwInfo { get; set; } = true;
    public int RefreshSeconds { get; set; } = 2;
    public string? HwInfoPath { get; set; }
    private static string FilePath => Path.Combine(Program.DataDirectory, "companion.json");
    public static CompanionSettings Load()
    {
        CompanionSettings settings;
        try { settings = JsonSerializer.Deserialize<CompanionSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or JsonException) { settings = new(); }
        if (!System.Text.RegularExpressions.Regex.IsMatch(settings.HostName, @"^pulse-[a-f0-9]{8}\.local$"))
            settings.HostName = "pulse-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant() + ".local";
        if (settings.RefreshSeconds is not (1 or 2 or 5 or 10)) settings.RefreshSeconds = 2;
        settings.Save();
        return settings;
    }
    public void Save()
    {
        lock (this)
        {
        Directory.CreateDirectory(Program.DataDirectory);
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, FilePath, true);
        }
    }
}
