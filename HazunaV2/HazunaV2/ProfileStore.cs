using System.IO;

namespace HazunaV2;

// Profil game: tiap profil menyimpan salinan data yang khusus per game
// (aturan gift, pustaka media, roda, soundboard). Pengaturan umum (akun, TTS, AI, tema) tidak ikut.
public static class ProfileStore
{
    public const string DefaultName = "Utama";

    public static readonly string[] Files = { "giftrules.json", "library.json", "wheel.json", "soundboard.json" };

    private static string Root => Path.Combine(SettingsStore.Folder, "profiles");
    public static string Dir(string name) => Path.Combine(Root, name);

    public static string Sanitize(string? name)
    {
        var t = new string((name ?? "").Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_').ToArray()).Trim();
        return t.Length > 40 ? t[..40].Trim() : t;
    }

    public static List<string> List()
    {
        var names = new List<string>();
        try
        {
            if (Directory.Exists(Root))
                names.AddRange(Directory.GetDirectories(Root).Select(d => Path.GetFileName(d)!));
        }
        catch { }
        return names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static bool Exists(string name) => Directory.Exists(Dir(name));

    // Salin data yang sedang dipakai ke folder profil (menimpa isi lama profil itu)
    public static void Snapshot(string name)
    {
        var dir = Dir(name);
        Directory.CreateDirectory(dir);
        foreach (var f in Files)
        {
            var src = Path.Combine(SettingsStore.Folder, f);
            var dst = Path.Combine(dir, f);
            if (File.Exists(src)) File.Copy(src, dst, true);
            else if (File.Exists(dst)) File.Delete(dst);
        }
    }

    // Pasang isi profil sebagai data yang dipakai (file yang tidak ada di profil dikosongkan)
    public static void Activate(string name)
    {
        var dir = Dir(name);
        foreach (var f in Files)
        {
            var src = Path.Combine(dir, f);
            var dst = Path.Combine(SettingsStore.Folder, f);
            if (File.Exists(src)) File.Copy(src, dst, true);
            else if (File.Exists(dst)) File.Delete(dst);
        }
    }

    public static void CreateEmpty(string name) => Directory.CreateDirectory(Dir(name));

    public static void Rename(string oldName, string newName) => Directory.Move(Dir(oldName), Dir(newName));

    public static void Delete(string name) => Directory.Delete(Dir(name), true);
}
