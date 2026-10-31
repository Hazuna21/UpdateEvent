using System.Collections.ObjectModel;

namespace HazunaV2;

public class GifterEntry
{
    public int Rank { get; set; }
    public string UserName { get; set; } = "";
    public long Diamonds { get; set; }
    public int GiftCount { get; set; }

    public string Initial =>
        UserName.Length > 0 ? char.ToUpper(UserName[0]).ToString() : "?";
}

public class Leaderboard
{
    public const int MaxShown = 5;

    private readonly Dictionary<string, GifterEntry> _totals =
        new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<GifterEntry> Top { get; } = new();
    public bool SortByDiamonds { get; set; } = true;

    public void AddGift(string userName, long diamonds, int giftCount = 1)
    {
        if (!_totals.TryGetValue(userName, out var entry))
        {
            entry = new GifterEntry { UserName = userName };
            _totals[userName] = entry;
        }
        entry.Diamonds += diamonds;
        entry.GiftCount += giftCount;
        Rebuild();
    }

    public void Reset()
    {
        _totals.Clear();
        Rebuild();
    }

    public void Rebuild()
    {
        var sorted = (SortByDiamonds
                ? _totals.Values.OrderByDescending(x => x.Diamonds).ThenByDescending(x => x.GiftCount)
                : _totals.Values.OrderByDescending(x => x.GiftCount).ThenByDescending(x => x.Diamonds))
            .Take(MaxShown)
            .ToList();

        Top.Clear();
        int rank = 1;
        foreach (var s in sorted)
        {
            Top.Add(new GifterEntry
            {
                Rank = rank++,
                UserName = s.UserName,
                Diamonds = s.Diamonds,
                GiftCount = s.GiftCount
            });
        }
    }
}