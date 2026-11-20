using System.Diagnostics;
using System.IO;
using System.Windows;

namespace HazunaV2;

// Profil game: ganti profil = simpan data profil sekarang, pasang data profil tujuan, lalu aplikasi mulai ulang.
public partial class MainWindow
{
    private bool _skipCloseSave;

    private void InitProfiles()
    {
        if (string.IsNullOrWhiteSpace(_settings.ActiveProfile)) _settings.ActiveProfile = ProfileStore.DefaultName;
        Title = "Hazuna LIVE Control - profil: " + _settings.ActiveProfile;
        RefreshProfileList(_settings.ActiveProfile);
    }

    private void RefreshProfileList(string select)
    {
        var names = ProfileStore.List();
        if (!names.Contains(_settings.ActiveProfile, StringComparer.OrdinalIgnoreCase)) names.Add(_settings.ActiveProfile);
        names = names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();

        ProfileCombo.Items.Clear();
        foreach (var n in names) ProfileCombo.Items.Add(n);
        ProfileCombo.SelectedItem = names.FirstOrDefault(n => string.Equals(n, select, StringComparison.OrdinalIgnoreCase)) ?? _settings.ActiveProfile;
        ProfileActiveText.Text = "Profil aktif: " + _settings.ActiveProfile;
    }

    // Tulis semua data khusus-game yang masih di memori ke file
    private void FlushProfileData()
    {
        RulesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        RuleStore.Save(_rules);
        LibraryStore.Save(_library);
        WheelStore.Save(_wheelSet);
        SoundboardStore.Save(_soundBoard);
    }

    private void ProfileNew_Click(object sender, RoutedEventArgs e)
    {
        var raw = PromptWindow.Ask(this, "Profil baru", "Nama profil (misalnya nama game):");
        if (raw == null) return;
        var name = ProfileStore.Sanitize(raw);
        if (name.Length == 0) { MessageBox.Show(this, "Nama tidak boleh kosong.", "Profil"); return; }
        if (ProfileStore.Exists(name) || string.Equals(name, _settings.ActiveProfile, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Profil dengan nama itu sudah ada.", "Profil");
            return;
        }

        var copy = MessageBox.Show(this,
            "Salin isi profil yang sekarang ke profil baru?\n\nYa = salin (aturan gift, pustaka, roda, soundboard)\nTidak = mulai kosong",
            "Profil baru", MessageBoxButton.YesNoCancel);
        if (copy == MessageBoxResult.Cancel) return;

        try
        {
            if (copy == MessageBoxResult.Yes)
            {
                FlushProfileData();
                ProfileStore.Snapshot(name);
            }
            else ProfileStore.CreateEmpty(name);
            RefreshProfileList(name);
            ProfileStatus.Text = $"Profil \"{name}\" dibuat. Pilih lalu klik \"Ganti profil\" untuk memakainya.";
        }
        catch (Exception ex) { ProfileStatus.Text = "Gagal: " + ex.Message; }
    }

    private void ProfileSwitch_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not string target) return;
        if (string.Equals(target, _settings.ActiveProfile, StringComparison.OrdinalIgnoreCase))
        {
            ProfileStatus.Text = "Profil itu sudah aktif.";
            return;
        }

        var exe = Environment.ProcessPath ?? "";
        bool canRestart = exe.Length > 0 && !Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

        if (MessageBox.Show(this,
                $"Ganti ke profil \"{target}\"?\n\nPengaturan profil \"{_settings.ActiveProfile}\" disimpan dulu. " +
                "Aplikasi akan mulai ulang dan koneksi LIVE akan terputus.",
                "Ganti profil", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            FlushProfileData();
            ProfileStore.Snapshot(_settings.ActiveProfile);   // simpan profil lama
            ProfileStore.Activate(target);                    // pasang profil tujuan
            _settings.ActiveProfile = target;
            SettingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            ProfileStatus.Text = "Gagal mengganti profil: " + ex.Message;
            return;
        }

        _skipCloseSave = true;   // jangan menimpa data profil baru dengan data lama di memori
        if (canRestart)
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c ping -n 3 127.0.0.1 >nul & start \"\" \"{exe}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            });
        }
        else
        {
            MessageBox.Show(this, "Profil sudah diganti. Buka aplikasi lagi secara manual untuk memakainya.", "Profil");
        }
        Application.Current.Shutdown();
    }

    private void ProfileRename_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not string old) return;
        var raw = PromptWindow.Ask(this, "Ubah nama profil", "Nama baru untuk profil \"" + old + "\":", old);
        if (raw == null) return;
        var name = ProfileStore.Sanitize(raw);
        if (name.Length == 0 || string.Equals(name, old, StringComparison.OrdinalIgnoreCase) && name == old) return;
        if (ProfileStore.Exists(name) && !string.Equals(name, old, StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "Profil dengan nama itu sudah ada.", "Profil");
            return;
        }

        try
        {
            bool active = string.Equals(old, _settings.ActiveProfile, StringComparison.OrdinalIgnoreCase);
            if (!ProfileStore.Exists(old))
            {
                if (active) { FlushProfileData(); ProfileStore.Snapshot(old); }
                else ProfileStore.CreateEmpty(old);
            }
            ProfileStore.Rename(old, name);
            if (active)
            {
                _settings.ActiveProfile = name;
                SettingsStore.Save(_settings);
                Title = "Hazuna LIVE Control - profil: " + name;
            }
            RefreshProfileList(name);
            ProfileStatus.Text = $"Profil diganti namanya menjadi \"{name}\".";
        }
        catch (Exception ex) { ProfileStatus.Text = "Gagal: " + ex.Message; }
    }

    private void ProfileDelete_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileCombo.SelectedItem is not string name) return;
        if (string.Equals(name, _settings.ActiveProfile, StringComparison.OrdinalIgnoreCase))
        {
            ProfileStatus.Text = "Profil yang sedang aktif tidak bisa dihapus. Ganti ke profil lain dulu.";
            return;
        }
        if (MessageBox.Show(this, $"Hapus profil \"{name}\" beserta isinya? Ini tidak bisa dibatalkan.",
                "Hapus profil", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        try
        {
            if (ProfileStore.Exists(name)) ProfileStore.Delete(name);
            RefreshProfileList(_settings.ActiveProfile);
            ProfileStatus.Text = $"Profil \"{name}\" dihapus.";
        }
        catch (Exception ex) { ProfileStatus.Text = "Gagal: " + ex.Message; }
    }
}
