namespace HazunaV2;

public record GiftEvent(string UserName, string GiftName, int Count, long DiamondsEach,
                        double KeyDelaySeconds = 0, long GiftId = 0);

public class GiftEngine
{
    private readonly IEnumerable<GiftRule> _rules;
    private readonly Leaderboard _board;
    private readonly ActionQueue _queue;

    public Action<string>? OnLog { get; set; }
    public Action? OnRulesChanged { get; set; }
    public Action<GiftRule>? OnRuleFired { get; set; }
    public Action<GiftEvent>? OnGift { get; set; }          // dipanggil untuk setiap gift yang masuk (dipakai Lucky Wheel)

    public GiftEngine(IEnumerable<GiftRule> rules, Leaderboard board, ActionQueue queue)
    {
        _rules = rules;
        _board = board;
        _queue = queue;
    }

    private static bool Matches(GiftRule rule, GiftEvent g)
    {
        if (!rule.Enabled || rule.Trigger != RuleTrigger.Gift) return false;

        // ID sama: pasti cocok
        if (rule.GiftId != 0 && g.GiftId != 0 && rule.GiftId == g.GiftId)
            return true;

        // Kalau tidak, cocokkan lewat nama
        return string.Equals(rule.GiftName.Trim(), g.GiftName.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    public void Process(GiftEvent g)
    {
        OnGift?.Invoke(g);

        long total = g.DiamondsEach * g.Count;

        // Leaderboard langsung diperbarui, ada aturan atau tidak
        _board.AddGift(g.UserName, total, g.Count);
        OnLog?.Invoke($"{g.UserName} mengirim {g.Count}x {g.GiftName} ({total:N0} diamonds)");

        var matches = _rules.Where(r => Matches(r, g)).ToList();

        if (matches.Count == 0)
        {
            OnLog?.Invoke("   Tidak ada aturan aktif untuk gift ini.");
            return;
        }

        // Aturan yang tadinya cocok lewat nama kini diikat ke ID gift
        bool bound = false;
        foreach (var r in matches)
        {
            if (r.GiftId == 0 && g.GiftId != 0)
            {
                r.GiftId = g.GiftId;
                bound = true;
                OnLog?.Invoke($"   Aturan \"{r.GiftName}\" kini terhubung ke ID gift {g.GiftId}.");
            }
        }
        if (bound) OnRulesChanged?.Invoke();

        foreach (var rule in matches)
            Enqueue(rule, g.UserName, g.GiftName,
                    rule.RepeatByCount ? Math.Max(1, g.Count) : 1, g.KeyDelaySeconds);
    }

    // Tap-tap / Follow / Share. count = jumlah tap pada event ini (Follow & Share selalu 1).
    public void ProcessSocial(RuleTrigger kind, string user, int count, double keyDelaySeconds = 0)
    {
        count = Math.Max(1, count);
        var rules = _rules.Where(r => r.Enabled && r.Trigger == kind).ToList();

        if (kind == RuleTrigger.Like)
            OnLog?.Invoke($"{user} tap-tap {count}x");
        else
            OnLog?.Invoke($"{user} " + (kind == RuleTrigger.Follow ? "follow" : kind == RuleTrigger.Join ? "bergabung ke" : "share") + " live");

        foreach (var rule in rules)
        {
            int times = 1;
            if (kind == RuleTrigger.Like)
            {
                // Tap dikumpulkan; tiap kali mencapai jumlah yang diatur, satu pemicu
                long acc = rule.LikeAccumulator + count;
                int need = Math.Max(1, rule.LikeCount);
                times = (int)Math.Min(acc / need, 1000);
                rule.LikeAccumulator = acc % need;
                if (times == 0)
                {
                    OnLog?.Invoke($"   Tap terkumpul {rule.LikeAccumulator:N0}/{need:N0} (aturan \"{rule.EffectName}\" belum terpicu)");
                    continue;
                }
                OnLog?.Invoke($"   Tap-tap mencapai {need:N0} → aturan terpicu {times}x");
            }

            Enqueue(rule, user, rule.TriggerLabel, rule.RepeatByCount ? times : 1, keyDelaySeconds);
        }
    }

    // Uji satu aturan saja, satu kali (tanpa peduli aktif/tidaknya dan tanpa mencocokkan aturan lain)
    public void TestRule(GiftRule rule, string user, double keyDelaySeconds)
    {
        OnLog?.Invoke($"Tes aturan \"{rule.EffectDisplay}\"");
        Enqueue(rule, user, rule.TriggerLabel, 1, keyDelaySeconds);
    }

    private void Enqueue(GiftRule rule, string user, string label, int units, double firstDelay)
    {
        string sound = rule.ResolvedSoundPath;
        string media = rule.ResolvedMediaPath;

        bool hasAction = sound.Length > 0
                         || media.Length > 0
                         || !string.IsNullOrWhiteSpace(rule.Key);

        if (!hasAction)
        {
            OnLog?.Invoke("   Aturan cocok, tapi belum punya suara, media, atau tombol.");
            return;
        }

        units = Math.Max(1, units);
        double duration = rule.DurationSeconds;

        var batch = new List<QueuedAction>(units);
        for (int i = 1; i <= units; i++)
        {
            batch.Add(new QueuedAction(
                user, label, i, units,
                rule.Key, rule.HoldMs, false,
                sound, media,
                duration,
                i == 1 ? firstDelay : 0,
                rule.SoundVolume, rule.EffectName,
                rule.WebhookEnabled ? rule.WebhookUrl : "", rule.WebhookMethod, rule.WebhookBody));
        }
        _queue.EnqueueRange(batch);
        OnRuleFired?.Invoke(rule);

        OnLog?.Invoke($"   Masuk antrean: {units}x, {duration:0.#} dtk per giliran " +
                      $"(≈ {ActionQueue.FormatSeconds(units * duration)})");
    }
}