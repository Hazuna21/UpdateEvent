using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace HazunaV2;

public enum RuleTrigger { Gift = 0, Like = 1, Follow = 2, Share = 3, Join = 4 }

public class GiftRule : INotifyPropertyChanged
{
    private bool _enabled = true;
    private string _giftName = "";
    private long _giftId;
    private string _key = "";
    private string _soundId = "";
    private string _mediaId = "";
    private int _holdMs = 100;
    private bool _holdKeyFull;
    private bool _repeatByCount = true;
    private double _durationSeconds = 3;
    private RuleTrigger _trigger = RuleTrigger.Gift;
    private int _likeCount = 100;
    private string _effectName = "";
    private int _soundVolume = 100;
    private bool _webhookEnabled;
    private string _webhookUrl = "";
    private string _webhookMethod = "POST";
    private string _webhookBody = "";

    // Volume suara aturan ini: 1 - 200 persen (di atas 100 = diperkeras, bisa pecah kalau terlalu tinggi)
    public int SoundVolume
    {
        get => _soundVolume;
        set { _soundVolume = Math.Clamp(value, 1, 200); OnChanged(); }
    }

    // Webhook: permintaan HTTP yang dikirim tiap kali aturan dijalankan
    public bool WebhookEnabled { get => _webhookEnabled; set { _webhookEnabled = value; OnChanged(); } }
    public string WebhookUrl { get => _webhookUrl; set { _webhookUrl = value ?? ""; OnChanged(); } }
    public string WebhookMethod { get => _webhookMethod; set { _webhookMethod = string.Equals(value, "GET", StringComparison.OrdinalIgnoreCase) ? "GET" : "POST"; OnChanged(); } }
    public string WebhookBody { get => _webhookBody; set { _webhookBody = value ?? ""; OnChanged(); } }

    // ---------- Ringkasan untuk daftar ----------

    [JsonIgnore]
    public string SourceText => _trigger switch
    {
        RuleTrigger.Like => $"Tap-tap  ·  tiap {_likeCount:N0} tap",
        RuleTrigger.Follow => "Follow",
        RuleTrigger.Share => "Share",
        RuleTrigger.Join => "Penonton baru (join)",
        _ => GiftDisplay
    };

    [JsonIgnore]
    public string DetailText
    {
        get
        {
            var parts = new List<string>();
            if (_trigger == RuleTrigger.Gift) parts.Add(GiftInfoText);
            parts.Add(string.IsNullOrWhiteSpace(_key) ? "tanpa tombol" : "tombol " + _key);
            parts.Add(DurationText + " dtk");
            if (!string.IsNullOrEmpty(_soundId)) parts.Add($"suara {_soundVolume}%");
            if (!string.IsNullOrEmpty(_mediaId)) parts.Add("media");
            if (_webhookEnabled && !string.IsNullOrWhiteSpace(_webhookUrl)) parts.Add("webhook");
            return string.Join("  ·  ", parts);
        }
    }

    [JsonIgnore]
    public string EffectDisplay => string.IsNullOrWhiteSpace(_effectName) ? "(belum diberi nama efek)" : _effectName;

    // Salinan lepas (dipakai jendela pengaturan supaya bisa dibatalkan)
    public GiftRule Clone()
    {
        var c = new GiftRule();
        c.CopyFrom(this);
        return c;
    }

    public void CopyFrom(GiftRule o)
    {
        Enabled = o.Enabled;
        Trigger = o.Trigger;
        LikeCount = o.LikeCount;
        GiftName = o.GiftName;
        GiftId = o.GiftId;
        EffectName = o.EffectName;
        Key = o.Key;
        HoldMs = o.HoldMs;
        RepeatByCount = o.RepeatByCount;
        DurationSeconds = o.DurationSeconds;
        SoundId = o.SoundId;
        MediaId = o.MediaId;
        SoundVolume = o.SoundVolume;
        WebhookEnabled = o.WebhookEnabled;
        WebhookUrl = o.WebhookUrl;
        WebhookMethod = o.WebhookMethod;
        WebhookBody = o.WebhookBody;
    }

