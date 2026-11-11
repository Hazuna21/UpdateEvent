using System.Text;

namespace HazunaV2;

// Mencatat apa yang terjadi selama satu sesi LIVE lalu menyusunnya jadi ringkasan teks.
public class LiveSummary
{
    private readonly Dictionary<string, (long Diamonds, int Gifts)> _gifters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _giftNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _effects = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _wheel = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _effectUsers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _viewers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _chatters = new(StringComparer.OrdinalIgnoreCase);

    private int _wins, _deaths, _follows, _shares, _comments, _giftCount;
    private long _likes, _diamonds;
    private int _scoreStart;

    public bool Active { get; private set; }
    public DateTime Start { get; private set; }
    public DateTime End { get; private set; }

    public bool HasActivity =>
        _giftCount > 0 || _effects.Count > 0 || _wins + _deaths > 0 || _follows + _shares + _comments > 0 || _viewers.Count > 0 || _likes > 0;

    public void Begin(int scoreNow)
    {
        _gifters.Clear(); _giftNames.Clear(); _effects.Clear(); _wheel.Clear(); _effectUsers.Clear();
        _viewers.Clear(); _chatters.Clear();
        _wins = _deaths = _follows = _shares = _comments = _giftCount = 0;
        _likes = _diamonds = 0;
        _scoreStart = scoreNow;
        Start = DateTime.Now;
        Active = true;
    }

    public void Finish() { if (Active) { End = DateTime.Now; Active = false; } }

    private static void Inc(Dictionary<string, int> d, string key, int by = 1)
    {
        key = (key ?? "").Trim();
        if (key.Length == 0) return;
        d[key] = d.GetValueOrDefault(key) + by;
    }

    public void AddGift(string user, string gift, int count, long diamondsEach)
    {
        if (!Active) return;
        count = Math.Max(1, count);
        long total = Math.Max(0, diamondsEach) * count;
        _giftCount += count;
        _diamonds += total;
        Inc(_giftNames, gift, count);
        user = string.IsNullOrWhiteSpace(user) ? "(tanpa nama)" : user.Trim();
        var cur = _gifters.GetValueOrDefault(user);
        _gifters[user] = (cur.Diamonds + total, cur.Gifts + count);
    }

    public void AddEffect(string user, string effect) { if (!Active) return; Inc(_effects, effect); Inc(_effectUsers, user); }
    public void AddWheel(string wheel, string text) { if (Active) Inc(_wheel, $"{text}  ({wheel})"); }
    public void Win() { if (Active) _wins++; }
    public void Death() { if (Active) _deaths++; }
    public void Follow() { if (Active) _follows++; }
    public void Share() { if (Active) _shares++; }
    public void Likes(int n) { if (Active) _likes += Math.Max(1, n); }
    public void Join(string user) { if (Active && !string.IsNullOrWhiteSpace(user)) _viewers.Add(user.Trim()); }
    public void Chat(string user) { if (!Active) return; _comments++; if (!string.IsNullOrWhiteSpace(user)) _chatters.Add(user.Trim()); }

    private static string Top(Dictionary<string, int> d, int n) =>
        d.Count == 0 ? "-" : string.Join(", ", d.OrderByDescending(x => x.Value).Take(n).Select(x => $"{x.Key} ({x.Value}x)"));

    public string Build(int scoreNow, int target)
    {
        var end = Active ? DateTime.Now : End;
        var dur = end - Start;
        var sb = new StringBuilder();

        sb.AppendLine("RINGKASAN LIVE");
        sb.AppendLine($"{Start:dddd, d MMMM yyyy}  {Start:HH:mm} - {end:HH:mm}  (durasi {(int)dur.TotalHours} jam {dur.Minutes} menit)");
        sb.AppendLine();

        sb.AppendLine("GIFT");
        sb.AppendLine($"  Total gift      : {_giftCount:N0}  dari {_gifters.Count} penonton");
        sb.AppendLine($"  Total diamond   : {_diamonds:N0}");
        sb.AppendLine($"  Gift terbanyak  : {Top(_giftNames, 3)}");
        sb.AppendLine("  Penyumbang teratas:");
        if (_gifters.Count == 0) sb.AppendLine("    -");
        int rank = 1;
        foreach (var g in _gifters.OrderByDescending(x => x.Value.Diamonds).ThenByDescending(x => x.Value.Gifts).Take(5))
            sb.AppendLine($"    {rank++}. {g.Key}  -  {g.Value.Diamonds:N0} diamond ({g.Value.Gifts:N0} gift)");
        sb.AppendLine();

        sb.AppendLine("GAME");
        int effectTotal = _effects.Values.Sum();
        sb.AppendLine($"  Efek dipicu     : {effectTotal:N0}   ({Top(_effects, 4)})");
        sb.AppendLine($"  Pemicu terbanyak: {Top(_effectUsers, 3)}");
        sb.AppendLine($"  Sampai puncak   : {_wins}x     Mati: {_deaths}x");
        sb.AppendLine($"  Skor WIN        : {_scoreStart} -> {scoreNow}  (target {target})");
        if (_wheel.Count > 0)
            sb.AppendLine($"  Hasil Lucky Wheel: {Top(_wheel, 5)}");
        sb.AppendLine();

        sb.AppendLine("PENONTON");
        sb.AppendLine($"  Penonton masuk  : {_viewers.Count:N0}");
        sb.AppendLine($"  Komentar        : {_comments:N0}  dari {_chatters.Count} orang");
        sb.AppendLine($"  Like            : {_likes:N0}");
        sb.AppendLine($"  Follow          : {_follows:N0}     Share: {_shares:N0}");
        return sb.ToString().TrimEnd();
    }
}
