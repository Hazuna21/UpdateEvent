using System.Windows;

namespace HazunaV2;

public partial class MainWindow
{
    private readonly LoyalStore _loyal = LoyalStore.Load();
    private readonly HashSet<string> _loyalSeen = new(StringComparer.OrdinalIgnoreCase);       // sudah dihitung hadir di sesi ini
    private readonly HashSet<string> _loyalAlerted = new(StringComparer.OrdinalIgnoreCase);    // sudah diberi alert di sesi ini
    private readonly System.Windows.Threading.DispatcherTimer _loyalSaveTimer = new() { Interval = TimeSpan.FromSeconds(20) };

    private void InitLoyal()
    {
        _loyalSaveTimer.Tick += (s, e) => _loyal.Save();
        _loyalSaveTimer.Start();
    }

    // Sesi LIVE baru: semua penonton dihitung ulang
    private void ResetLoyalSession()
    {
        _loyalSeen.Clear();
        _loyalAlerted.Clear();
    }

    // Penonton hadir (masuk atau kirim gift). diamonds = nilai gift kalau ini dari gift.
    private void LoyalTouch(string user, long diamonds)
    {
        user = (user ?? "").Trim();
        if (user.Length == 0 || user == "(tanpa nama)") return;
        if (string.Equals(user, _settings.TikTokUsername, StringComparison.OrdinalIgnoreCase)) return;

        var v = _loyal.Get(user);
        if (_loyalSeen.Add(user)) v.Visits++;
        v.Diamonds += Math.Max(0, diamonds);
        v.LastSeen = DateTime.Now;
        _loyal.MarkDirty();

        if (!_settings.LoyalEnabled || _loyalAlerted.Contains(user)) return;

        bool byVisits = _settings.LoyalVisits > 0 && v.Visits >= _settings.LoyalVisits;
        bool byDiamonds = _settings.LoyalDiamonds > 0 && v.Diamonds >= _settings.LoyalDiamonds;
        if (!byVisits && !byDiamonds) return;

        _loyalAlerted.Add(user);
        string detail = v.Visits > 1 ? $"Kunjungan ke-{v.Visits}" : "";
        if (v.Diamonds > 0) detail += (detail.Length > 0 ? " · " : "") + $"total {v.Diamonds:N0} 💎";

        AddLog($"👑 Penonton setia: {user} ({detail})");
        NoteGameEvent($"Penonton setia {user} hadir ({detail})", GameEventLevel.Medium);
        _kindnessOverlay?.ShowBanner($"👑 Penonton setia {user}!\n{detail}");

        if (_settings.LoyalSpeak && _tts != null && _ttsReady)
            _tts.Enqueue($"Penonton setia {user} hadir. Terima kasih sudah selalu menemani!", Math.Max(5, _settings.TtsMaxQueue * 2));
    }

    private void LoyalOption_Changed(object sender, RoutedEventArgs e)
    {
        if (!_settingsReady) return;

        _settings.LoyalEnabled = LoyalEnableCheck.IsChecked == true;
        _settings.LoyalSpeak = LoyalSpeakCheck.IsChecked == true;
        if (int.TryParse(LoyalVisitsBox.Text.Trim(), out var vis)) _settings.LoyalVisits = Math.Clamp(vis, 0, 1000);
        if (int.TryParse(LoyalDiamondsBox.Text.Trim(), out var dia)) _settings.LoyalDiamonds = Math.Clamp(dia, 0, 100000000);
        LoyalVisitsBox.Text = _settings.LoyalVisits.ToString();
        LoyalDiamondsBox.Text = _settings.LoyalDiamonds.ToString();
        SettingsStore.Save(_settings);
    }
}
