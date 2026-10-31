using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace HazunaV2;

public static class RuleStore
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HazunaLiveControl");
    private static readonly string FilePath = Path.Combine(Folder, "giftrules.json");

    public static ObservableCollection<GiftRule> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var list = JsonSerializer.Deserialize<List<GiftRule>>(json);
                if (list != null) return new ObservableCollection<GiftRule>(list);
            }
        }
        catch { /* file rusak: mulai dari daftar kosong */ }
        return new ObservableCollection<GiftRule>();
    }

    public static void Save(IEnumerable<GiftRule> rules)
    {
        Directory.CreateDirectory(Folder);
        var json = JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}