    // Nama efek yang ditampilkan di overlay menu gift (kosong = tidak ditampilkan)
    public string EffectName
    {
        get => _effectName;
        set { _effectName = value ?? ""; OnChanged(); }
    }

    // Judul kartu di overlay
    [JsonIgnore]
    public string OverlayTitle => _trigger switch
    {
        RuleTrigger.Like => $"Taptap {_likeCount}x",
        RuleTrigger.Follow => "Follow",
        RuleTrigger.Share => "Share",
        RuleTrigger.Join => "Join",
        _ => string.IsNullOrWhiteSpace(_giftName) ? "Gift" : _giftName
    };

    // ---------- Jenis pemicu ----------

    // Gift (bawaan, data lama tetap Gift), Tap-tap (like), Follow, atau Share
    public RuleTrigger Trigger
    {
        get => _trigger;
        set
        {
            _trigger = value;
            OnChanged();
            OnChanged(nameof(TriggerIndex));
            OnChanged(nameof(IsGift));
            OnChanged(nameof(IsLike));
            OnChanged(nameof(IsSocial));
            OnChanged(nameof(IsFollowOrShare));
            OnChanged(nameof(SocialInfoText));
        }
    }

    // Jumlah tap yang dibutuhkan untuk satu kali pemicu (hanya untuk Tap-tap)
    public int LikeCount
    {
        get => _likeCount;
        set
        {
            _likeCount = Math.Clamp(value, 1, 1_000_000);
            OnChanged();
            OnChanged(nameof(LikeCountText));
            OnChanged(nameof(SocialInfoText));
        }
    }

    [JsonIgnore]
    public string LikeCountText
    {
        get => _likeCount.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (int.TryParse((value ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0)
                LikeCount = n;
            else
                OnChanged();
        }
    }

    [JsonIgnore]
    public int TriggerIndex { get => (int)_trigger; set => Trigger = (RuleTrigger)Math.Clamp(value, 0, 4); }

    [JsonIgnore] public bool IsGift => _trigger == RuleTrigger.Gift;
    [JsonIgnore] public bool IsLike => _trigger == RuleTrigger.Like;
    [JsonIgnore] public bool IsFollowOrShare => _trigger == RuleTrigger.Follow || _trigger == RuleTrigger.Share || _trigger == RuleTrigger.Join;
    [JsonIgnore] public bool IsSocial => _trigger != RuleTrigger.Gift;

    [JsonIgnore]
    public string SocialInfoText => _trigger switch
    {
        RuleTrigger.Like => $"Tap-tap: aktif tiap {_likeCount:N0} tap",
        RuleTrigger.Follow => "Follow: aktif tiap ada follower baru",
        RuleTrigger.Share => "Share: aktif tiap live dibagikan",
        RuleTrigger.Join => "Join: aktif tiap ada penonton baru masuk (namanya dipakai untuk zombie)",
        _ => ""
    };

    // Penghitung tap yang belum genap (tidak disimpan ke file)
    [JsonIgnore] public long LikeAccumulator { get; set; }

    [JsonIgnore]
    public string TriggerLabel => _trigger switch
    {
        RuleTrigger.Like => "Tap-tap",
        RuleTrigger.Follow => "Follow",
        RuleTrigger.Share => "Share",
        RuleTrigger.Join => "Join",
        _ => "Gift"
    };

    public bool Enabled { get => _enabled; set { _enabled = value; OnChanged(); } }

    // ---------- Gift ----------

    public string GiftName
    {
        get => _giftName;
        set
        {
            _giftName = value;
            OnChanged();
            OnChanged(nameof(GiftDisplay));
            OnChanged(nameof(GiftInfoText));
        }
    }

    // ID gift dari TikTok; 0 berarti belum diketahui (dicocokkan lewat nama)
    public long GiftId
    {
    get => _giftId;
    set
    {
        _giftId = value;
        OnChanged();
        OnChanged(nameof(GiftInfoText));
    }
    }

    [JsonIgnore]
    public string GiftDisplay =>
        string.IsNullOrWhiteSpace(_giftName) ? "(pilih gift...)" : _giftName;

    [JsonIgnore]
    public string GiftInfoText
    {
        get
        {
            var known = _giftId != 0 ? GiftCatalog.FindById(_giftId) : GiftCatalog.FindByName(_giftName);
            var parts = new List<string>();
            if (known != null && known.Diamonds > 0)
                parts.Add($"{known.Diamonds:N0} diamond");
            parts.Add(_giftId != 0 ? $"ID {_giftId}" : "cocok lewat nama");
            return string.Join("  ·  ", parts);
        }
    }

    // Dipanggil setelah katalog diperbarui, supaya harga di baris ikut segar
    public void RefreshGiftInfo() => OnChanged(nameof(GiftInfoText));

    // ---------- Tombol ----------

    public string Key { get => _key; set { _key = value; OnChanged(); OnChanged(nameof(KeyDisplay)); } }

    public int HoldMs
    {
        get => _holdMs;
        set { _holdMs = Math.Clamp(value, 20, 2000); OnChanged(); }
    }

    // Tidak lagi dipakai di tampilan; dipertahankan supaya data lama tetap terbaca
    public bool HoldKeyFull
    {
        get => _holdKeyFull;
        set { _holdKeyFull = value; OnChanged(); }
    }

    [JsonIgnore]
    public string KeyDisplay =>
        string.IsNullOrWhiteSpace(_key) ? "(belum diatur)" : _key;

    // ---------- Giliran ----------

    // Satu gift dalam kombo = satu giliran
    public bool RepeatByCount
    {
        get => _repeatByCount;
        set { _repeatByCount = value; OnChanged(); }
    }

    public double DurationSeconds
    {
        get => _durationSeconds;
        set
        {
            _durationSeconds = Math.Clamp(value, 0.2, 3600);
            OnChanged();
            OnChanged(nameof(DurationText));
        }
    }

    // Menerima "2.5" maupun "2,5"
    [JsonIgnore]
    public string DurationText
    {
        get => _durationSeconds.ToString("0.##", CultureInfo.InvariantCulture);
        set
        {
            var t = (value ?? "").Trim().Replace(',', '.');
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d > 0)
                DurationSeconds = d;
            else
                OnChanged(); // kembalikan tampilan ke nilai lama
        }
    }

