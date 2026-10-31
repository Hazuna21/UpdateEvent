using System.IO;
using System.Text;
using System.Text.Json;

namespace HazunaV2;

// Menulis nama pengirim ke file sebelum tombol ditekan, supaya mod/script di game
// bisa membaca siapa yang memicu spawn lalu memasang namanya ke zombie.
//
// File (di folder %AppData%\HazunaLiveControl):
//   spawn_name.txt   -> hanya nama (satu baris), paling mudah dibaca script
//   spawn_name.json  -> {"name":"budi","seq":12,"time":"...","rule":"Gift","count":3,"index":1}
// "seq" naik setiap giliran, jadi mod bisa tahu ada pemicu baru walau namanya sama.
public static class SpawnName
{
    private static readonly string Dir = SettingsStore.Folder;
    private static long _seq;

    public static string TxtPath => Path.Combine(Dir, "spawn_name.txt");
    public static string JsonPath => Path.Combine(Dir, "spawn_name.json");

    public static void Write(QueuedAction a)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string name = Clean(a.UserName);
            long seq = Interlocked.Increment(ref _seq);

            var json = JsonSerializer.Serialize(new
            {
                name,
                seq,
                time = DateTime.UtcNow.ToString("o"),
                rule = a.GiftName,
                index = a.Index,
                count = a.Total
            });

            Atomic(TxtPath, name);
            Atomic(JsonPath, json);
        }
        catch
        {
            // gagal menulis tidak boleh menghentikan aksi tombol
        }
    }

    private static string Clean(string s)
    {
        var t = (s ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return t.Length > 40 ? t[..40] : t;
    }

    private static void Atomic(string path, string content)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, content, new UTF8Encoding(false));
        File.Move(tmp, path, true);
    }
}
