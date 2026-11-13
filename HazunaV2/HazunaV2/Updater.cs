using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;

namespace HazunaV2;

public record ReleaseNote(string Tag, DateTime Date, string Notes);

public record UpdateInfo(Version Version, string Tag, string AssetApiUrl, string Notes, bool IsNewer = true);

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

    public static string ResultFile => Path.Combine(SettingsStore.Folder, "update_result.txt");
    public static string LogFile => Path.Combine(SettingsStore.Folder, "update.log");

    // IsNewer = false kalau versi terpasang sudah sama atau lebih baru dari rilis terakhir di GitHub
    public static async Task<UpdateInfo> CheckAsync(string repo, string token)
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

        if (latest <= Current) return new UpdateInfo(latest, tag, "", "", false);

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

    // Daftar rilis terbaru (paling baru di atas) beserta catatannya
    public static async Task<List<ReleaseNote>> HistoryAsync(string repo, string token, int count = 20)
    {
        repo = (repo ?? "").Trim().Trim('/');
        if (!repo.Contains('/') || repo.Contains(' '))
            throw new InvalidOperationException("Isi repo GitHub dengan format pemilik/nama-repo.");

        using var http = CreateClient(token);
        using var resp = await http.GetAsync($"https://api.github.com/repos/{repo}/releases?per_page={count}");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

        var list = new List<ReleaseNote>();
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            string tag = r.GetProperty("tag_name").GetString() ?? "";
            DateTime date = DateTime.Now;
            if (r.TryGetProperty("published_at", out var pa) && pa.ValueKind == JsonValueKind.String
                && DateTime.TryParse(pa.GetString(), null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var d))
                date = d.ToLocalTime();
            string body = r.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() ?? "" : "";
            list.Add(new ReleaseNote(tag, date, body.Trim()));
        }
        // Urut dari versi terbaru ke terlama
        return list.OrderByDescending(x => Version.TryParse(x.Tag.TrimStart('v', 'V'), out var v) ? v : new Version(0, 0)).ToList();
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

        // Folder aplikasi harus bisa ditulis, kalau tidak file baru tidak bisa menimpa yang lama
        try
        {
            string probe = Path.Combine(appDir, ".update_probe");
            File.WriteAllText(probe, "x");
            File.Delete(probe);
        }
        catch
        {
            throw new InvalidOperationException(
                "Folder aplikasi tidak bisa ditulis (" + appDir + "). Pindahkan aplikasi ke folder biasa " +
                "seperti C:\\Hazuna, atau jalankan sebagai administrator.");
        }

        Directory.CreateDirectory(SettingsStore.Folder);
        try { File.Delete(ResultFile); } catch { }

        // Skrip: tunggu aplikasi tertutup, salin file baru (diulang kalau terkunci), catat hasilnya, buka lagi
        int pid = Environment.ProcessId;
        string cmd = Path.Combine(work, "update.cmd");
        File.WriteAllText(cmd,
            "@echo off\r\n" +
            "set n=0\r\n" +
            $"echo [%date% %time%] mulai update ke {info.Tag} > \"{LogFile}\"\r\n" +
            ":wait\r\n" +
            $"tasklist /fi \"pid eq {pid}\" | find \"{pid}\" >nul\r\n" +
            "if errorlevel 1 goto copy\r\n" +
            "set /a n+=1\r\n" +
            "if %n% GEQ 60 goto copy\r\n" +
            "ping -n 2 127.0.0.1 >nul\r\n" +
            "goto wait\r\n" +
            ":copy\r\n" +
            $"echo aplikasi lama tertutup, menyalin dari {extract} ke {appDir} >> \"{LogFile}\"\r\n" +
            $"robocopy \"{extract}\" \"{appDir}\" /E /R:15 /W:1 /NFL /NDL /NP >> \"{LogFile}\" 2>&1\r\n" +
            "if %errorlevel% GEQ 8 goto fail\r\n" +
            $"echo OK {info.Tag} > \"{ResultFile}\"\r\n" +
            "goto run\r\n" +
            ":fail\r\n" +
            $"echo GAGAL: file tidak bisa ditimpa (kode %errorlevel%). Lihat {LogFile} > \"{ResultFile}\"\r\n" +
            ":run\r\n" +
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
