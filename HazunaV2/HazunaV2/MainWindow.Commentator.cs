using System.Windows;

namespace HazunaV2;

public enum GameEventLevel { Low, Medium, High }

public partial class MainWindow
{
    private readonly List<(DateTime At, string Text)> _gameEvents = new();
    private DateTime _lastReact = DateTime.MinValue;

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
        var recent = _gameEvents.Where(e => (now - e.At).TotalSeconds <= 90).TakeLast(8).ToList();
        if (recent.Count == 0) return "";

        var lines = recent.Select(e => $"- {(int)(now - e.At).TotalSeconds} dtk lalu: {e.Text}").ToList();
        lines.Add($"- Skor WIN saat ini: {_score.Value} / {_score.Target}");
        return string.Join("\n", lines);
    }

    private void TryReact(string text, GameEventLevel level)
    {
        if (level == GameEventLevel.Low) return;
        if (_aiQueue == null || !_aiReady || AiEnableCheck.IsChecked != true || AiReactCheck.IsChecked != true) return;
        if (string.IsNullOrWhiteSpace(_settings.AiApiKey)) return;

        // Jangan menumpuk: reaksi hanya masuk kalau AI sedang menganggur
        if (_aiQueue.Pending > 0 || _aiQueue.Current != null) return;

        double cooldown = Math.Max(3, _settings.AiReactCooldown);
        double needed = level == GameEventLevel.High ? Math.Min(cooldown, 8) : cooldown;
        if ((DateTime.UtcNow - _lastReact).TotalSeconds < needed) return;

        _lastReact = DateTime.UtcNow;
        _aiQueue.Enqueue(new AiJob("Game", text, false, text), _settings.AiMaxPending);
    }
}
