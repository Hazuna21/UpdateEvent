using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace HazunaV2;

public class KnownGift
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public long Diamonds { get; set; }
    public int TimesSeen { get; set; }
    public DateTime LastSeen { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ImageUrl { get; set; }

    [JsonIgnore]
    public string Detail
    {
        get
        {
            var parts = new List<string> { Id != 0 ? $"ID {Id}" : "ditambah manual (belum ada ID)" };
            if (TimesSeen > 0) parts.Add($"terlihat {TimesSeen}x");
            return string.Join("  ·  ", parts);
        }
    }
}

public static class GiftCatalog
{
    private static readonly string FilePath = Path.Combine(SettingsStore.Folder, "gifts.json");
    private static bool _dirty;
    private static DispatcherTimer? _saveTimer;

    // Alasan kalau file gagal dibaca (kosong kalau semuanya baik-baik saja)
    public static string? LoadError { get; private set; }

    public static ObservableCollection<KnownGift> Items { get; } = Load();

    static GiftCatalog()
    {
        // Pengaman terakhir: simpan saat proses berakhir
        AppDomain.CurrentDomain.ProcessExit += (s, e) => Flush();
    }

    private static ObservableCollection<KnownGift> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var list = JsonSerializer.Deserialize<List<KnownGift>>(json);
                if (list != null) return new ObservableCollection<KnownGift>(list);
            }
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
        }
        return new ObservableCollection<KnownGift>();
    }

    // Menyimpan hanya kalau ada perubahan
    public static void Flush()
    {
        if (!_dirty) return;
        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            var json = JsonSerializer.Serialize(Items, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
            _dirty = false;
        }
        catch
        {
            // coba lagi pada penyimpanan berikutnya
        }
    }

    // Menandai ada perubahan; disimpan otomatis beberapa detik kemudian
    private static void MarkDirty()
    {
        _dirty = true;

        if (_saveTimer == null)
        {
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _saveTimer.Tick += (s, e) =>
            {
                _saveTimer!.Stop();
                Flush();
            };
        }
        if (!_saveTimer.IsEnabled)
            _saveTimer.Start();
    }

    public static KnownGift? FindById(long id) =>
        id == 0 ? null : Items.FirstOrDefault(i => i.Id == id);

    public static KnownGift? FindByName(string name) =>
        Items.FirstOrDefault(i => SameName(i.Name, name));

    // Dipanggil setiap ada gift asli masuk: belajar dari LIVE
    public static void Observe(LiveGift g)
    {
        if (string.IsNullOrWhiteSpace(g.GiftName)) return;

        var item = FindById(g.GiftId)
                   ?? Items.FirstOrDefault(i => SameName(i.Name, g.GiftName));

        if (item == null)
        {
            item = new KnownGift { Id = g.GiftId };
            Items.Add(item);
        }
        else if (g.GiftId != 0 && item.Id != g.GiftId)
        {
            item.Id = g.GiftId; // ID dari LIVE dianggap paling akurat
        }

        item.Name = g.GiftName.Trim();
        if (g.DiamondsEach > 0) item.Diamonds = g.DiamondsEach;
        if (!string.IsNullOrWhiteSpace(g.ImageUrl)) item.ImageUrl = g.ImageUrl;
        item.TimesSeen++;
        item.LastSeen = DateTime.Now;
        MarkDirty();
    }

    // Memuat katalog dari pustaka TikTok; langsung disimpan ke file
    public static (int Added, int Updated) ImportCatalog(IEnumerable<LiveGiftInfo> gifts)
    {
        int added = 0, updated = 0;

        foreach (var g in gifts)
        {
            if (string.IsNullOrWhiteSpace(g.Name)) continue;
            var name = g.Name.Trim();

            var item = FindById(g.Id)
                       ?? Items.FirstOrDefault(i => i.Id == 0 && SameName(i.Name, name));

            if (item == null)
            {
                Items.Add(new KnownGift
                {
                    Id = g.Id,
                    Name = name,
                    Diamonds = g.Diamonds,
                    ImageUrl = string.IsNullOrWhiteSpace(g.ImageUrl) ? null : g.ImageUrl
                });
                added++;
                continue;
            }

            bool changed = false;
            if (item.Id == 0 && g.Id != 0) { item.Id = g.Id; changed = true; }
            if (!string.Equals(item.Name, name, StringComparison.Ordinal)) { item.Name = name; changed = true; }
            if (g.Diamonds > 0 && item.Diamonds != g.Diamonds) { item.Diamonds = g.Diamonds; changed = true; }
            if (!string.IsNullOrWhiteSpace(g.ImageUrl) && item.ImageUrl != g.ImageUrl)
            {
                item.ImageUrl = g.ImageUrl;
                changed = true;
            }
            if (changed) updated++;
        }

        if (added + updated > 0)
        {
            _dirty = true;
            Flush(); // langsung simpan, tidak menunggu timer
        }
        return (added, updated);
    }

    public static KnownGift AddManual(string name, long diamonds)
    {
        name = name.Trim();
        var existing = FindByName(name);
        if (existing != null)
        {
            if (diamonds > 0) existing.Diamonds = diamonds;
            MarkDirty();
            return existing;
        }

        var item = new KnownGift { Name = name, Diamonds = diamonds };
        Items.Add(item);
        MarkDirty();
        return item;
    }

    private static bool SameName(string a, string b) =>
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}