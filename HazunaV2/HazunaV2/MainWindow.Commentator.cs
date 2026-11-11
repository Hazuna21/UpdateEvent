using System.Windows;

namespace HazunaV2;

public enum GameEventLevel { Low, Medium, High }

public partial class MainWindow
{
    private readonly List<(DateTime At, string Text)> _gameEvents = new();
    private DateTime _lastReact = DateTime.MinValue;
    private DateTime _lastActivity = DateTime.UtcNow;
    private readonly List<string> _recentAiSays = new();
    private readonly System.Windows.Threading.DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private long _likeTotal, _likeNoted;

    // Dipanggil sekali saat halaman AI siap: AI ikut bicara sendiri kalau stream sedang sepi
    private void StartIdleChatter()
    {
        _idleTimer.Tick += (s, e) => IdleTick();
        _idleTimer.Start();
    }

    private void IdleTick()
    {
        double cooldown = Math.Max(3, _settings.AiReactCooldown);
        double quiet = Math.Max(20, cooldown * 2);
        var now = DateTime.UtcNow;
        if ((now - _lastActivity).TotalSeconds < quiet || (now - _lastReact).TotalSeconds < quiet) return;

        // Hanya kalau LIVE tersambung (atau sedang ada riwayat kejadian), supaya tidak bicara sendiri tanpa penonton
        if (!_connector.IsRunning && _gameEvents.Count == 0) return;

        TryReact("Suasana sedang sepi, belum ada kejadian baru beberapa saat ini", GameEventLevel.Medium, quiet: true);
    }

    private void NoteSocial(RuleTrigger kind, string user, int count)
    {
        switch (kind)
        {
            case RuleTrigger.Follow:
                NoteGameEvent($"{user} follow streamer", GameEventLevel.Medium);
                break;
            case RuleTrigger.Share:
                NoteGameEvent($"{user} membagikan LIVE", GameEventLevel.Medium);
                break;
            case RuleTrigger.Like:
                _likeTotal += Math.Max(1, count);
                if (_likeTotal - _likeNoted >= 100)
                {
                    _likeNoted = _likeTotal;
                    NoteGameEvent($"Like terus mengalir, total sudah {_likeTotal:N0} like (terakhir dari {user})", GameEventLevel.Medium);
                }
                break;
        }
    }

    // Komentar penonton jadi bahan konteks saja (jawabannya diurus alur AI Reply biasa)
    private void NoteChat(LiveChat c)
    {
        if (string.Equals(c.UserName, _settings.TikTokUsername, StringComparison.OrdinalIgnoreCase)) return;
        string t = (c.Text ?? "").Trim();
        if (t.Length < 2) return;
        if (t.Length > 80) t = t[..80];
        NoteGameEvent($"{c.UserName} berkomentar: {t}", GameEventLevel.Low);
    }

    // Catat sesuatu yang terjadi di stream/game. Semua kejadian jadi bahan konteks AI;
    // yang penting juga dipancing menjadi reaksi spontan (kalau diaktifkan).
    private void NoteGameEvent(string text, GameEventLevel level)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => NoteGameEvent(text, level));
            return;
        }
        text = CleanEventText(text);
        if (text.Length == 0) return;

        _gameEvents.Add((DateTime.UtcNow, text));
        while (_gameEvents.Count > 40) _gameEvents.RemoveAt(0);
        if (level != GameEventLevel.Low) _lastActivity = DateTime.UtcNow;

        TryReact(text, level);
    }

    // Nama penonton berasal dari luar: buang karakter yang bisa merusak pembungkus data
    private static string CleanEventText(string s)
    {
        s = (s ?? "").Replace('\r', ' ').Replace('\n', ' ').Replace("<", "").Replace(">", "").Replace("\"", "'").Trim();
        return s.Length > 160 ? s[..160] : s;
    }

    private string BuildGameContext()
    {
        var now = DateTime.UtcNow;
        var recent = _gameEvents.Where(e => (now - e.At).TotalSeconds <= 180).TakeLast(12).ToList();
        if (recent.Count == 0) return "";

        var lines = recent.Select(e => $"- {(int)(now - e.At).TotalSeconds} dtk lalu: {e.Text}").ToList();
        lines.Add($"- Skor WIN saat ini: {_score.Value} / {_score.Target}");
        return string.Join("\n", lines);
    }

    private void TryReact(string text, GameEventLevel level, bool quiet = false)
    {
        if (level == GameEventLevel.Low) return;
        if (_aiQueue == null || !_aiReady || AiEnableCheck.IsChecked != true || AiReactCheck.IsChecked != true) return;
        if (string.IsNullOrWhiteSpace(_settings.AiApiKey)) return;

        // Jangan menumpuk: reaksi hanya masuk kalau AI sedang menganggur
        if (_aiQueue.Pending > 0 || _aiQueue.Current != null) return;

        double cooldown = Math.Max(3, _settings.AiReactCooldown);
        double needed = level == GameEventLevel.High ? Math.Min(cooldown, 8) : quiet ? 0 : cooldown;
        if ((DateTime.UtcNow - _lastReact).TotalSeconds < needed) return;

        _lastReact = DateTime.UtcNow;
        _aiQueue.Enqueue(new AiJob("Game", text, false, text), _settings.AiMaxPending);
    }
}
