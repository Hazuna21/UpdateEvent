using System.IO;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;

namespace HazunaV2;

public enum LiveState { Idle, Connecting, Connected, Disconnected, Error }
public record LiveGift(string UserName, string GiftName, long GiftId, int Count, long DiamondsEach,string? ImageUrl = null);
public record LiveGiftInfo(long Id, string Name, long Diamonds, string? ImageUrl = null);
public record LiveChat(string UserName, string Text);
public record LiveSocial(string UserName, RuleTrigger Kind, int Count);

// Jembatan ke pustaka TikTokLive_Sharp. Memakai refleksi supaya tidak bergantung pada
// nama method yang belum bisa diverifikasi; semua yang ditemukan ditulis ke log.
public class TikTokConnector
{
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

    private static readonly string[] RunNames = { "RunAsync", "Run", "StartAsync", "Start", "ConnectAsync", "Connect" };
    private static readonly string[] StopNames = { "StopAsync", "Stop", "DisconnectAsync", "Disconnect" };
    private static readonly string[] StatusKeys = { "Connect", "Disconnect", "LiveEnded", "Error", "Exception", "Fail" };
    private static readonly string[] ActivityKeys = { "Chat", "Comment", "Like", "Viewer", "Join", "Follow", "Share", "RoomUser" };

    private static readonly string[] PreferredGiftEvents = { "OnGift", "OnGiftMessage", "OnWebcastGiftMessage", "OnGiftEvent" };
    private static readonly string[] GiftNoise =
    {
        "Binding", "Panel", "Banner", "Notice", "Rank", "Vote", "Poll", "Tray", "Bubble",
        "Update", "Gallery", "Unlock", "Dynamic", "Particle", "Sticker", "Guide", "Preview", "Goal", "Wish"
    };

    public event Action<LiveGift>? GiftReceived;
    public event Action<LiveChat>? ChatReceived;
    public event Action<LiveSocial>? SocialReceived;
    public event Action<List<LiveGiftInfo>>? CatalogLoaded;
    public event Action<string>? Log;
    public event Action<LiveState, string>? StateChanged;

    private object? _client;
    private CancellationTokenSource? _cts;
    private bool _warnedNoStreakFlag;

    public LiveState State { get; private set; } = LiveState.Idle;
    public bool IsRunning => _cts != null;

    // ---------- Menghubungkan / memutus ----------

    public void Connect(string username)
    {
        if (_cts != null) return;

        username = username.Trim().TrimStart('@');
        _warnedNoStreakFlag = false;
        SetState(LiveState.Connecting, $"TikTok: menghubungkan ke @{username}...");

        try
        {
            var type = FindClientType();
            var client = CreateClient(type, username);
            _client = client;
            _cts = new CancellationTokenSource();

            Subscribe(type, client);
            ProbeCatalog(client);
            _ = RunClientAsync(type, client, _cts.Token);
        }
        catch (Exception ex)
        {
            Cleanup();
            SetState(LiveState.Error, "TikTok: gagal memulai (" + Unwrap(ex).Message + ")");
        }
    }

    public void Disconnect()
    {
        if (_cts == null) return;

        var client = _client;
        Cleanup();

        if (client != null)
        {
            try
            {
                var stop = FindMethod(client.GetType(), StopNames);
                if (stop != null)
                {
                    var args = stop.GetParameters().Select(DefaultFor).ToArray();
                    _ = stop.Invoke(client, args);
                }
                (client as IDisposable)?.Dispose();
            }
            catch (Exception ex)
            {
                Log?.Invoke("Peringatan saat memutus: " + Unwrap(ex).Message);
            }
        }

        SetState(LiveState.Disconnected, "TikTok: terputus");
    }

