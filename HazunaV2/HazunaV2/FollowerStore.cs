using System.IO;
using System.Text.Json;

namespace HazunaV2;

// Daftar penonton yang diketahui sudah follow (dari event follow selama aplikasi terhubung). Disimpan di followers.json.
public static class FollowerStore
{
    private static readonly object Gate = new();
    private static HashSet<string>? _set;
    private static string FilePath => Path.Combine(SettingsStore.Folder, "followers.json");

    private static HashSet<string> Load()
    {
        if (_set != null) return _set;
        _set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(FilePath))
            {
                var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath));
                if (list != null) foreach (var n in list) _set.Add(n);
            }
        }
        catch { }
        return _set;
    }

    public static bool Contains(params string?[] names)
    {
        lock (Gate)
        {
            var s = Load();
            return names.Any(n => !string.IsNullOrWhiteSpace(n) && s.Contains(n.Trim()));
        }
    }

    public static void Add(params string?[] names)
    {
        lock (Gate)
        {
            var s = Load();
            bool changed = false;
            foreach (var n in names)
                if (!string.IsNullOrWhiteSpace(n) && n != "(tanpa nama)" && s.Add(n.Trim())) changed = true;
            if (!changed) return;
            try
            {
                Directory.CreateDirectory(SettingsStore.Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(s.ToList()));
            }
            catch { }
        }
    }
}
