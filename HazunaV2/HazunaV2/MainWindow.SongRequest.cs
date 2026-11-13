using System.IO;
using System.Windows;

namespace HazunaV2;

// Permintaan lagu dari chat: penonton mengetik "!lagu nama" dan suara dari Media & Sounds (jenis Suara) diputar.
public partial class MainWindow
{
    private readonly SoundChannel _songChannel = new();
    private readonly Dictionary<string, DateTime> _songUserLast = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _songGlobalLast = DateTime.MinValue;
    private int _songToken;

    private string SongCommand
    {
        get
        {
            var c = (_settings.SongReqCommand ?? "").Trim();
            return c.Length == 0 ? "!lagu" : c;
        }
    }

    // Isi setelah perintah, atau null kalau bukan perintah lagu
    private string? SongQuery(string text)
    {
        var t = (text ?? "").Trim();
        var cmd = SongCommand;
        if (!t.StartsWith(cmd, StringComparison.OrdinalIgnoreCase)) return null;
        if (t.Length > cmd.Length && !char.IsWhiteSpace(t[cmd.Length])) return null;
        return t[cmd.Length..].Trim();
    }

    private List<MediaItem> SongChoices() =>
        _library.Where(m => m.Kind == "Suara" && File.Exists(m.FilePath)).ToList();

    private static MediaItem? FindSong(List<MediaItem> items, string query)
    {
        string q = query.Trim().ToLowerInvariant();
        if (q.Length == 0) return null;

        MediaItem? Pick(Func<string, bool> test) => items.FirstOrDefault(m => test(m.DisplayName.ToLowerInvariant()));

        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Pick(n => n == q)
            ?? Pick(n => n.StartsWith(q))
            ?? Pick(n => n.Contains(q))
            ?? Pick(n => words.All(w => n.Contains(w)));
    }

    private void OnSongRequestChat(LiveChat c)
    {
        if (!_settings.SongReqEnabled) return;
        var query = SongQuery(c.Text);
        if (query == null) return;

        var items = SongChoices();
        if (query.Length == 0)
        {
            AddLog($"[Lagu] {c.UserName} mengetik {SongCommand} tanpa judul ({items.Count} lagu tersedia).");
            return;
        }

        var song = FindSong(items, query);
        if (song == null)
        {
            AddLog($"[Lagu] {c.UserName}: \"{query}\" tidak ada di daftar suara.");
            return;
        }

        var now = DateTime.Now;
        if (_songChannel.IsPlaying)
        {
            AddLog($"[Lagu] {c.UserName}: masih ada lagu yang diputar, permintaan \"{song.DisplayName}\" dilewati.");
            return;
        }

        double globalLeft = _settings.SongReqGlobalCooldown - (now - _songGlobalLast).TotalSeconds;
        if (globalLeft > 0)
        {
            AddLog($"[Lagu] {c.UserName}: jeda umum, tunggu {Math.Ceiling(globalLeft)} detik.");
            return;
        }

        if (_songUserLast.TryGetValue(c.UserName, out var last))
        {
            double userLeft = _settings.SongReqUserCooldown - (now - last).TotalSeconds;
            if (userLeft > 0)
            {
                AddLog($"[Lagu] {c.UserName}: jeda per orang, tunggu {Math.Ceiling(userLeft)} detik.");
                return;
            }
        }

        _songGlobalLast = now;
        _songUserLast[c.UserName] = now;
        AddLog($"[Lagu] {c.UserName} meminta: {song.DisplayName}");
        _songChannel.Play(song.FilePath, _settings.SongReqVolume);

        int max = _settings.SongReqMaxSeconds;
        if (max > 0)
        {
            int token = ++_songToken;
            _ = Task.Delay(TimeSpan.FromSeconds(max)).ContinueWith(_ =>
            {
                if (token == _songToken) _songChannel.Stop();
            });
        }
    }

    private void SongReq_Changed(object sender, RoutedEventArgs e)
    {
        if (!_settingsReady) return;

        _settings.SongReqEnabled = SongReqCheck.IsChecked == true;
        var cmd = SongReqCmdBox.Text.Trim();
        _settings.SongReqCommand = cmd.Length == 0 ? "!lagu" : cmd;

        int Num(System.Windows.Controls.TextBox box, int cur, int min, int max)
            => int.TryParse(box.Text.Trim(), out var v) ? Math.Clamp(v, min, max) : cur;

        _settings.SongReqUserCooldown = Num(SongReqUserBox, _settings.SongReqUserCooldown, 0, 3600);
        _settings.SongReqGlobalCooldown = Num(SongReqGlobalBox, _settings.SongReqGlobalCooldown, 0, 3600);
        _settings.SongReqVolume = Num(SongReqVolumeBox, _settings.SongReqVolume, 1, 200);
        _settings.SongReqMaxSeconds = Num(SongReqMaxBox, _settings.SongReqMaxSeconds, 0, 3600);
        ShowSongReqSettings();
        SettingsStore.Save(_settings);
    }

    private void ShowSongReqSettings()
    {
        SongReqCheck.IsChecked = _settings.SongReqEnabled;
        SongReqCmdBox.Text = _settings.SongReqCommand;
        SongReqUserBox.Text = _settings.SongReqUserCooldown.ToString();
        SongReqGlobalBox.Text = _settings.SongReqGlobalCooldown.ToString();
        SongReqVolumeBox.Text = _settings.SongReqVolume.ToString();
        SongReqMaxBox.Text = _settings.SongReqMaxSeconds.ToString();
    }

    private void SongReqCopyList_Click(object sender, RoutedEventArgs e)
    {
        var items = SongChoices();
        if (items.Count == 0)
        {
            SongReqStatus.Text = "Belum ada suara di Media & Sounds.";
            return;
        }

        var text = string.Join("\n", items.Select(m => $"{SongCommand} {m.DisplayName}"));
        try { Clipboard.SetText(text); SongReqStatus.Text = $"{items.Count} perintah disalin."; }
        catch { SongReqStatus.Text = "Gagal menyalin ke clipboard."; }
    }
}
