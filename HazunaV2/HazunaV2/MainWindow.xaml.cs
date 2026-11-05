using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HazunaV2;

public partial class MainWindow : Window
{
    private static readonly Dictionary<string, string> Descriptions = new()
    {
        ["Dashboard"] = "Ringkasan aplikasi dan status koneksi.",
        ["Gift Rules"] = "Hubungkan gift dengan tombol, suara, dan media dari pustaka. Durasi = lama satu giliran. Per gift = tiap gift dalam kombo jadi satu giliran.",
        ["Media & Sounds"] = "Pustaka suara dan media. Atur urutan dengan ▲▼, pilih jenisnya, lalu pakai di Gift Rules.",
        ["Top Gifters"] = "Overlay leaderboard penonton dan skor game (WIN = x / target). Skor bisa diatur manual atau otomatis dari script GTA.",
        ["Lucky Wheel"] = "Roda keberuntungan: penonton mengirim gift tertentu untuk memutar roda. Isi roda dan peluangnya Anda atur sendiri.",
        ["Settings"] = "Pengaturan aplikasi. Klik Simpan Pengaturan agar perubahan tersimpan.",
        ["Text to Speech"] = "Membaca komentar TikTok LIVE dengan suara. Komentar dibersihkan dulu, lalu dibaca satu per satu.",
        ["Voice Changer"] = "Mengubah suara mikrofon Anda secara real-time (nada dan efek). Kirim ke VB-Cable agar terdengar di LIVE.",
        ["AI Reply"] = "Menjawab komentar penonton secara otomatis dengan AI, lalu membacakannya lewat suara. Memakai API berbayar.",
    };

    private static readonly string[] TestNames =
        { "Rina", "Budi", "Sari", "Dimas", "Putri", "Andi", "Wulan", "Fajar" };
    private static readonly int[] TestDiamonds = { 1, 5, 10, 30, 100, 500 };

    private readonly ObservableCollection<GiftRule> _rules;
    private readonly ObservableCollection<MediaItem> _library = AppData.Library;
    private readonly Leaderboard _board = new();
    private OverlayWindow? _overlay;
    private string _currentPage = "";

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (s, e) => DarkTitleBar.Apply(this);

        _rules = RuleStore.Load();
        RulesGrid.ItemsSource = _rules;

        InitLibrary();

        BoardGrid.ItemsSource = _board.Top;

        SoundService.Error += msg =>
            Dispatcher.BeginInvoke(() => AddLog("   ! Gagal memutar suara: " + msg));

