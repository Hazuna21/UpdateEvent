using System.IO;
using System.Text.Json;

namespace HazunaV2;

public class AppSettings
{
    public bool SidebarCollapsed { get; set; }
    public string TikTokUsername { get; set; } = "";
    public bool AutoConnect { get; set; } = true;       // hubungkan otomatis saat LIVE dimulai
    public int SoundVolume { get; set; } = 100;           // lama: volume global, kini dipindah ke tiap aturan
    public bool VolumeMigrated { get; set; }

    // Skor game (WIN = x / target)
    public int ScoreValue { get; set; }
    public int ScoreTarget { get; set; } = 50;
    public int ScoreWin { get; set; } = 1;
    public int ScoreDeath { get; set; } = 2;
    public double SimDelaySeconds { get; set; } = 3;
    public bool OverlayChromaKey { get; set; }
    public string TikToolsApiKey { get; set; } = "";
    public double MenuOverlayScale { get; set; } = GiftMenuOverlay.HdScale;   // ukuran jendela overlay menu gift
    public string UpdateRepo { get; set; } = "";
    public string UpdateToken { get; set; } = "";

    // Text to Speech
    public bool TtsEnabled { get; set; }
    public string TtsVoice { get; set; } = "";
    public double TtsAnimePitch { get; set; } = 1.4;
    public int TtsRate { get; set; } = 0;
    public int TtsVolume { get; set; } = 100;
    public string TtsTemplate { get; set; } = "{user} berkata, {text}";
    public int TtsMaxChars { get; set; } = 150;
    public int TtsMaxQueue { get; set; } = 5;
    public bool TtsSkipCommands { get; set; } = true;
    public bool TtsSkipLinks { get; set; } = true;

    // AI Reply (saklar aktif sengaja tidak disimpan)
    public string AiApiKey { get; set; } = "";
    public string AiModel { get; set; } = AiClient.DefaultModel;
    public string AiPersona { get; set; } = AiClient.DefaultPersona;
    public int AiCooldownSeconds { get; set; } = 2;
    public int AiMaxPending { get; set; } = 3;
    public int AiMaxChars { get; set; } = 200;
    public bool AiOnlyQuestions { get; set; }
    public bool AiSpeakAnswer { get; set; } = true;
    public string AiSpeakTemplate { get; set; } = "{user}, {text}";
    public bool LoyalEnabled { get; set; } = true;
    public int LoyalVisits { get; set; } = 3;             // hadir di sekian LIVE berbeda = penonton setia (0 = abaikan)
    public int LoyalDiamonds { get; set; } = 500;         // atau total diamond sebesar ini (0 = abaikan)
    public bool LoyalSpeak { get; set; } = true;
    public int KindnessBigGift { get; set; } = 100;      // gift dengan total diamond sebesar ini memicu ucapan terima kasih besar (0 = mati)
    public bool SongReqEnabled { get; set; } = true;
    public string SongReqCommand { get; set; } = "!lagu";
    public int SongReqUserCooldown { get; set; } = 60;
    public int SongReqGlobalCooldown { get; set; } = 10;
    public int SongReqVolume { get; set; } = 80;
    public int SongReqMaxSeconds { get; set; } = 45;
    public bool AutoShowOverlays { get; set; } = true;
    public string OverlayTheme { get; set; } = "Neon Ungu";
    public string AppTheme { get; set; } = "Navy Ungu (bawaan)";
    public int AiReactCooldown { get; set; } = 15;      // jeda minimal antar reaksi otomatis pada kejadian game (detik)
    public bool AiWelcome { get; set; }                 // sambut penonton baru (selalu mati saat aplikasi dibuka, seperti saklar AI)
    public int JoinCooldownMinutes { get; set; } = 30;  // penonton yang sama tidak diproses lagi dalam waktu ini
}

public static class SettingsStore
{
    public static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HazunaLiveControl");
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var s = JsonSerializer.Deserialize<AppSettings>(json);
                if (s != null)
                {
                    s.SoundVolume = Math.Clamp(s.SoundVolume, 0, 100);
                    return s;
                }
            }
        }
        catch { /* file rusak: pakai pengaturan bawaan */ }
        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Folder);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
