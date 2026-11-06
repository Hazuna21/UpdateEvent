using System.IO;
using System.Text;

namespace HazunaV2;

// Skor "WIN = x / target". Sumber kebenarannya aplikasi ini.
// Script GTA menambahkan satu baris ("win" atau "death") ke game_events.txt tiap kejadian;
// aplikasi membacanya lalu menambah / mengurangi skor. Nilai terbaru ditulis ke game_score.txt
// ("nilai target") supaya script bisa menampilkannya.
public class GameScore
{
    public static string EventsPath => Path.Combine(SettingsStore.Folder, "game_events.txt");
    public static string ScorePath => Path.Combine(SettingsStore.Folder, "game_score.txt");

    private long _offset;

    public const int MinValue = -100000;                // skor boleh minus

    public int Value { get; private set; }
    public int Target { get; private set; } = 50;
    public int WinDelta { get; set; } = 1;
    public int DeathDelta { get; set; } = 2;

    public event Action? Changed;
    public event Action<string>? Event;     // "win" / "death", untuk log

    public GameScore(int value, int target, int winDelta, int deathDelta)
    {
        Target = Math.Clamp(target, 1, 100000);
        Value = Math.Clamp(value, MinValue, Target);
        WinDelta = Math.Clamp(winDelta, 0, 1000);
        DeathDelta = Math.Clamp(deathDelta, 0, 1000);

        // Kejadian lama sebelum aplikasi dibuka diabaikan
        try { _offset = File.Exists(EventsPath) ? new FileInfo(EventsPath).Length : 0; } catch { _offset = 0; }

        WriteScoreFile();
    }

    public void Set(int value)
    {
        int v = Math.Clamp(value, MinValue, Target);
        if (v == Value) { WriteScoreFile(); return; }
        Value = v;
        Raise();
    }

    public void Add(int delta) => Set(Value + delta);

    public void Configure(int target, int winDelta, int deathDelta)
    {
        Target = Math.Clamp(target, 1, 100000);
        WinDelta = Math.Clamp(winDelta, 0, 1000);
        DeathDelta = Math.Clamp(deathDelta, 0, 1000);
        Value = Math.Clamp(Value, MinValue, Target);
        Raise();
    }

    private void Raise()
    {
        WriteScoreFile();
        Changed?.Invoke();
    }

    private void WriteScoreFile()
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            var tmp = ScorePath + ".tmp";
            File.WriteAllText(tmp, Value + " " + Target, new UTF8Encoding(false));
            File.Move(tmp, ScorePath, true);
        }
        catch { /* gagal menulis tidak boleh mengganggu aplikasi */ }
    }

    // Dipanggil berkala: baca baris baru di game_events.txt
    public void Poll()
    {
        try
        {
            if (!File.Exists(EventsPath)) { _offset = 0; return; }

            using var fs = new FileStream(EventsPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length < _offset) _offset = 0;          // file dikosongkan
            if (fs.Length == _offset) return;

            fs.Seek(_offset, SeekOrigin.Begin);
            var buf = new byte[fs.Length - _offset];
            int read = fs.Read(buf, 0, buf.Length);
            string text = Encoding.UTF8.GetString(buf, 0, read);

            int lastNl = text.LastIndexOf('\n');
            if (lastNl < 0) return;                         // baris belum lengkap
            _offset += Encoding.UTF8.GetByteCount(text.AsSpan(0, lastNl + 1));

            foreach (var raw in text[..lastNl].Split('\n'))
            {
                string kind = raw.Trim().ToLowerInvariant();
                if (kind == "win") { Event?.Invoke("win"); Add(WinDelta); }
                else if (kind == "death") { Event?.Invoke("death"); Add(-DeathDelta); }
            }
        }
        catch { /* dicoba lagi pada putaran berikutnya */ }
    }
}