        InitSettings();
        InitScore();
        InitWheel();
        InitSidebar();
        InitGifts();
        InitDashboard();
        InitTts();
        InitAi();
        InitVoiceChanger();
        InitConnection();
        ShowPage("Dashboard");
    }

    // ---------- Navigasi ----------

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string name)
            ShowPage(name);
    }

    private void ShowPage(string name)
    {
        bool changed = name != _currentPage;
        _currentPage = name;

        PageTitle.Text = name;
        PageDesc.Text = Descriptions.TryGetValue(name, out var d) ? d : "";

        DashboardPage.Visibility = name == "Dashboard" ? Visibility.Visible : Visibility.Collapsed;
        GiftRulesPage.Visibility = name == "Gift Rules" ? Visibility.Visible : Visibility.Collapsed;
        MediaPage.Visibility = name == "Media & Sounds" ? Visibility.Visible : Visibility.Collapsed;
        TopGiftersPage.Visibility = name == "Top Gifters" ? Visibility.Visible : Visibility.Collapsed;
        WheelPage.Visibility = name == "Lucky Wheel" ? Visibility.Visible : Visibility.Collapsed;
        TtsPage.Visibility = name == "Text to Speech" ? Visibility.Visible : Visibility.Collapsed;
        AiPage.Visibility = name == "AI Reply" ? Visibility.Visible : Visibility.Collapsed;
        VcPage.Visibility = name == "Voice Changer" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = name == "Settings" ? Visibility.Visible : Visibility.Collapsed;

        if (name == "Media & Sounds")
            LibraryGrid.Items.Refresh();
        if (name == "Dashboard")
            RefreshDashboard();

        foreach (var child in NavPanel.Children)
        {
            if (child is RadioButton rb)
                rb.IsChecked = (rb.Tag as string) == name;
        }

        if (changed)
        {
            FrameworkElement? page = name switch
            {
                "Dashboard" => DashboardPage,
                "Gift Rules" => GiftRulesPage,
                "Media & Sounds" => MediaPage,
                "Top Gifters" => TopGiftersPage,
                "Text to Speech" => TtsPage,
                "AI Reply" => AiPage,
                "Voice Changer" => VcPage,
                "Settings" => SettingsPage,
                _ => null
            };
            if (page != null)
                AnimateIn(page);
        }
    }

    private static void AnimateIn(FrameworkElement el)
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slide = new TranslateTransform(0, 14);
        el.RenderTransform = slide;

        el.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
    }

    // ---------- Suara ----------

    private void PlaySound(string path, int volumePercent = 100)
    {
        if (string.IsNullOrEmpty(path))
        {
            MessageBox.Show("Belum ada file suara yang dipilih.");
            return;
        }
        if (!System.IO.File.Exists(path))
        {
            MessageBox.Show("File tidak ditemukan:\n" + path);
            return;
        }

        SoundService.Play(path, volumePercent);
    }

    private void StopSound_Click(object sender, RoutedEventArgs e) => SoundService.Stop();

    // ---------- Gift Rules ----------

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        var fresh = new GiftRule { GiftName = "", Key = "" };
        var dlg = new RuleEditorWindow(fresh, true) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        _rules.Add(dlg.Result);
        RuleStore.Save(_rules);
        RulesGrid.SelectedItem = dlg.Result;
        RulesGrid.ScrollIntoView(dlg.Result);
    }

    private void EditRule(GiftRule rule)
    {
        var dlg = new RuleEditorWindow(rule, false) { Owner = this };
        if (dlg.ShowDialog() != true) return;

        rule.CopyFrom(dlg.Result);
        RuleStore.Save(_rules);
    }

    private void TestRule_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GiftRule rule) return;

        if (KeyEnableCheck.IsChecked == true && !string.IsNullOrWhiteSpace(rule.Key) && _settings.SimDelaySeconds > 0)
            AddLog($"   Tombol dikirim {_settings.SimDelaySeconds:0.#} dtk lagi, pindah ke jendela tujuan.");

        _engine.TestRule(rule, "Penguji", _settings.SimDelaySeconds);
    }

    private void EditRule_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GiftRule rule) EditRule(rule);
    }

    private void RulesGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Hanya saat yang diklik adalah baris (bukan kotak centang, nama efek, atau pegangan geser)
        if (e.OriginalSource is not DependencyObject d) return;
        for (var cur = d; cur != null; cur = System.Windows.Media.VisualTreeHelper.GetParent(cur))
        {
            if (cur is System.Windows.Controls.Primitives.ButtonBase || cur is System.Windows.Controls.TextBox) return;
            if (cur is System.Windows.Controls.DataGridRow row && row.Item is GiftRule rule)
            {
                EditRule(rule);
                e.Handled = true;
                return;
            }
        }
    }

    private void TriggerChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Simpan hanya kalau diubah pengguna (bukan saat baris dimuat)
        if (sender is System.Windows.Controls.ComboBox cb && (cb.IsDropDownOpen || cb.IsKeyboardFocusWithin))
            RuleStore.Save(_rules);
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is GiftRule r)
        {
            _rules.Remove(r);
            RuleStore.Save(_rules);
        }
    }

    private void PickKey_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GiftRule rule) return;

        var dlg = new KeyPickerWindow(rule.Key) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            rule.Key = dlg.Result;
            RuleStore.Save(_rules);
        }
    }

    // ---------- Top Gifters ----------

    private void ToggleOverlay_Click(object sender, RoutedEventArgs e)
    {
        var o = EnsureOverlay();
        SetOverlayVisible(o, !_overlayShown.Contains(o));
    }

    private GiftMenuOverlay? _menuOverlay;

    private void ToggleMenuOverlay_Click(object sender, RoutedEventArgs e)
    {
        var o = EnsureMenuOverlay();
        SetOverlayVisible(o, !_overlayShown.Contains(o));
    }

    private static readonly double[] MenuScales = { 1.0, 1.5, GiftMenuOverlay.HdScale };

    private void MenuScale_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingsReady || MenuScaleCombo.SelectedIndex < 0) return;

        double scale = MenuScales[MenuScaleCombo.SelectedIndex];
        _settings.MenuOverlayScale = scale;
        SettingsStore.Save(_settings);
        _menuOverlay?.SetScale(scale);
    }

    private void AddTestGift_Click(object sender, RoutedEventArgs e)
    {
        var name = TestNames[Random.Shared.Next(TestNames.Length)];
        var diamonds = TestDiamonds[Random.Shared.Next(TestDiamonds.Length)];
        _board.AddGift(name, diamonds);
    }

    private void ResetBoard_Click(object sender, RoutedEventArgs e)
    {
        _board.Reset();
        _overlay?.Refresh();
    }

    private void SortCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo) return;
        _board.SortByDiamonds = combo.SelectedIndex == 0;
        _board.Rebuild();
        _overlay?.Refresh();
    }

    private void ChromaCheck_Click(object sender, RoutedEventArgs e)
    {
        bool on = ChromaCheck.IsChecked == true;
        _overlay?.SetChromaKey(on);
        _menuOverlay?.SetChromaKey(on);
        _scoreOverlay?.SetChromaKey(on);
        SetWheelChroma(on);
        _settings.OverlayChromaKey = on;
        SettingsStore.Save(_settings);
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        RulesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        RuleStore.Save(_rules);
        LibraryStore.Save(_library);
        _connector.Disconnect();
        GiftCatalog.Flush();
        _queue.Clear();
        FlushTtsSettings();
        FlushVcSettings();
        _vc.Dispose();
        _tts?.Dispose();
        _overlay?.Close();
        _menuOverlay?.Close();
        _scoreOverlay?.Close();
        CloseAllWheelOverlays();
        WheelStore.Save(_wheelSet);
        SoundService.Stop();
        base.OnClosing(e);
    }
}