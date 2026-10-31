using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace HazunaV2;

public partial class MainWindow
{
    private string _libraryFilter = "Semua";

    private void InitLibrary()
    {
        // Item lama belum punya nama dan jenis: lengkapi
        foreach (var item in _library)
        {
            if (string.IsNullOrEmpty(item.Kind))
                item.Kind = AppData.GuessKind(item.FilePath);
            if (string.IsNullOrWhiteSpace(item.Name))
                item.Name = Path.GetFileNameWithoutExtension(item.FilePath);
        }

        MigrateRules();

        LibraryGrid.ItemsSource = _library;
        var view = CollectionViewSource.GetDefaultView(_library);
        view.Filter = o => o is MediaItem m && PassesFilter(m);

        LibraryStore.Save(_library);
        RuleStore.Save(_rules);

        // Setelah ini, setiap perubahan pustaka disimpan otomatis
        AppData.LibraryChanged += OnLibraryChanged;
    }

    // Aturan lama menyimpan lokasi file langsung. Pindahkan ke pustaka.
    private void MigrateRules()
    {
        foreach (var rule in _rules)
        {
            if (!string.IsNullOrWhiteSpace(rule.SoundPath) && string.IsNullOrEmpty(rule.SoundId))
                rule.SoundId = AppData.FindOrAdd(rule.SoundPath, AppData.KindSound).Id;
            rule.SoundPath = null;

            if (!string.IsNullOrWhiteSpace(rule.MediaPath) && string.IsNullOrEmpty(rule.MediaId))
                rule.MediaId = AppData.FindOrAdd(rule.MediaPath, AppData.KindMedia).Id;
            rule.MediaPath = null;
        }
    }

    private void OnLibraryChanged()
    {
        LibraryStore.Save(_library);
        foreach (var rule in _rules)
            rule.RefreshLibraryNames();
    }

    private bool PassesFilter(MediaItem m) =>
        _libraryFilter == "Semua" || m.Kind == _libraryFilter;

    // ---------- Halaman Media & Sounds ----------

    private void LibraryFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb || cb.SelectedItem is not ComboBoxItem item) return;
        _libraryFilter = item.Content?.ToString() ?? "Semua";
        CollectionViewSource.GetDefaultView(_library)?.Refresh();
    }

    private void AddSound_Click(object sender, RoutedEventArgs e) => AddToLibrary(AppData.KindSound);

    private void AddMedia_Click(object sender, RoutedEventArgs e) => AddToLibrary(AppData.KindMedia);

    private void AddToLibrary(string kind)
    {
        var dlg = new OpenFileDialog
        {
            Title = kind == AppData.KindSound ? "Tambah suara ke pustaka" : "Tambah media ke pustaka",
            Multiselect = true,
            Filter = AppData.DialogFilter(kind)
        };
        if (dlg.ShowDialog() != true) return;

        // Pastikan item baru terlihat, bukan tersembunyi oleh filter
        if (_libraryFilter != "Semua" && _libraryFilter != kind)
            LibraryFilterBox.SelectedIndex = 0;

        MediaItem? last = null;
        foreach (var file in dlg.FileNames)
            last = AppData.FindOrAdd(file, kind);

        if (last != null)
        {
            LibraryGrid.SelectedItem = last;
            LibraryGrid.ScrollIntoView(last);
        }
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => MoveLibraryItem(-1);

    private void MoveDown_Click(object sender, RoutedEventArgs e) => MoveLibraryItem(1);

    private void MoveLibraryItem(int step)
    {
        if (LibraryGrid.SelectedItem is not MediaItem item) return;

        int from = _library.IndexOf(item);
        int to = from + step;

        // Lompati item yang tersembunyi oleh filter
        while (to >= 0 && to < _library.Count && !PassesFilter(_library[to]))
            to += step;
        if (to < 0 || to >= _library.Count) return;

        _library.Move(from, to);
        LibraryGrid.SelectedItem = item;
        LibraryGrid.ScrollIntoView(item);
    }

    private void ChangeFile_Click(object sender, RoutedEventArgs e)
    {
        if (LibraryGrid.SelectedItem is not MediaItem item)
        {
            MessageBox.Show("Pilih dulu satu item di daftar.");
            return;
        }

        var dlg = new OpenFileDialog
        {
            Title = "Pilih file pengganti untuk \"" + item.DisplayName + "\"",
            Filter = AppData.DialogFilter(item.Kind)
        };
        if (dlg.ShowDialog() == true)
            item.FilePath = dlg.FileName;
    }

    private void DeleteLibrary_Click(object sender, RoutedEventArgs e)
    {
        if (LibraryGrid.SelectedItem is not MediaItem item) return;

        var users = _rules.Where(r => r.SoundId == item.Id || r.MediaId == item.Id).ToList();
        if (users.Count > 0)
        {
            var answer = MessageBox.Show(
                $"\"{item.DisplayName}\" dipakai oleh {users.Count} aturan di Gift Rules. " +
                "Kalau dihapus, pilihannya di aturan itu dikosongkan.\n\nTetap hapus dari daftar?",
                "Hapus dari pustaka", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;

            foreach (var rule in users)
            {
                if (rule.SoundId == item.Id) rule.SoundId = "";
                if (rule.MediaId == item.Id) rule.MediaId = "";
            }
            RuleStore.Save(_rules);
        }

        _library.Remove(item);
    }

    private void TestLibraryItem_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not MediaItem item) return;

        if (!File.Exists(item.FilePath))
        {
            MessageBox.Show("File tidak ditemukan:\n" + item.FilePath);
            return;
        }

        if (item.Kind == AppData.KindSound)
            PlaySound(item.FilePath);
        else
            MediaPopup.Show(item.FilePath);
    }

    // ---------- Pilihan di Gift Rules ----------

    private void PickSound_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GiftRule rule) return;

        var dlg = new LibraryPickerWindow(AppData.KindSound, rule.SoundId) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            rule.SoundId = dlg.SelectedId;
            RuleStore.Save(_rules);
        }
    }

    private void PickMedia_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GiftRule rule) return;

        var dlg = new LibraryPickerWindow(AppData.KindMedia, rule.MediaId) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            rule.MediaId = dlg.SelectedId;
            RuleStore.Save(_rules);
        }
    }

    private void TestSound_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GiftRule rule) return;
        PlaySound(rule.ResolvedSoundPath);
    }

    private void TestMedia_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GiftRule rule) return;

        var path = rule.ResolvedMediaPath;
        if (string.IsNullOrEmpty(path))
        {
            MessageBox.Show("Belum ada media yang dipilih untuk aturan ini.");
            return;
        }
        if (!File.Exists(path))
        {
            MessageBox.Show("File tidak ditemukan:\n" + path);
            return;
        }

        MediaPopup.Show(path);
    }
}