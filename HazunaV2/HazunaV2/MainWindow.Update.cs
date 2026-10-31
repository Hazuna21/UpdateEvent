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
            if (info == null)
            {
                UpdateStatus.Text = "Sudah versi terbaru (" + Updater.Current + ").";
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
