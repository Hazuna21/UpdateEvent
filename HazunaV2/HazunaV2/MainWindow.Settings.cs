using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace HazunaV2;

public partial class MainWindow
{
    private readonly AppSettings _settings = SettingsStore.Load();
    private bool _settingsReady;

    private void InitSettings()
    {
        ApiKeyBox.Password = _settings.TikToolsApiKey;
        UsernameBox.Text = _settings.TikTokUsername;
        AutoConnectCheck.IsChecked = _settings.AutoConnect;
        SimDelayBox.Text = _settings.SimDelaySeconds.ToString();
        ChromaCheck.IsChecked = _settings.OverlayChromaKey;
        KindnessBigBox.Text = _settings.KindnessBigGift.ToString();
        LoyalEnableCheck.IsChecked = _settings.LoyalEnabled;
        LoyalSpeakCheck.IsChecked = _settings.LoyalSpeak;
        LoyalVisitsBox.Text = _settings.LoyalVisits.ToString();
        LoyalDiamondsBox.Text = _settings.LoyalDiamonds.ToString();
        InitLoyal();

        OverlayTheme.Set(_settings.OverlayTheme);
        GiftMenuOverlay.ReloadTheme();
        foreach (var t in OverlayTheme.All) ThemeCombo.Items.Add(t.Name);
        ThemeCombo.SelectedItem = OverlayTheme.Current.Name;
        MigrateVolume();

        InitUpdate();

        // Pilihan ukuran overlay menu gift (yang terdekat dengan nilai tersimpan)
        int best = 0;
        double[] scales = { 1.0, 1.5, GiftMenuOverlay.HdScale };
        for (int i = 1; i < scales.Length; i++)
            if (Math.Abs(scales[i] - _settings.MenuOverlayScale) < Math.Abs(scales[best] - _settings.MenuOverlayScale)) best = i;
        MenuScaleCombo.SelectedIndex = best;

        _settingsReady = true;
    }

    // Volume dulu satu untuk semua; kini per aturan. Terapkan nilai lama sekali saja.
    private void MigrateVolume()
    {
        if (_settings.VolumeMigrated) return;

        int old = Math.Clamp(_settings.SoundVolume, 1, 100);
        if (old != 100)
        {
            foreach (var r in _rules) r.SoundVolume = old;
            RuleStore.Save(_rules);
        }
        _settings.VolumeMigrated = true;
        SettingsStore.Save(_settings);
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        var user = UsernameBox.Text.Trim().TrimStart('@');
        if (user.Contains(' '))
        {
            MessageBox.Show("Nama pengguna TikTok tidak boleh mengandung spasi.");
            return;
        }
        if (!double.TryParse(SimDelayBox.Text, out var delay) || delay < 0 || delay > 30)
        {
            MessageBox.Show("Tunda uji harus berupa angka antara 0 sampai 30 detik.");
            return;
        }

        _settings.TikTokUsername = user;
        _settings.TikToolsApiKey = ApiKeyBox.Password.Trim();
        _settings.SimDelaySeconds = delay;
        SettingsStore.Save(_settings);

        UsernameBox.Text = user;
        SettingsStatus.Text = "Tersimpan pukul " + DateTime.Now.ToString("HH:mm:ss");
    }

    private void OpenDataFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(SettingsStore.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SettingsStore.Folder}\"")
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Gagal membuka folder:\n" + ex.Message);
        }
    }
}