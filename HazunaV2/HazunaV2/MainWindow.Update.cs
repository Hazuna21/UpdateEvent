using System.Windows;

namespace HazunaV2;

public partial class MainWindow
{
    private bool _updating;

    private void InitUpdate()
    {
        UpdateRepoBox.Text = _settings.UpdateRepo;
        UpdateTokenBox.Password = _settings.UpdateToken;
        UpdateVersionText.Text = "Versi terpasang: " + Updater.Current;

        // Hasil update sebelumnya (ditulis oleh skrip pemasang setelah aplikasi ditutup)
        try
        {
            if (System.IO.File.Exists(Updater.ResultFile))
            {
                string result = System.IO.File.ReadAllText(Updater.ResultFile).Trim();
                System.IO.File.Delete(Updater.ResultFile);

                bool failed = result.StartsWith("GAGAL", StringComparison.OrdinalIgnoreCase);
                UpdateStatus.Text = failed ? result : "Update berhasil: " + result.Replace("OK", "").Trim();
                if (failed)
                    Dispatcher.InvokeAsync(() => MessageBox.Show(result, "Update Hazuna"));
            }
        }
        catch
        {
            // abaikan
        }
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_updating) return;

        _settings.UpdateRepo = UpdateRepoBox.Text.Trim();
        _settings.UpdateToken = UpdateTokenBox.Password.Trim();
        SettingsStore.Save(_settings);

        _updating = true;
        UpdateButton.IsEnabled = false;
        UpdateStatus.Text = "Memeriksa versi baru...";

        try
        {
            var info = await Updater.CheckAsync(_settings.UpdateRepo, _settings.UpdateToken);
            if (!info.IsNewer)
            {
                UpdateStatus.Text = $"Sudah versi terbaru. Terpasang {Updater.Current}, rilis terakhir di GitHub {info.Tag}.";
                return;
            }

            var answer = MessageBox.Show(
                $"Versi baru {info.Tag} tersedia (terpasang: {Updater.Current}).\n\n" +
                (info.Notes.Length > 0 ? info.Notes + "\n\n" : "") +
                "Pasang sekarang? Aplikasi akan tertutup lalu terbuka lagi.",
                "Update Hazuna", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (answer != MessageBoxResult.Yes)
            {
                UpdateStatus.Text = "Update " + info.Tag + " ditunda.";
                return;
            }

            _queue.Clear();
            if (await Updater.DownloadAndStageAsync(info, _settings.UpdateToken, s => UpdateStatus.Text = s))
            {
                UpdateStatus.Text = "Memasang update...";
                Application.Current.Shutdown();
            }
        }
        catch (Exception ex)
        {
            UpdateStatus.Text = "Gagal: " + ex.Message;
        }
        finally
        {
            _updating = false;
            UpdateButton.IsEnabled = true;
        }
    }
}