    private async Task RunClientAsync(Type type, object client, CancellationToken token)
    {
        try
        {
            var method = FindMethod(type, RunNames)
                ?? throw new MissingMethodException("metode Run/RunAsync/Start tidak ditemukan");

            Log?.Invoke("Menjalankan " + method.Name + "...");

            var args = method.GetParameters()
                .Select(p => p.ParameterType == typeof(CancellationToken) ? (object?)token : DefaultFor(p))
                .ToArray();

            bool returnsTask = typeof(Task).IsAssignableFrom(method.ReturnType);
            bool nonBlockingStart = !returnsTask &&
                method.Name.StartsWith("Start", StringComparison.OrdinalIgnoreCase);

            object? result = returnsTask
                ? method.Invoke(client, args)
                : await Task.Run(() => method.Invoke(client, args));

            if (result is Task task)
                await task;

            // Metode "Start" biasanya langsung kembali walau koneksi masih berjalan
            if (nonBlockingStart) return;

            if (IsCurrent(client) && !token.IsCancellationRequested)
            {
                Cleanup();
                SetState(LiveState.Disconnected, "TikTok: koneksi berakhir");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (IsCurrent(client) && !token.IsCancellationRequested)
            {
                Cleanup();
                SetState(LiveState.Error, "TikTok: kesalahan koneksi (" + Unwrap(ex).Message + ")");
            }
        }
    }

    // ---------- Mencari dan membuat klien ----------

    private static Type FindClientType()
    {
        static Type? Search(Assembly asm) => SafeTypes(asm).FirstOrDefault(t =>
            t.IsClass && !t.IsAbstract && t.IsPublic &&
            t.Name.Equals("TikTokLiveClient", StringComparison.OrdinalIgnoreCase));

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()
                     .Where(a => a.GetName().Name?.Contains("TikTok", StringComparison.OrdinalIgnoreCase) == true))
        {
            var found = Search(asm);
            if (found != null) return found;
        }

        foreach (var file in Directory.GetFiles(AppContext.BaseDirectory, "*TikTok*.dll"))
        {
            try
            {
                var found = Search(Assembly.LoadFrom(file));
                if (found != null) return found;
            }
            catch
            {
                // lewati file yang tidak bisa dimuat
            }
        }

        throw new TypeLoadException(
            "pustaka TikTokLive_Sharp tidak ditemukan. Pasang dengan: dotnet add package TikTokLive_Sharp");
    }

