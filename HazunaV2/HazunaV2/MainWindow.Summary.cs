using System.Diagnostics;
using System.IO;
using System.Windows;

namespace HazunaV2;

public partial class MainWindow
{
    private readonly LiveSummary _sum = new();
    private string _lastSummary = "";
    private string _lastSummaryPath = "";

    private static string SummaryFolder => Path.Combine(SettingsStore.Folder, "ringkasan");

    // Dipanggil tiap status TikTok berubah: sesi mulai saat tersambung, selesai saat terputus
    private void TrackLiveSession(LiveState state)
    {
        if (state == LiveState.Connected && !_sum.Active)
        {
            _sum.Begin(_score.Value);
            _kindnessOverlay?.Reset();
            ResetLoyalSession();
            return;
        }

        if (_sum.Active && !_connector.IsRunning &&
            (state == LiveState.Disconnected || state == LiveState.Error || state == LiveState.Idle))
        {
            _sum.Finish();
            if (!_sum.HasActivity) return;

            _lastSummary = _sum.Build(_score.Value, _score.Target);
            _lastSummaryPath = SaveSummary(_lastSummary);
            AddLog("[Ringkasan] LIVE selesai, ringkasan disimpan.");
            ShowSummary();
        }
    }

    private string SaveSummary(string text)
    {
        try
        {
            Directory.CreateDirectory(SummaryFolder);
            var path = Path.Combine(SummaryFolder, $"ringkasan-{_sum.Start:yyyy-MM-dd-HHmm}.txt");
            File.WriteAllText(path, text, new System.Text.UTF8Encoding(true));
            return path;
        }
        catch { return ""; }
    }

    private void ShowSummary()
    {
        bool canSpeak = !string.IsNullOrWhiteSpace(_settings.AiApiKey) && _aiReady;
        var win = new SummaryWindow(_lastSummary, _lastSummaryPath,
            canSpeak ? () => SpeakSummary(_lastSummary) : null,
            () =>
            {
                try
                {
                    Directory.CreateDirectory(SummaryFolder);
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SummaryFolder}\"") { UseShellExecute = true });
                }
                catch { }
            })
        { Owner = this };
        win.Show();
    }

    // Tombol "Ringkasan": tampilkan ringkasan sesi yang sedang berjalan, atau yang terakhir
    private void ShowSummary_Click(object sender, RoutedEventArgs e)
    {
        if (_sum.Active)
        {
            _lastSummary = _sum.Build(_score.Value, _score.Target);
            _lastSummaryPath = "";
        }
        else if (_lastSummary.Length == 0)
        {
            MessageBox.Show("Belum ada ringkasan. Ringkasan dibuat otomatis saat LIVE selesai.");
            return;
        }
        ShowSummary();
    }

    private async void SpeakSummary(string report)
    {
        try
        {
            string system = AiClient.BuildSystemPrompt(_settings.AiPersona) +
                "\n\nTugas khusus kali ini: LIVE sudah selesai. Dari data di dalam <komentar_penonton>, buat penutup singkat " +
                "2 sampai 3 kalimat: ucapkan terima kasih, sebut penyumbang teratas dan satu momen menarik. " +
                "Jangan menjawab SKIP dan jangan mengarang di luar data.";
            var (answer, error) = await AiClient.AskAsync(_settings.AiApiKey, _settings.AiModel, system,
                "Ringkasan", report, 300, CancellationToken.None);
            if (answer == null) { AddLog("[AI] Penutup gagal: " + error); return; }

            AddAiLine("   Penutup LIVE: " + answer);
            SpeakAiAnswer("Ringkasan", answer, true);
        }
        catch (Exception ex) { AddLog("[AI] Penutup gagal: " + ex.Message); }
    }
}
