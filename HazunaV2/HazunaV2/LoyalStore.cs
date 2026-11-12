using System.IO;
using System.Text.Json;

namespace HazunaV2;

public class LoyalViewer
{
    public string Name { get; set; } = "";
    public int Visits { get; set; }            // jumlah LIVE berbeda yang diikuti
    public long Diamonds { get; set; }         // total diamond dari semua LIVE
    public DateTime LastSeen { get; set; }
}

// Catatan penonton lintas LIVE (disimpan di loyal.json) untuk mengenali penonton setia.
public class LoyalStore
{
    private static string FilePath => Path.Combine(SettingsStore.Folder, "loyal.json");

    private readonly Dictionary<string, LoyalViewer> _data = new(StringComparer.OrdinalIgnoreCase);
    private bool _dirty;

    public static LoyalStore Load()
    {
        var store = new LoyalStore();
        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<LoyalViewer>>(File.ReadAllText(FilePath));
                if (list != null)
                    foreach (var v in list.Where(v => !string.IsNullOrWhiteSpace(v.Name)))
                        store._data[v.Name] = v;
            }
        }
        catch { /* file rusak: mulai dari kosong */ }
        return store;
    }

    public LoyalViewer Get(string name)
    {
        if (!_data.TryGetValue(name, out var v))
            _data[name] = v = new LoyalViewer { Name = name };
        return v;
    }

    public void MarkDirty() => _dirty = true;

    public void Save()
    {
        if (!_dirty) return;
        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            // Simpan paling banyak 5000 penonton terakhir terlihat supaya file tidak membengkak
            var list = _data.Values.OrderByDescending(v => v.LastSeen).Take(5000).ToList();
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(list));
            File.Move(tmp, FilePath, true);
            _dirty = false;
        }
        catch { /* dicoba lagi nanti */ }
    }
}
