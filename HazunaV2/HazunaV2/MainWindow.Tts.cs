using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace HazunaV2;

public partial class MainWindow
{
    private TtsService? _tts;
    private readonly ObservableCollection<string> _chatLog = new();
    private readonly DispatcherTimer _ttsSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private bool _ttsReady;

    private void InitTts()
    {
        ChatList.ItemsSource = _chatLog;

        _ttsSaveTimer.Tick += (s, e) =>
        {
            _ttsSaveTimer.Stop();
            SettingsStore.Save(_settings);
        };

        try
        {
            _tts = new TtsService { OnError = msg => AddLog("   ! TTS: " + msg) };
        }
        catch (Exception ex)
        {
            TtsNote.Text = "Mesin suara tidak bisa dimulai: " + ex.Message;
            return;
        }

        _tts.Changed += UpdateTtsStatus;
        _tts.Start();

        var voices = _tts.GetVoices();
        TtsVoiceBox.ItemsSource = voices;

        TtsVoiceBox.SelectedItem =
            voices.FirstOrDefault(v => v.Name == _settings.TtsVoice)
            ?? voices.FirstOrDefault(v => v.Label.Contains("(id", StringComparison.OrdinalIgnoreCase))
            ?? voices.FirstOrDefault();

        TtsEnableCheck.IsChecked = _settings.TtsEnabled;
        TtsSkipCmdCheck.IsChecked = _settings.TtsSkipCommands;
        TtsSkipLinkCheck.IsChecked = _settings.TtsSkipLinks;
        TtsTemplateBox.Text = _settings.TtsTemplate;
        TtsMaxCharsBox.Text = _settings.TtsMaxChars.ToString();
        TtsMaxQueueBox.Text = _settings.TtsMaxQueue.ToString();
        TtsRateSlider.Value = Math.Clamp(_settings.TtsRate, -10, 10);
        TtsVolumeSlider.Value = Math.Clamp(_settings.TtsVolume, 0, 100);
        TtsRateLabel.Text = ((int)TtsRateSlider.Value).ToString("+0;-0;0");
        TtsVolumeLabel.Text = (int)TtsVolumeSlider.Value + "%";

        _ttsReady = true;
        ApplyTtsEngine();

        if (voices.Count == 0)
            TtsNote.Text = "Tidak ada suara yang terpasang di Windows ini, jadi komentar tidak bisa dibaca.";
        else if (!voices.Any(v => v.Label.Contains("(id", StringComparison.OrdinalIgnoreCase)))
            TtsNote.Text = "Tidak ada suara bahasa Indonesia di daftar. Komentar berbahasa Indonesia " +
                           "akan terdengar aneh dengan suara bahasa lain.";

        UpdateTtsStatus();
    }

    private void ApplyTtsEngine()
    {
        if (_tts == null) return;
        if (TtsVoiceBox.SelectedValue is string name)
            _tts.TrySelectVoice(name);
        _tts.Rate = (int)TtsRateSlider.Value;
        _tts.Volume = (int)TtsVolumeSlider.Value;
    }

    private TtsOptions ReadTtsOptions() =>
        new(_settings.TtsTemplate, _settings.TtsMaxChars,
            _settings.TtsSkipCommands, _settings.TtsSkipLinks);

    // Membaca semua kontrol ke pengaturan, lalu menyimpan (ditunda sebentar)
    private void CommitTtsSettings()
    {
        if (!_ttsReady) return;

        _settings.TtsEnabled = TtsEnableCheck.IsChecked == true;
        _settings.TtsVoice = TtsVoiceBox.SelectedValue as string ?? "";
        _settings.TtsRate = (int)TtsRateSlider.Value;
        _settings.TtsVolume = (int)TtsVolumeSlider.Value;
        _settings.TtsSkipCommands = TtsSkipCmdCheck.IsChecked == true;
        _settings.TtsSkipLinks = TtsSkipLinkCheck.IsChecked == true;

        if (int.TryParse(TtsMaxCharsBox.Text, out var maxChars))
            _settings.TtsMaxChars = Math.Clamp(maxChars, 20, 500);
        TtsMaxCharsBox.Text = _settings.TtsMaxChars.ToString();

        if (int.TryParse(TtsMaxQueueBox.Text, out var maxQueue))
            _settings.TtsMaxQueue = Math.Clamp(maxQueue, 1, 50);
        TtsMaxQueueBox.Text = _settings.TtsMaxQueue.ToString();

        _settings.TtsTemplate = string.IsNullOrWhiteSpace(TtsTemplateBox.Text)
            ? "{text}"
            : TtsTemplateBox.Text.Trim();
        TtsTemplateBox.Text = _settings.TtsTemplate;

        ApplyTtsEngine();

        _ttsSaveTimer.Stop();
        _ttsSaveTimer.Start();
    }

