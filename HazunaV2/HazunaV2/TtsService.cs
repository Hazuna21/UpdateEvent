using System.Globalization;
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
        var list = new List<TtsVoice>();
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
        try
        {
            _synth.SelectVoice(name);
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