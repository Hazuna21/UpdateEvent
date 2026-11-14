using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HazunaV2;

public class MediaItem : INotifyPropertyChanged
{
    private string _filePath = "";
    private string _name = "";
    private string _kind = "";

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string FilePath
    {
        get => _filePath;
        set
        {
            _filePath = value;
            OnChanged();
            OnChanged(nameof(FileName));
            OnChanged(nameof(Format));
            OnChanged(nameof(Status));
            OnChanged(nameof(DisplayName));
        }
    }

    public string Name
    {
        get => _name;
        set { _name = value; OnChanged(); OnChanged(nameof(DisplayName)); }
    }

    // "Suara" atau "Media"
    public string Kind
    {
        get => _kind;
        set { _kind = value; OnChanged(); }
    }

    [JsonIgnore]
    public string DisplayName =>
        string.IsNullOrWhiteSpace(_name) ? Path.GetFileNameWithoutExtension(_filePath) : _name;

    [JsonIgnore]
    public string FileName => Path.GetFileName(_filePath);

    [JsonIgnore]
    public string Format => Path.GetExtension(_filePath).TrimStart('.').ToUpperInvariant();

    [JsonIgnore]
    public string Status => File.Exists(_filePath) ? "Ada" : "Hilang";

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

public static class LibraryStore
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HazunaLiveControl");
    private static readonly string FilePath = Path.Combine(Folder, "library.json");

    public static ObservableCollection<MediaItem> Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var list = JsonSerializer.Deserialize<List<MediaItem>>(json);
                if (list != null) return new ObservableCollection<MediaItem>(list);
            }
        }
        catch { /* file rusak: mulai dari daftar kosong */ }
        return new ObservableCollection<MediaItem>();
    }

    public static void Save(IEnumerable<MediaItem> items)
    {
        Directory.CreateDirectory(Folder);
        var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}

// Pustaka bersama yang dipakai Gift Rules dan halaman Media & Sounds
public static class AppData
{
    public const string KindSound = "Suara";
    public const string KindMedia = "Media";

    private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".wma", ".m4a", ".aac" };

    public static ObservableCollection<MediaItem> Library { get; } = LoadLibrary();

    // Dipanggil saat isi pustaka berubah (tambah, hapus, urutan, nama, jenis, lokasi)
    public static event Action? LibraryChanged;

    private static ObservableCollection<MediaItem> LoadLibrary()
    {
        var list = LibraryStore.Load();
        foreach (var item in list)
            item.PropertyChanged += OnItemChanged;

        list.CollectionChanged += (s, e) =>
        {
            if (e.OldItems != null)
                foreach (MediaItem i in e.OldItems) i.PropertyChanged -= OnItemChanged;
            if (e.NewItems != null)
                foreach (MediaItem i in e.NewItems) i.PropertyChanged += OnItemChanged;
            LibraryChanged?.Invoke();
        };
        return list;
    }

    private static void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is "Name" or "Kind" or "FilePath")
            LibraryChanged?.Invoke();
    }

    public static MediaItem? Find(string? id) =>
        string.IsNullOrEmpty(id) ? null : Library.FirstOrDefault(i => i.Id == id);

    public static MediaItem FindOrAdd(string path, string kind)
    {
        var existing = Library.FirstOrDefault(i =>
            i.Kind == kind && string.Equals(i.FilePath, path, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;

        var item = new MediaItem
        {
            FilePath = path,
            Kind = kind,
            Name = Path.GetFileNameWithoutExtension(path)
        };
        Library.Add(item);
        return item;
    }

    public static string GuessKind(string path) =>
        AudioExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()) ? KindSound : KindMedia;

    public static string DialogFilter(string kind) =>
        kind == KindSound
            ? "File audio|*.mp3;*.wav;*.wma;*.m4a;*.aac|Semua file|*.*"
            : "GIF dan video|*.gif;*.mp4;*.wmv;*.avi;*.mov|Semua file|*.*";
}