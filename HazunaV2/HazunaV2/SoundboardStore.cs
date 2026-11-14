using System.IO;
using System.Text.Json;

namespace HazunaV2;

// Tombol soundboard buatan sendiri (terpisah dari pustaka Media & Sounds).
public class SoundboardItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string Hotkey { get; set; } = "";
    public string Color { get; set; } = "AccentBrush";
    public int Volume { get; set; } = 100;   // 1-200, dikalikan dengan volume umum soundboard
}

public static class SoundboardStore
{
    private static string FilePath => Path.Combine(SettingsStore.Folder, "soundboard.json");

    public static List<SoundboardItem> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<List<SoundboardItem>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new();
    }

    public static void Save(IEnumerable<SoundboardItem> items)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
