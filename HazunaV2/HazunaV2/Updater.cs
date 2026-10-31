using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace HazunaV2;

public record UpdateInfo(Version Version, string Tag, string AssetApiUrl, string Notes);

// Memeriksa GitHub Releases, mengunduh HazunaV2.zip, lalu mengganti file aplikasi
// lewat skrip kecil setelah aplikasi ditutup, dan membuka aplikasi lagi.
public static class Updater
{
    private const string AssetName = "HazunaV2.zip";

    public static Version Current
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v == null ? new Version(0, 0, 0) : new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    private static HttpClient CreateClient(string token)
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("HazunaV2-Updater");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        if (!string.IsNullOrWhiteSpace(token))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        return http;
    }

    // Mengembalikan null kalau sudah versi terbaru
    public static async Task<UpdateInfo?> CheckAsync(string repo, string token)
    {
        repo = (repo ?? "").Trim().Trim('/');
        if (!repo.Contains('/') || repo.Contains(' '))
            throw new InvalidOperationException("Isi repo GitHub dengan format pemilik/nama-repo.");

        using var http = CreateClient(token);
        using var resp = await http.GetAsync($"https://api.github.com/repos/{repo}/releases/latest");

        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            throw new InvalidOperationException("Release tidak ditemukan. Cek nama repo, token (kalau repo private), atau belum ada release.");
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
            throw new InvalidOperationException("Tag release tidak dikenali: " + tag);

        if (latest <= Current) return null;

        string? assetUrl = null;
        foreach (var a in root.GetProperty("assets").EnumerateArray())
        {
            if (string.Equals(a.GetProperty("name").GetString(), AssetName, StringComparison.OrdinalIgnoreCase))
            {
                assetUrl = a.GetProperty("url").GetString();
                break;
            }
        }
        if (assetUrl == null)
            throw new InvalidOperationException($"Release {tag} tidak punya file {AssetName}.");

        string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        return new UpdateInfo(latest, tag, assetUrl, notes);
    }

    // Mengunduh, menyiapkan skrip pemasang, lalu mengembalikan true kalau aplikasi harus ditutup sekarang
    public static async Task<bool> DownloadAndStageAsync(UpdateInfo info, string token, Action<string> status)
    {
        string exe = Environment.ProcessPath ?? "";
        if (exe.Length == 0 || Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Update otomatis hanya untuk aplikasi hasil build (exe), bukan saat dijalankan dari dotnet run.");

        string appDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        string work = Path.Combine(Path.GetTempPath(), "HazunaUpdate_" + Guid.NewGuid().ToString("N")[..8]);
        string zip = Path.Combine(work, AssetName);
        string extract = Path.Combine(work, "files");
        Directory.CreateDirectory(extract);

        status("Mengunduh " + info.Tag + "...");
        using (var http = CreateClient(token))
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, info.AssetApiUrl);
            req.Headers.Accept.Clear();
            req.Headers.Accept.ParseAdd("application/octet-stream");
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(zip);
            await resp.Content.CopyToAsync(fs);
        }

        status("Mengekstrak...");
        ZipFile.ExtractToDirectory(zip, extract, true);

        if (!File.Exists(Path.Combine(extract, Path.GetFileName(exe))))
            throw new InvalidOperationException("Isi update tidak berisi " + Path.GetFileName(exe) + ". Update dibatalkan.");

        // Skrip: tunggu aplikasi tertutup, salin file baru, buka lagi
        int pid = Environment.ProcessId;
        string cmd = Path.Combine(work, "update.cmd");
        File.WriteAllText(cmd,
            "@echo off\r\n" +
            ":wait\r\n" +
            $"tasklist /fi \"pid eq {pid}\" | find \"{pid}\" >nul\r\n" +
            "if not errorlevel 1 (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
            $"xcopy \"{extract}\\*\" \"{appDir}\\\" /e /y /q >nul\r\n" +
            $"start \"\" \"{exe}\"\r\n");

        Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{cmd}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden
        });
        return true;
    }
}