    // Dipanggil saat aplikasi ditutup supaya perubahan terakhir tidak hilang
    private void FlushTtsSettings()
    {
        if (!_ttsSaveTimer.IsEnabled) return;
        _ttsSaveTimer.Stop();
        SettingsStore.Save(_settings);
    }

    // ---------- Komentar masuk ----------

    private void OnLiveChat(LiveChat c)
    {
        if (_tts == null || !_ttsReady)
        {
            AddChatLog(c, "TTS tidak siap");
            return;
        }

        var speech = TtsText.Prepare(c.UserName, c.Text, ReadTtsOptions(), out var reason);
        if (speech == null)
        {
            AddChatLog(c, "dilewati: " + reason);
            return;
        }

        if (!_settings.TtsEnabled)
        {
            AddChatLog(c, "TTS nonaktif");
            return;
        }

        if (!_tts.Enqueue(speech, _settings.TtsMaxQueue))
        {
            AddChatLog(c, "dilewati: antrean penuh");
            return;
        }

        AddChatLog(c, "masuk antrean");
    }

    private void AddChatLog(LiveChat c, string status)
    {
        _chatLog.Add($"[{DateTime.Now:HH:mm:ss}] {c.UserName}: {c.Text}   ({status})");
        while (_chatLog.Count > 100)
            _chatLog.RemoveAt(0);
        ChatList.ScrollIntoView(_chatLog[_chatLog.Count - 1]);
    }

    private void UpdateTtsStatus()
    {
        if (_tts == null) return;

        var cur = _tts.Current;
        var now = cur == null
            ? "Tidak sedang membaca"
            : "Membaca: " + (cur.Length > 60 ? cur[..60] + "..." : cur);

        var waiting = _tts.Pending == 0 ? "antrean kosong" : $"{_tts.Pending} menunggu";
        TtsStatus.Text = $"{now}   |   {waiting}";
    }

    // ---------- Event kontrol ----------

    private void TtsEnable_Click(object sender, RoutedEventArgs e)
    {
        CommitTtsSettings();
        if (_ttsReady && TtsEnableCheck.IsChecked != true)
            _tts?.Clear();
    }

    private void TtsOption_Click(object sender, RoutedEventArgs e) => CommitTtsSettings();

    private void TtsVoice_Changed(object sender, SelectionChangedEventArgs e) => CommitTtsSettings();

    private void TtsField_LostFocus(object sender, RoutedEventArgs e) => CommitTtsSettings();

    private void TtsRate_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ttsReady) return;
        TtsRateLabel.Text = ((int)TtsRateSlider.Value).ToString("+0;-0;0");
        CommitTtsSettings();
    }

    private void TtsVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_ttsReady) return;
        TtsVolumeLabel.Text = (int)TtsVolumeSlider.Value + "%";
        CommitTtsSettings();
    }

    private void TtsTest_Click(object sender, RoutedEventArgs e)
    {
        if (_tts == null)
        {
            MessageBox.Show("Mesin suara belum siap.");
            return;
        }

        var text = TtsTestBox.Text.Trim();
        if (text.Length == 0)
        {
            MessageBox.Show("Isi dulu teks yang mau diuji.");
            return;
        }

        ApplyTtsEngine();
        _tts.Enqueue(text, int.MaxValue);
    }

    private void TtsStop_Click(object sender, RoutedEventArgs e)
    {
        int dropped = _tts?.Clear() ?? 0;
        AddLog($"TTS dihentikan: {dropped} antrean dibatalkan.");
    }

    private void ClearChatLog_Click(object sender, RoutedEventArgs e) => _chatLog.Clear();
}