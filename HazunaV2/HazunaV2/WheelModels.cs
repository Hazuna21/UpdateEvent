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

    // Tambah (+) atau kurangi (-) skor WIN saat isi ini menang; 0 = tidak mengubah skor
    private int _scoreDelta;
    public int ScoreDelta { get => _scoreDelta; set => _scoreDelta = Math.Clamp(value, -1000, 1000); }

    [JsonIgnore] public string KeyDisplay => string.IsNullOrWhiteSpace(Key) ? "—" : Key;
}

public class WheelConfig : System.ComponentModel.INotifyPropertyChanged
{
    private string _name = "Lucky Wheel";

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

    public string Name
    {
        get => _name;
        set
        {
            _name = string.IsNullOrWhiteSpace(value) ? "Lucky Wheel" : value.Trim();
            PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Name)));
        }
    }

    public long GiftId { get; set; }
    public string GiftName { get; set; } = "";
    public bool SpinPerGift { get; set; } = true;      // jumlah gift = jumlah putaran (maks 10)
    public double SpinSeconds { get; set; } = 6;       // lama roda berputar
    public double ActionSeconds { get; set; } = 2;     // lama tombol hasil dijalankan
    public bool ShuffleEachSpin { get; set; }          // acak posisi isi di roda sebelum tiap putaran
    public bool Standby { get; set; } = true;           // jendela selalu ada (untuk TikTok Studio), isinya tak terlihat sampai ada gift
    public bool AlwaysShow { get; set; }               // false = roda disembunyikan, muncul hanya saat ada gift
    public bool AutoOpenOverlay { get; set; }          // lama (satu roda); dipakai hanya untuk memindahkan data lama
    public double? PosX { get; set; }                  // posisi terakhir overlay
    public double? PosY { get; set; }
    public ObservableCollection<WheelEntry> Entries { get; set; } = new();

    [JsonIgnore]
    public string GiftDisplay => string.IsNullOrWhiteSpace(GiftName) ? "(belum dipilih)" : GiftName;

    public static WheelConfig CreateDefault(string name)
    {
        var w = new WheelConfig { Name = name };
        foreach (var t in new[] { "Hadiah 1", "Hadiah 2", "Hadiah 3", "Coba lagi" })
            w.Entries.Add(new WheelEntry { Text = t });
        return w;
    }
}

public class WheelSet
{
    public ObservableCollection<WheelConfig> Wheels { get; set; } = new();
    public double? PosX { get; set; }                  // posisi satu jendela overlay untuk semua roda
    public double? PosY { get; set; }
}

public static class WheelStore
{
    private static readonly string FilePath = Path.Combine(SettingsStore.Folder, "wheel.json");

    private static void Normalize(WheelConfig w)
    {
        w.SpinSeconds = Math.Clamp(w.SpinSeconds, 2, 30);
        w.ActionSeconds = Math.Clamp(w.ActionSeconds, 0.2, 60);
    }

    public static WheelSet Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                string json = File.ReadAllText(FilePath);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("Wheels", out _))
                {
                    var set = JsonSerializer.Deserialize<WheelSet>(json);
                    if (set != null && set.Wheels.Count > 0)
                    {
                        foreach (var w in set.Wheels) Normalize(w);
                        return set;
                    }
                }
                else
                {
                    // Format lama: satu roda saja
                    var old = JsonSerializer.Deserialize<WheelConfig>(json);
                    if (old != null)
                    {
                        old.Name = "Lucky Wheel";
                        old.AlwaysShow = old.AutoOpenOverlay;
                        Normalize(old);
                        var migrated = new WheelSet();
                        migrated.Wheels.Add(old);
                        return migrated;
                    }
                }
            }
        }
        catch { /* file rusak: mulai dari isi bawaan */ }

        var def = new WheelSet();
        def.Wheels.Add(WheelConfig.CreateDefault("Lucky Wheel"));
        return def;
    }

    public static void Save(WheelSet set)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(set, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* gagal menyimpan tidak boleh mengganggu aplikasi */ }
    }
}
