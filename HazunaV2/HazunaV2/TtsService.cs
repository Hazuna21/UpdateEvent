using System.Globalization;
using System.IO;
using System.Net.Http;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Speech.Synthesis;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace HazunaV2;

public record TtsVoice(string Name, string Label);

public record TtsOptions(string Template, int MaxChars, bool SkipCommands, bool SkipLinks);

// Mesin suara: antrean teks yang dibaca satu per satu
public class TtsService : IDisposable
{
    // Suara online bahasa Indonesia (wanita): tidak ada suara wanita Indonesia bawaan di Windows
    public const string OnlineVoiceName = "online:google-id";
    private static readonly HttpClient OnlineHttp = CreateOnlineHttp();
    private bool _useOnline;
    private CancellationTokenSource? _onlineCts;

    private static HttpClient CreateOnlineHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        return http;
    }

    private readonly SpeechSynthesizer _synth = new();

    private readonly Channel<(string Text, int Generation)> _channel =
        Channel.CreateUnbounded<(string Text, int Generation)>(
            new UnboundedChannelOptions { SingleReader = true });

    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;
    private int _pending;
    private int _generation;
    private bool _disposed;

    public Action<string>? OnError { get; set; }
    public event Action? Changed;

    public string? Current { get; private set; }
    public int Pending => Math.Max(0, Volatile.Read(ref _pending));

    public int Rate
    {
        get => _synth.Rate;
        set => _synth.Rate = Math.Clamp(value, -10, 10);
    }

    public int Volume
    {
        get => _synth.Volume;
        set => _synth.Volume = Math.Clamp(value, 0, 100);
    }

    public List<TtsVoice> GetVoices()
    {
        var list = new List<TtsVoice> { new(OnlineVoiceName, "Indonesia, Wanita (online)") };
        foreach (var v in _synth.GetInstalledVoices())
        {
            if (!v.Enabled) continue;
            var info = v.VoiceInfo;
            list.Add(new TtsVoice(info.Name, $"{info.Name}  ({info.Culture.Name})"));
        }
        return list;
    }

    public bool TrySelectVoice(string name)
    {
        if (name == OnlineVoiceName)
        {
            _useOnline = true;
            return true;
        }

        try
        {
            _synth.SelectVoice(name);
            _useOnline = false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Start() => _ = WorkerAsync();

    // Mengembalikan false kalau antrean sudah penuh
    public bool Enqueue(string text, int maxPending)
    {
        if (_disposed) return false;
        if (Pending >= maxPending) return false;

        Interlocked.Increment(ref _pending);
        _channel.Writer.TryWrite((text, Volatile.Read(ref _generation)));
        RaiseChanged();
        return true;
    }

    // Membatalkan semua antrean dan menghentikan suara yang sedang dibaca
    public int Clear()
    {
        Interlocked.Increment(ref _generation);
        int dropped = Interlocked.Exchange(ref _pending, 0);
        try { _synth.SpeakAsyncCancelAll(); } catch { }
        try { _onlineCts?.Cancel(); } catch { }
        RaiseChanged();
        return dropped;
    }

    private async Task WorkerAsync()
    {
        await foreach (var (text, generation) in _channel.Reader.ReadAllAsync())
        {
            if (generation != Volatile.Read(ref _generation))
                continue; // sudah dibatalkan

            Interlocked.Decrement(ref _pending);
            Current = text;
            RaiseChanged();

            try
            {
                await SpeakOneAsync(text);
            }
            catch (Exception ex)
            {
                OnError?.Invoke(ex.Message);
            }
            finally
            {
                Current = null;
                RaiseChanged();
            }
        }
    }

    private async Task SpeakOneAsync(string text)
    {
        if (_useOnline)
        {
            try
            {
                await SpeakOnlineAsync(text);
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                OnError?.Invoke("Suara online gagal (" + ex.Message + "), dibacakan dengan suara Windows.");
            }
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnDone(object? s, SpeakCompletedEventArgs e) => tcs.TrySetResult();

        _synth.SpeakCompleted += OnDone;
        try
        {
            _synth.SpeakAsync(text);

            // Pengaman: kalau suara macet lebih dari 60 detik, hentikan
            var finished = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(60)));
            if (finished != tcs.Task)
                _synth.SpeakAsyncCancelAll();
        }
        finally
        {
            _synth.SpeakCompleted -= OnDone;
        }
    }

    // ---------- Suara online ----------

    private async Task SpeakOnlineAsync(string text)
    {
        using var cts = new CancellationTokenSource();
        _onlineCts = cts;
        try
        {
            // Unduh semua potongan sekaligus; potongan berikutnya sudah siap saat yang pertama selesai dibaca
            var downloads = SplitForOnline(text, 180).Select(c => DownloadOnlineAsync(c, cts.Token)).ToList();
            foreach (var d in downloads)
            {
                var bytes = await d;
                await PlayMp3Async(bytes, cts.Token);
            }
        }
        finally
        {
            _onlineCts = null;
        }
    }

    private static async Task<byte[]> DownloadOnlineAsync(string chunk, CancellationToken ct)
    {
        string url = "https://translate.google.com/translate_tts?ie=UTF-8&client=tw-ob&tl=id&q=" + Uri.EscapeDataString(chunk);
        using var res = await OnlineHttp.GetAsync(url, ct);
        if (!res.IsSuccessStatusCode)
            throw new InvalidOperationException("server menjawab " + (int)res.StatusCode);
        var bytes = await res.Content.ReadAsByteArrayAsync(ct);
        if (bytes.Length < 200)
            throw new InvalidOperationException("suara kosong");
        return bytes;
    }

    private async Task PlayMp3Async(byte[] bytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        using var ms = new MemoryStream(bytes);
        using var reader = new Mp3FileReader(ms);
        var sample = new VolumeSampleProvider(reader.ToSampleProvider()) { Volume = Math.Clamp(_synth.Volume, 0, 100) / 100f };

        using var output = new WaveOutEvent();
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (s, e) => done.TrySetResult();
        output.Init(sample);

        using (ct.Register(() => { try { output.Stop(); } catch { } }))
        {
            output.Play();
            await done.Task;
        }

        ct.ThrowIfCancellationRequested();
    }

    // Layanan online membatasi panjang teks: pecah di tanda baca atau spasi
    private static IEnumerable<string> SplitForOnline(string text, int max)
    {
        var sb = new StringBuilder();
        foreach (var part in Regex.Split(text.Trim(), @"(?<=[\.\!\?\,\n])\s*"))
        {
            var piece = part.Trim();
            if (piece.Length == 0) continue;

            while (piece.Length > max)
            {
                int cut = piece.LastIndexOf(' ', max - 1);
                if (cut <= 0) cut = max;
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
                yield return piece[..cut].Trim();
                piece = piece[cut..].Trim();
            }

            if (sb.Length + piece.Length + 1 > max)
            {
                yield return sb.ToString();
                sb.Clear();
            }
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(piece);
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    private void RaiseChanged()
    {
        if (_ui != null)
            _ui.Post(_ => Changed?.Invoke(), null);
        else
            Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _channel.Writer.TryComplete();
        try { _synth.SpeakAsyncCancelAll(); } catch { }
        _synth.Dispose();
    }
}

// Membersihkan komentar sebelum dibaca
public static class TtsText
{
    private static readonly Regex LinkRegex =
        new(@"https?://\S+|www\.\S+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Huruf yang sama berulang lebih dari 3x dipendekkan ("haaaaaai" -> "haaai")
    private static readonly Regex RepeatRegex = new(@"(\p{L})\1{3,}", RegexOptions.Compiled);

    private static readonly Regex SpacesRegex = new(@"\s+", RegexOptions.Compiled);

    private static readonly Regex TemplateRegex =
        new(@"\{(user|text)\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Mengembalikan teks yang siap dibaca, atau null (alasannya ada di 'reason')
    public static string? Prepare(string user, string comment, TtsOptions o, out string reason)
    {
        reason = "";
        var text = (comment ?? "").Trim();

        if (text.Length == 0)
        {
            reason = "komentar kosong";
            return null;
        }

        if (o.SkipCommands && (text.StartsWith('!') || text.StartsWith('/')))
        {
            reason = "perintah (diawali ! atau /)";
            return null;
        }

        if (o.SkipLinks)
            text = LinkRegex.Replace(text, " ");

        text = Clean(text);
        if (text.Length == 0)
        {
            reason = "tidak ada teks yang bisa dibaca";
            return null;
        }

        if (text.Length > o.MaxChars)
            text = Truncate(text, o.MaxChars);

        var name = CleanName(user);
        var template = string.IsNullOrWhiteSpace(o.Template) ? "{text}" : o.Template;

        return TemplateRegex.Replace(template,
            m => m.Groups[1].Value.Equals("user", StringComparison.OrdinalIgnoreCase) ? name : text).Trim();
    }

    // Membuang emoji dan karakter aneh, merapikan spasi
    private static string Clean(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (char.IsSurrogate(ch)) continue;                      // emoji
            if (ch >= '\uFE00' && ch <= '\uFE0F') continue;          // penanda emoji

            var cat = char.GetUnicodeCategory(ch);
            if (cat is UnicodeCategory.OtherSymbol or UnicodeCategory.Format or UnicodeCategory.Control)
            {
                sb.Append(' ');
                continue;
            }
            sb.Append(ch);
        }

        var t = RepeatRegex.Replace(sb.ToString(), "$1$1$1");
        return SpacesRegex.Replace(t, " ").Trim();
    }

    private static string CleanName(string user)
    {
        var n = Clean((user ?? "").Replace('_', ' ').Replace('.', ' '));
        if (n.Length > 20) n = n[..20].TrimEnd();
        return n.Length == 0 ? "seseorang" : n;
    }

    private static string Truncate(string s, int max)
    {
        var cut = s[..max];
        int space = cut.LastIndexOf(' ');
        if (space > max / 2) cut = cut[..space];
        return cut.TrimEnd(' ', ',', '.');
    }
}