    private static IEnumerable<Type> SafeTypes(Assembly asm)
    {
        try
        {
            return asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null).Cast<Type>();
        }
    }

    private object CreateClient(Type type, string username)
    {
        var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Where(c => c.GetParameters().Length > 0 && c.GetParameters()[0].ParameterType == typeof(string))
            .Where(c => c.GetParameters().Skip(1).All(p => p.HasDefaultValue || !p.ParameterType.IsValueType))
            .OrderBy(c => c.GetParameters().Length)
            .FirstOrDefault()
            ?? throw new MissingMethodException("konstruktor TikTokLiveClient(string, ...) tidak ditemukan");

        var ps = ctor.GetParameters();
        var args = new object?[ps.Length];
        args[0] = username;
        for (int i = 1; i < ps.Length; i++)
            args[i] = DefaultFor(ps[i]);

        Log?.Invoke("Konstruktor: " + type.Name + "(" +
                    string.Join(", ", ps.Select(p => p.ParameterType.Name + " " + p.Name)) + ")");

        return ctor.Invoke(args);
    }

    private static MethodInfo? FindMethod(Type type, string[] names)
    {
        foreach (var name in names)
        {
            var m = type.GetMethods(Flags)
                .Where(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase))
                .Where(x => x.GetParameters().All(p => p.ParameterType == typeof(CancellationToken) || p.HasDefaultValue))
                .OrderBy(x => x.GetParameters().Length)
                .FirstOrDefault();
            if (m != null) return m;
        }
        return null;
    }

    private static object? DefaultFor(ParameterInfo p)
    {
        if (p.HasDefaultValue)
            return p.DefaultValue is DBNull ? null : p.DefaultValue;

        var t = p.ParameterType;
        if (t.IsValueType) return Activator.CreateInstance(t);

        if (t != typeof(string) && !t.IsAbstract && !t.IsInterface && t.GetConstructor(Type.EmptyTypes) != null)
        {
            try { return Activator.CreateInstance(t); } catch { }
        }
        return null;
    }

    // ---------- Event ----------

    private void Subscribe(Type type, object client)
    {
        var all = type.GetEvents(Flags);
        Log?.Invoke("Event pada pustaka: " + string.Join(", ", all.Select(e => e.Name)));
        Log?.Invoke("Properti klien: " + string.Join(", ",
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name)));

        // Pilih event gift yang benar: utama dulu, event pendukung hanya sebagai cadangan terakhir
        var giftEvents = all.Where(e => Has(e.Name, "Gift")).Select(e => e.Name).ToList();
        var preferred = giftEvents
            .Where(n => PreferredGiftEvents.Any(p => n.Equals(p, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var chosen = preferred.Count > 0
            ? preferred
            : giftEvents.Where(n => !GiftNoise.Any(k => Has(n, k))).ToList();
        if (chosen.Count == 0)
            chosen = giftEvents;

        var skipped = giftEvents.Except(chosen).ToList();
        Log?.Invoke("Event gift dipakai: " + (chosen.Count == 0 ? "(tidak ada)" : string.Join(", ", chosen)) +
                    (skipped.Count > 0 ? "; diabaikan: " + string.Join(", ", skipped) : ""));

        var attached = new List<string>();
        foreach (var ev in all)
        {
            var name = ev.Name;
            Action<object?>? handler = null;
            string kind = "";

            if (Has(name, "Gift"))
            {
                if (!chosen.Contains(name)) continue;
                handler = p => OnGiftPayload(name, p);
                kind = "gift";
            }
            else if (IsChatEvent(name))
            {
                handler = p => OnChatPayload(name, p);
                kind = "komentar";
            }
            else if (SocialKindOf(name) is RuleTrigger sk)
            {
                handler = p => OnSocialPayload(name, p, sk);
                kind = "tap/follow/share";
            }
            else if (IsSocialGeneric(name))
            {
                handler = p => OnSocialPayload(name, p, null);
                kind = "sosial";
            }
            else if (StatusKeys.Any(k => Has(name, k)))
            {
                handler = p => OnStatusPayload(name, p);
                kind = "status";
            }
            else if (ActivityKeys.Any(k => Has(name, k)))
            {
                handler = _ => MarkActive();
                kind = "aktivitas";
            }

            if (handler == null) continue;

            if (TryAttach(ev, client, handler))
                attached.Add($"{name} ({kind})");
        }

        Log?.Invoke("Terpasang: " + (attached.Count == 0 ? "(tidak ada)" : string.Join(", ", attached)));
    }

    // Memasang handler ke event dengan tipe delegate apa pun
    private bool TryAttach(EventInfo ev, object client, Action<object?> handler)
    {
        try
        {
            var handlerType = ev.EventHandlerType!;
            var invoke = handlerType.GetMethod("Invoke")!;
            if (invoke.ReturnType != typeof(void)) return false;

            var ps = invoke.GetParameters()
                .Select(p => Expression.Parameter(p.ParameterType, p.Name))
                .ToArray();

            Expression payload = ps.Length == 0
                ? Expression.Constant(null, typeof(object))
                : Expression.Convert(ps[^1], typeof(object));

            var body = Expression.Invoke(Expression.Constant(handler), payload);
            var del = Expression.Lambda(handlerType, body, ps).Compile();
            ev.AddEventHandler(client, del);
            return true;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Gagal memasang {ev.Name}: {Unwrap(ex).Message}");
            return false;
        }
    }

    private void MarkActive()
    {
        if (State == LiveState.Connecting)
            SetState(LiveState.Connected, "TikTok: terhubung (event masuk)");
    }

    private void OnStatusPayload(string eventName, object? payload)
    {
        string detail = payload is Exception ex ? ex.Message : "";
        string text = detail.Length > 0 ? $"{eventName}: {detail}" : eventName;
        Log?.Invoke("Event " + text);

        if (Has(eventName, "Disconnect") || Has(eventName, "LiveEnded"))
        {
            if (_cts != null)
            {
                Cleanup();
                SetState(LiveState.Disconnected,
                    Has(eventName, "LiveEnded") ? "TikTok: LIVE berakhir" : "TikTok: terputus");
            }
        }
        else if (Has(eventName, "Connect"))
        {
            SetState(LiveState.Connected, "TikTok: terhubung");
        }
        else
        {
            SetState(LiveState.Error, "TikTok: " + text);
        }
    }

    private static readonly string[] ChatNoise =
        { "Emote", "Tray", "Banner", "Notice", "Bubble", "Rank", "Update", "Sticker", "Guide", "Pinned" };

    private bool _warnedNoChatText;

    private static bool IsChatEvent(string name) =>
        (Has(name, "Chat") || Has(name, "Comment"))
        && !Has(name, "Gift")
        && !ChatNoise.Any(k => Has(name, k));

    private static readonly string[] SocialNoise =
        { "Rank", "Update", "Tray", "Banner", "Notice", "Goal", "Bubble", "Total", "Gift", "Chat", "Comment", "Sticker", "Guide", "Effect" };

    // Event Like / Follow / Share dari pustaka (berdasarkan nama event)
    private static RuleTrigger? SocialKindOf(string name)
    {
        if (SocialNoise.Any(k => Has(name, k))) return null;
        if (Has(name, "Like")) return RuleTrigger.Like;
        if (Has(name, "Follow")) return RuleTrigger.Follow;
        if (Has(name, "Share")) return RuleTrigger.Share;
        if (Has(name, "Join")) return RuleTrigger.Join;
        return null;
    }

    // Beberapa pustaka mengirim follow/share lewat satu event "Social" dengan field aksi
    private static bool IsSocialGeneric(string name) =>
        Has(name, "Social") && !SocialNoise.Any(k => Has(name, k));

    private void OnSocialPayload(string eventName, object? p, RuleTrigger? known)
    {
        try
        {
            if (p == null) return;
            MarkActive();

            if (_dumpedEvents.Add(eventName))
                Log?.Invoke($"Contoh data {eventName} (diagnostik): " + Dump(p));

            var kind = known;
            if (kind == null)
            {
                string action = (FirstString(p, "Action", "ActionType", "Type", "Label", "Display", "Text") ?? "").ToLowerInvariant();
                if (action.Contains("follow")) kind = RuleTrigger.Follow;
                else if (action.Contains("share")) kind = RuleTrigger.Share;
                else return;
            }

            string user = FirstString(p,
                "User.UniqueId", "User.NickName", "User.Nickname",
                "Sender.UniqueId", "Sender.NickName", "Sender.Nickname",
                "UniqueId", "NickName", "Nickname")
                ?? "(tanpa nama)";

            int count = 1;
            if (kind == RuleTrigger.Like)
            {
                // Jumlah tap pada event ini, bukan total keseluruhan live
                long n = FirstLong(p, "Count", "LikeCount", "Likes", "Amount") ?? 1;
                count = (int)Math.Clamp(n, 1, 100000);
            }

            if (IsDuplicateFromOtherEvent(eventName, $"social|{kind}|{user}|{count}"))
                return;

            SocialReceived?.Invoke(new LiveSocial(user, kind.Value, count));
        }
        catch (Exception ex)
        {
            Log?.Invoke("Gagal membaca event tap/follow/share: " + ex.Message);
        }
    }

    private readonly HashSet<string> _dumpedEvents = new();
    private readonly Dictionary<string, (string Event, DateTime At)> _recentGifts = new();
    private readonly object _giftLock = new();
    private bool _warnedDuplicate;

    // Gift yang sama dari event BERBEDA dalam 2 detik dianggap duplikat
    private bool IsDuplicateFromOtherEvent(string eventName, string signature)
    {
        var now = DateTime.UtcNow;
        lock (_giftLock)
        {
            foreach (var key in _recentGifts
                         .Where(x => (now - x.Value.At).TotalSeconds > 5)
                         .Select(x => x.Key)
                         .ToList())
                _recentGifts.Remove(key);

            if (_recentGifts.TryGetValue(signature, out var prev) &&
                prev.Event != eventName &&
                (now - prev.At).TotalSeconds < 2)
                return true;

            _recentGifts[signature] = (eventName, now);
            return false;
        }
    }

        private void OnGiftPayload(string eventName, object? p)
    {
        try
        {
            if (p == null) return;
            MarkActive();

            if (_dumpedEvents.Add(eventName))
                Log?.Invoke($"Contoh data gift dari {eventName} (diagnostik): " + Dump(p));

            string user = FirstString(p,
                "Sender.UniqueId", "Sender.NickName", "Sender.Nickname", "Sender.UserName",
                "User.UniqueId", "User.NickName", "User.Nickname", "UniqueId", "Nickname")
                ?? "(tanpa nama)";

            string giftName = FirstString(p, "Gift.Name", "Gift.GiftName", "GiftName", "Name") ?? "";

            long giftId = FirstLong(p, "Gift.Id", "Gift.GiftId", "GiftId", "Id") ?? 0;

            long rawCount = FirstLong(p, "Amount", "RepeatCount", "Count", "Combo", "Gift.RepeatCount") ?? 1;
            int count = (int)Math.Clamp(rawCount, 1, 100000);

            // Bagian data yang khusus gift (bukan data pengirim, supaya foto profil tidak tertukar)
            var giftPart = GetPath(p, "Gift");

            long diamonds = FirstLong(p,
                "Gift.DiamondCost", "Gift.DiamondCount", "Gift.Diamonds", "Gift.Cost",
                "DiamondCost", "DiamondCount")
                ?? FindNumberByHint(giftPart ?? p, DiamondHints)
                ?? 0;

            string? imageUrl = giftPart != null ? FindImageUrl(giftPart) : null;

            if (!IsFinal(p)) return; // masih di tengah kombo; tunggu akhir kombo

            if (giftName.Length == 0)
            {
                Log?.Invoke($"Gift diterima dari {eventName} tanpa nama. Lihat baris diagnostik di atas.");
                return;
            }

            string signature = $"{user}|{giftId}|{count}|{giftName}";
            if (IsDuplicateFromOtherEvent(eventName, signature))
            {
                if (!_warnedDuplicate)
                {
                    _warnedDuplicate = true;
                    Log?.Invoke($"Gift yang sama datang dari dua event (salah satunya {eventName}): " +
                                "duplikat diabaikan.");
                }
                return;
            }

            GiftReceived?.Invoke(new LiveGift(user, giftName, giftId, count, diamonds, imageUrl));
        }
        catch (Exception ex)
        {
            Log?.Invoke("Gagal membaca data gift: " + ex.Message);
        }
    }

    private bool IsFinal(object p)
    {
        var finished = FirstBool(p, "StreakFinished", "StreakEnded", "StreakEnd", "RepeatEnd",
                                 "IsStreakEnd", "Gift.StreakFinished");
        if (finished != null) return finished.Value;

        var streaking = FirstBool(p, "Streaking", "IsStreaking", "InStreak");
        if (streaking != null) return !streaking.Value;

        if (!_warnedNoStreakFlag)
        {
            _warnedNoStreakFlag = true;
            Log?.Invoke("Penanda akhir kombo tidak ditemukan: tiap event dihitung sendiri-sendiri, " +
                        "jadi kombo bisa terhitung berlebih.");
        }
        return true;
    }

    // ---------- Katalog gift ----------

    // Katalog diisi pustaka setelah terhubung, jadi dicoba beberapa kali
    private void ProbeCatalog(object client)
    {
        _ = Task.Run(async () =>
        {
            int[] delays = { 1500, 3000, 5000, 10000, 20000 };
            foreach (var delay in delays)
            {
                await Task.Delay(delay);
                if (!IsCurrent(client)) return;

                var list = ReadAvailableGifts(client);
                if (list.Count > 0)
                {
                    CatalogLoaded?.Invoke(list);
                    return;
                }
            }

            if (IsCurrent(client))
                Log?.Invoke("Katalog gift belum tersedia dari pustaka (AvailableGifts masih kosong).");
        });
    }

    private List<LiveGiftInfo> ReadAvailableGifts(object client)
    {
        foreach (var propName in new[] { "AvailableGifts", "DisplayedGifts" })
        {
            var list = ParseGifts(GetPath(client, propName), propName);
            if (list.Count > 0) return list;
        }
        return new List<LiveGiftInfo>();
    }

    // Mengubah koleksi gift apa pun (daftar atau kamus) menjadi daftar yang seragam
    private List<LiveGiftInfo> ParseGifts(object? value, string label)
    {
        var result = new List<LiveGiftInfo>();
        if (value is string || value is not System.Collections.IEnumerable entries)
            return result;

        bool dumped = false;
        int total = 0;
        foreach (var entry in entries)
        {
            if (entry == null) continue;
            total++;

            object? gift = entry;
            long? keyId = null;

            // Kamus (Dictionary) menghasilkan pasangan Key dan Value
            var et = entry.GetType();
            if (et.IsGenericType && et.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                var key = GetPath(entry, "Key");
                gift = GetPath(entry, "Value");
                if (key is IConvertible)
                {
                    try { keyId = Convert.ToInt64(key); } catch { }
                }
            }
            if (gift == null) continue;

            if (!dumped)
            {
                dumped = true;
                Log?.Invoke($"Contoh data katalog gift dari {label} (diagnostik): " +
                            (gift is string s ? s : Dump(gift)));
            }

            string name = gift is string str
                ? str
                : FirstString(gift, "Name", "GiftName", "Title") ?? FindStringByHint(gift, "name") ?? "";
            long id = gift is string ? keyId ?? 0 : FirstLong(gift, "Id", "GiftId") ?? keyId ?? 0;
            long diamonds = gift is string ? 0
                : FirstLong(gift, "DiamondCount", "DiamondCost", "Diamonds", "Cost", "Price")
                  ?? FindNumberByHint(gift, DiamondHints)
                  ?? 0;
            string? imageUrl = gift is string ? null : FindImageUrl(gift);

            if (name.Length == 0) continue;
            result.Add(new LiveGiftInfo(id, name.Trim(), diamonds, imageUrl));
        }

        if (result.Count > 0)
            Log?.Invoke($"{label}: {result.Count} gift terbaca");
        else if (total > 0)
            Log?.Invoke($"{label}: {total} entri ada, tapi nama gift tidak terbaca. Lihat baris diagnostik.");

        return result;
    }

    // ---------- Membaca data lewat nama anggota ----------

    private static bool Has(string name, string key) =>
        name.Contains(key, StringComparison.OrdinalIgnoreCase);

    private static object? GetPath(object? obj, string path)
    {
        try
        {
            foreach (var part in path.Split('.'))
            {
                if (obj == null) return null;
                var t = obj.GetType();

                var prop = t.GetProperty(part, Flags);
                if (prop != null && prop.GetIndexParameters().Length == 0)
                {
                    obj = prop.GetValue(obj);
                    continue;
                }

                var field = t.GetField(part, Flags);
                if (field != null)
                {
                    obj = field.GetValue(obj);
                    continue;
                }
                return null;
            }
            return obj;
        }
        catch
        {
            return null;
        }
    }

        private static readonly string[] DiamondHints = { "diamond", "cost", "price", "coin", "worth" };
    private static readonly string[] ImageHints = { "image", "icon", "picture", "cover", "url", "img" };

    // Mencari angka positif pada anggota data yang namanya mengandung salah satu kata petunjuk
    private static long? FindNumberByHint(object? obj, string[] hints, int depth = 0)
    {
        if (obj == null || depth > 2) return null;

        long? best = null;
        int bestRank = int.MaxValue;
        var nested = new List<object>();

        foreach (var (name, value) in ReadMembers(obj))
        {
            if (value == null || value is string || value is bool) continue;
            var t = value.GetType();
            if (t.IsEnum) continue;

            if (t.IsPrimitive || value is decimal)
            {
                int rank = Array.FindIndex(hints, h => name.Contains(h, StringComparison.OrdinalIgnoreCase));
                if (rank < 0) continue;

                long n;
                try { n = Convert.ToInt64(value); } catch { continue; }
                if (n > 0 && rank < bestRank)
                {
                    best = n;
                    bestRank = rank;
                }
            }
            else if (value is not System.Collections.IEnumerable &&
                     !(t.Namespace?.StartsWith("System", StringComparison.Ordinal) ?? false))
            {
                nested.Add(value);
            }
        }

        if (best != null) return best;

        foreach (var n in nested)
        {
            var found = FindNumberByHint(n, hints, depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    private static string? FindStringByHint(object obj, string hint)
    {
        foreach (var (name, value) in ReadMembers(obj))
        {
            if (value is string s && !string.IsNullOrWhiteSpace(s) &&
                name.Contains(hint, StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("url", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("image", StringComparison.OrdinalIgnoreCase))
                return s;
        }
        return null;
    }

    // Mencari tautan gambar di dalam data gift (string, atau daftar string)
    private static string? FindImageUrl(object? obj, int depth = 0)
    {
        if (obj == null || depth > 3) return null;

        var nested = new List<object>();

        foreach (var (name, value) in ReadMembers(obj))
        {
            if (value == null) continue;

            if (value is string s)
            {
                if (s.StartsWith("http", StringComparison.OrdinalIgnoreCase) && IsImageish(name, s))
                    return s;
                continue;
            }

            if (value is System.Collections.IEnumerable items)
            {
                foreach (var item in items)
                {
                    if (item is string us && us.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                        && IsImageish(name, us))
                        return us;
                    break; // cukup periksa elemen pertama
                }
                continue;
            }

            var t = value.GetType();
            if (!t.IsPrimitive && !t.IsEnum &&
                !(t.Namespace?.StartsWith("System", StringComparison.Ordinal) ?? false))
                nested.Add(value);
        }

        foreach (var n in nested)
        {
            var found = FindImageUrl(n, depth + 1);
            if (found != null) return found;
        }
        return null;
    }

    private static bool IsImageish(string memberName, string url) =>
        ImageHints.Any(h => memberName.Contains(h, StringComparison.OrdinalIgnoreCase))
        || url.Contains(".png", StringComparison.OrdinalIgnoreCase)
        || url.Contains(".webp", StringComparison.OrdinalIgnoreCase)
        || url.Contains(".jpg", StringComparison.OrdinalIgnoreCase)
        || url.Contains(".jpeg", StringComparison.OrdinalIgnoreCase)
        || url.Contains("tplv", StringComparison.OrdinalIgnoreCase);

    private static string? FirstString(object obj, params string[] paths)
    {
        foreach (var path in paths)
        {
            var v = GetPath(obj, path);
            if (v is string s && !string.IsNullOrWhiteSpace(s)) return s;
        }
        return null;
    }

    private static long? FirstLong(object obj, params string[] paths)
    {
        foreach (var path in paths)
        {
            var v = GetPath(obj, path);
            if (v == null || v is bool || v.GetType().IsEnum) continue;
            try
            {
                if (v is IConvertible) return Convert.ToInt64(v);
            }
            catch
            {
                // bukan angka: coba jalur berikutnya
            }
        }
        return null;
    }

    private static bool? FirstBool(object obj, params string[] paths)
    {
        foreach (var path in paths)
        {
            var v = GetPath(obj, path);
            if (v == null) continue;
            if (v is bool b) return b;
            if (v is string s && bool.TryParse(s, out var parsed)) return parsed;
            if (v is IConvertible && !v.GetType().IsEnum)
            {
                try { return Convert.ToInt64(v) != 0; } catch { }
            }
        }
        return null;
    }

        private static string Dump(object obj, int depth = 0)
    {
        var sb = new StringBuilder();
        sb.Append(obj.GetType().Name).Append('{');

        foreach (var (name, value) in ReadMembers(obj))
        {
            if (value == null) continue;
            var t = value.GetType();

            if (t.IsPrimitive || value is string || t.IsEnum || value is decimal)
            {
                sb.Append(name).Append('=').Append(value).Append("; ");
            }
            else if (value is System.Collections.IEnumerable items)
            {
                var first = items.Cast<object?>().FirstOrDefault();
                if (first is string fs)
                    sb.Append(name).Append("[0]=").Append(fs).Append("; ");
            }
            else if (depth < 2)
            {
                sb.Append(name).Append('=').Append(Dump(value, depth + 1)).Append("; ");
            }
        }

        sb.Append('}');
        var text = sb.ToString();
        return text.Length > 1800 ? text[..1800] + "..." : text;
    }

    private static IEnumerable<(string Name, object? Value)> ReadMembers(object obj)
    {
        var t = obj.GetType();
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0) continue;
            object? v;
            try { v = p.GetValue(obj); } catch { continue; }
            yield return (p.Name, v);
        }
        foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            object? v;
            try { v = f.GetValue(obj); } catch { continue; }
            yield return (f.Name, v);
        }
    }

    private void OnChatPayload(string eventName, object? p)
    {
        try
        {
            if (p == null) return;
            MarkActive();

            if (_dumpedEvents.Add(eventName))
                Log?.Invoke($"Contoh data komentar dari {eventName} (diagnostik): " + Dump(p));

            string? text = FirstString(p, "Comment", "Content", "Text", "Message", "Msg");
            if (string.IsNullOrWhiteSpace(text))
            {
                if (!_warnedNoChatText)
                {
                    _warnedNoChatText = true;
                    Log?.Invoke($"Komentar diterima dari {eventName} tapi teksnya tidak terbaca. " +
                                "Lihat baris diagnostik di atas.");
                }
                return;
            }

            string user = FirstString(p,
                "User.NickName", "User.Nickname", "User.UniqueId",
                "Sender.NickName", "Sender.Nickname", "Sender.UniqueId",
                "NickName", "Nickname", "UniqueId")
                ?? "(tanpa nama)";

            // Komentar yang sama dari event berbeda dalam 2 detik dianggap duplikat
            if (IsDuplicateFromOtherEvent(eventName, $"chat|{user}|{text}"))
                return;

            ChatReceived?.Invoke(new LiveChat(user, text.Trim()));
        }
        catch (Exception ex)
        {
            Log?.Invoke("Gagal membaca data komentar: " + ex.Message);
        }
    }

    // ---------- Utilitas ----------

    private bool IsCurrent(object client) => ReferenceEquals(_client, client);

    private void Cleanup()
    {
        var cts = _cts;
        _cts = null;
        _client = null;
        try
        {
            cts?.Cancel();
            cts?.Dispose();
        }
        catch
        {
            // abaikan
        }
    }

    private void SetState(LiveState state, string message)
    {
        State = state;
        StateChanged?.Invoke(state, message);
    }

    private static Exception Unwrap(Exception ex)
    {
        while (ex is TargetInvocationException or AggregateException && ex.InnerException != null)
            ex = ex.InnerException;
        return ex;
    }
}