    // ---------- Pilihan dari pustaka ----------

    public string SoundId
    {
        get => _soundId;
        set
        {
            _soundId = value ?? "";
            OnChanged();
            OnChanged(nameof(SoundName));
            OnChanged(nameof(ResolvedSoundPath));
        }
    }

    public string MediaId
    {
        get => _mediaId;
        set
        {
            _mediaId = value ?? "";
            OnChanged();
            OnChanged(nameof(MediaName));
            OnChanged(nameof(ResolvedMediaPath));
        }
    }

    // Data lama (sebelum ada pustaka). Hanya dibaca untuk migrasi, lalu dikosongkan.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SoundPath { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MediaPath { get; set; }

    [JsonIgnore]
    public string ResolvedSoundPath => AppData.Find(_soundId)?.FilePath ?? "";

    [JsonIgnore]
    public string ResolvedMediaPath => AppData.Find(_mediaId)?.FilePath ?? "";

    [JsonIgnore]
    public string SoundName => NameFor(_soundId);

    [JsonIgnore]
    public string MediaName => NameFor(_mediaId);

    private static string NameFor(string id)
    {
        if (string.IsNullOrEmpty(id)) return "(belum dipilih)";
        return AppData.Find(id)?.DisplayName ?? "(dihapus dari pustaka)";
    }

    // Dipanggil saat nama/lokasi item pustaka berubah
    public void RefreshLibraryNames()
    {
        OnChanged(nameof(SoundName));
        OnChanged(nameof(MediaName));
        OnChanged(nameof(ResolvedSoundPath));
        OnChanged(nameof(ResolvedMediaPath));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? n = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        // Ringkasan di daftar ikut diperbarui setiap ada perubahan
        if (n != null && n != nameof(SourceText) && n != nameof(DetailText) && n != nameof(EffectDisplay))
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SourceText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailText)));
            if (n == nameof(EffectName))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(EffectDisplay)));
        }
    }
}