using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HazunaV2;

// Satu isi roda: teks yang tampil, bobot (peluang relatif), dan tombol opsional yang ditekan saat terpilih
public class WheelEntry
{
    private int _weight = 1;

    public string Text { get; set; } = "";

    public int Weight { get => _weight; set => _weight = Math.Clamp(value, 1, 1000); }

    public string Key { get; set; } = "";

    [JsonIgnore] public string KeyDisplay => string.IsNullOrWhiteSpace(Key) ? "—" : Key;
}

public class WheelConfig
{
    public long GiftId { get; set; }
    public string GiftName { get; set; } = "";
    public bool SpinPerGift { get; set; } = true;      // jumlah gift = jumlah putaran (maks 10)
    public double SpinSeconds { get; set; } = 6;       // lama roda berputar
    public double ActionSeconds { get; set; } = 2;     // lama tombol hasil dijalankan
    public bool AutoOpenOverlay { get; set; }          // buka overlay roda saat aplikasi dibuka
    public ObservableCollection<WheelEntry> Entries { get; set; } = new();

    [JsonIgnore]
    public string GiftDisplay => string.IsNullOrWhiteSpace(GiftName) ? "(belum dipilih)" : GiftName;
}

public static class WheelStore
{
    private static readonly string FilePath = Path.Combine(SettingsStore.Folder, "wheel.json");

    public static WheelConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var cfg = JsonSerializer.Deserialize<WheelConfig>(File.ReadAllText(FilePath));
                if (cfg != null)
                {
                    cfg.SpinSeconds = Math.Clamp(cfg.SpinSeconds, 2, 30);
                    cfg.ActionSeconds = Math.Clamp(cfg.ActionSeconds, 0.2, 60);
                    return cfg;
                }
            }
        }
        catch { /* file rusak: mulai dari isi bawaan */ }

        var def = new WheelConfig();
        foreach (var t in new[] { "Hadiah 1", "Hadiah 2", "Hadiah 3", "Coba lagi" })
            def.Entries.Add(new WheelEntry { Text = t });
        return def;
    }

    public static void Save(WheelConfig cfg)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(cfg, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* gagal menyimpan tidak boleh mengganggu aplikasi */ }
    }
}
