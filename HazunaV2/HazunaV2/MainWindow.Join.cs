using System.Windows;

namespace HazunaV2;

public partial class MainWindow
{
    // Penonton yang sudah diproses pada sesi ini (nama -> waktu), supaya yang keluar-masuk tidak berulang
    private readonly Dictionary<string, DateTime> _joinSeen = new(StringComparer.OrdinalIgnoreCase);

    // Penonton baru masuk: jalankan aturan Join (misalnya zombie bernama dia) dan sambut lewat AI
    private void OnViewerJoin(string user, double keyDelaySeconds)
    {
        user = (user ?? "").Trim();
        if (user.Length == 0 || user == "(tanpa nama)") return;

        // Akun streamer sendiri tidak disambut
        if (string.Equals(user, _settings.TikTokUsername, StringComparison.OrdinalIgnoreCase)) return;

        var now = DateTime.UtcNow;
        var window = TimeSpan.FromMinutes(Math.Max(1, _settings.JoinCooldownMinutes));
        if (_joinSeen.TryGetValue(user, out var last) && now - last < window) return;
        _joinSeen[user] = now;

        // Batasi antrean aksi: kalau sudah menumpuk, sambutan zombie dilewati supaya tidak membanjiri game
        if (_queue.Pending <= 8)
            _engine.ProcessSocial(RuleTrigger.Join, user, 1, keyDelaySeconds);
        else
            AddLog($"   {user} bergabung, tapi antrean sedang penuh (zombie dilewati).");

        NoteGameEvent($"{user} baru bergabung ke LIVE", GameEventLevel.Low);
        WelcomeWithAi(user);
    }

    private void WelcomeWithAi(string user)
    {
        if (_aiQueue == null || !_aiReady || AiEnableCheck.IsChecked != true || AiWelcomeCheck.IsChecked != true) return;
        if (string.IsNullOrWhiteSpace(_settings.AiApiKey)) return;

        // Sambutan tidak boleh menutupi jawaban untuk komentar: hanya masuk kalau antrean AI hampir kosong
        if (_aiQueue.Pending >= 2) return;

        _aiQueue.Enqueue(new AiJob(user, "(baru bergabung ke LIVE)", true), _settings.AiMaxPending);
    }

    private void SendTestJoin_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadSimUser(out var user)) return;

        // Uji selalu jalan: lupakan catatan penonton ini dulu
        _joinSeen.Remove(user);
        OnViewerJoin(user, _settings.SimDelaySeconds);
    }
}
