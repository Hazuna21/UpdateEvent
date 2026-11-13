using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Windows;

namespace HazunaV2;

// Permintaan lagu dari YouTube: "!play judul artis" -> yt-dlp mencari dan mengunduh audio (m4a), lalu diputar.
// yt-dlp.exe diunduh otomatis ke folder data aplikasi.
public partial class MainWindow
{
    private const string YtDlpUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
    private bool _ytBusy;

    private static string ToolsFolder => Path.Combine(SettingsStore.Folder, "tools");
    private static string YtDlpPath => Path.Combine(ToolsFolder, "yt-dlp.exe");
    private static string YtCacheFolder => Path.Combine(SettingsStore.Folder, "yt_cache");

    private string YtCommand
    {
        get
        {
            var c = (_settings.YtCommand ?? "").Trim();
            return c.Length == 0 ? "!play" : c;
        }
    }

    private string? YtQuery(string text)
    {
        var t = (text ?? "").Trim();
        var cmd = YtCommand;
        if (!t.StartsWith(cmd, StringComparison.OrdinalIgnoreCase)) return null;
        if (t.Length > cmd.Length && !char.IsWhiteSpace(t[cmd.Length])) return null;
        return t[cmd.Length..].Trim();
    }

    private static async Task DownloadYtDlpAsync()
    {
        Directory.CreateDirectory(ToolsFolder);
        string tmp = YtDlpPath + ".tmp";
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("HazunaV2");
        using (var resp = await http.GetAsync(YtDlpUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(tmp);
            await resp.Content.CopyToAsync(fs);
        }
        File.Move(tmp, YtDlpPath, true);
    }

    private void CleanYtCache()
    {
        try
        {
            if (!Directory.Exists(YtCacheFolder)) return;
            foreach (var f in Directory.GetFiles(YtCacheFolder))
                if (DateTime.Now - File.GetLastWriteTime(f) > TimeSpan.FromHours(3))
                    try { File.Delete(f); } catch { }
        }
        catch { }
    }

    private async void OnYtRequestChat(LiveChat c)
    {
        if (!_settings.YtEnabled) return;
        var query = YtQuery(c.Text);
        if (query == null) return;

        if (query.Length == 0)
        {
            AddLog($"[YouTube] {c.UserName} mengetik {YtCommand} tanpa judul.");
            return;
        }
        if (query.Length > 100) query = query[..100];
        if (!FollowerAllowed(c, "YouTube")) return;

        if (_ytBusy) { AddLog($"[YouTube] {c.UserName}: masih mencari lagu lain, dilewati."); return; }
        if (_songChannel.IsPlaying) { AddLog($"[YouTube] {c.UserName}: masih ada lagu yang diputar, dilewati."); return; }

        var now = DateTime.Now;
        double globalLeft = _settings.SongReqGlobalCooldown - (now - _songGlobalLast).TotalSeconds;
        if (globalLeft > 0) { AddLog($"[YouTube] {c.UserName}: jeda umum, tunggu {Math.Ceiling(globalLeft)} detik."); return; }
        if (_songUserLast.TryGetValue(c.UserName, out var last))
        {
            double userLeft = _settings.SongReqUserCooldown - (now - last).TotalSeconds;
            if (userLeft > 0) { AddLog($"[YouTube] {c.UserName}: jeda per orang, tunggu {Math.Ceiling(userLeft)} detik."); return; }
        }

        _ytBusy = true;
        _songGlobalLast = now;
        _songUserLast[c.UserName] = now;
        try
        {
            if (!File.Exists(YtDlpPath))
            {
                AddLog("[YouTube] Mengunduh yt-dlp (sekali saja)...");
                await DownloadYtDlpAsync();
            }

            AddLog($"[YouTube] {c.UserName} meminta: {query} (mencari...)");
            CleanYtCache();
            var (title, path, error) = await Task.Run(() => RunYtDlp(query));
            if (path == null)
            {
                AddLog("[YouTube] Gagal: " + error);
                return;
            }

            AddLog($"[YouTube] Memutar: {title}");
            _songChannel.Play(path, _settings.SongReqVolume);

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
        catch (Exception ex)
        {
            AddLog("[YouTube] Gagal: " + ex.Message);
        }
        finally
        {
            _ytBusy = false;
        }
    }

    private (string title, string? path, string error) RunYtDlp(string query)
    {
        Directory.CreateDirectory(YtCacheFolder);
        int maxSec = Math.Clamp(_settings.YtMaxMinutes, 1, 60) * 60;

        var psi = new ProcessStartInfo(YtDlpPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var a in new[]
        {
            "--encoding", "utf-8", "--no-warnings", "--no-playlist", "--no-mtime", "--windows-filenames",
            "-f", "bestaudio[ext=m4a]/bestaudio[ext=mp4]/best[ext=mp4]",
            "--match-filter", $"duration<={maxSec} & !is_live",
            "--max-downloads", "1",
            "--no-simulate",
            "--print", "TITLE:%(title)s",
            "--print", "after_move:PATH:%(filepath)s",
            "-o", Path.Combine(YtCacheFolder, "%(id)s.%(ext)s"),
            "ytsearch5:" + query
        }) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi) ?? throw new InvalidOperationException("yt-dlp tidak bisa dijalankan.");
        var errTask = p.StandardError.ReadToEndAsync();
        var outTask = p.StandardOutput.ReadToEndAsync();
        if (!p.WaitForExit(120_000))
        {
            try { p.Kill(true); } catch { }
            return ("", null, "waktu habis (lebih dari 2 menit).");
        }

        string title = "", path = "";
        foreach (var line in outTask.Result.Split('\n'))
        {
            var l = line.Trim();
            if (l.StartsWith("TITLE:")) title = l[6..];
            else if (l.StartsWith("PATH:")) path = l[5..];
        }

        if (path.Length > 0 && File.Exists(path)) return (title, path, "");

        string err = errTask.Result.Trim().Split('\n').LastOrDefault()?.Trim() ?? "";
        if (err.Length == 0) err = $"tidak ada hasil yang cocok (video lebih dari {maxSec / 60} menit atau siaran langsung dilewati).";
        return ("", null, err);
    }

    private void YtOption_Changed(object sender, RoutedEventArgs e)
    {
        if (!_settingsReady) return;
        _settings.YtEnabled = YtCheck.IsChecked == true;
        var cmd = YtCmdBox.Text.Trim();
        _settings.YtCommand = cmd.Length == 0 ? "!play" : cmd;
        if (int.TryParse(YtMaxBox.Text.Trim(), out var m)) _settings.YtMaxMinutes = Math.Clamp(m, 1, 60);
        ShowYtSettings();
        SettingsStore.Save(_settings);
    }

    private void ShowYtSettings()
    {
        YtCheck.IsChecked = _settings.YtEnabled;
        YtCmdBox.Text = _settings.YtCommand;
        YtMaxBox.Text = _settings.YtMaxMinutes.ToString();
    }

    private async void YtInstall_Click(object sender, RoutedEventArgs e)
    {
        YtInstallBtn.IsEnabled = false;
        YtStatus.Text = "Mengunduh yt-dlp...";
        try
        {
            await DownloadYtDlpAsync();
            YtStatus.Text = "yt-dlp terpasang / diperbarui.";
        }
        catch (Exception ex)
        {
            YtStatus.Text = "Gagal: " + ex.Message;
        }
        finally
        {
            YtInstallBtn.IsEnabled = true;
        }
    }
}
