using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HazunaV2;

// Jendela pengaturan lengkap untuk satu aturan. Bekerja pada salinan; baru diterapkan saat klik Simpan.
public class RuleEditorWindow : Window
{
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0xA5, 0xB0, 0xD0));
    private static readonly Brush Cyan = new SolidColorBrush(Color.FromRgb(0x22, 0xD3, 0xEE));

    private readonly GiftRule _r;             // salinan kerja
    public GiftRule Result => _r;

    private readonly CheckBox _enabled = new() { Content = "Aturan aktif" };
    private readonly ComboBox _trigger = new();
    private readonly TextBox _effect = new();
    private readonly TextBlock _giftText = new() { Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _giftInfo = new() { Foreground = Muted, FontSize = 11 };
    private readonly Button _giftBtn = new();
    private readonly TextBox _likeCount = new() { Width = 90 };
    private readonly StackPanel _giftRow = new();
    private readonly StackPanel _likeRow = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock _socialInfo = new() { Foreground = Muted };
    private readonly TextBox _duration = new() { Width = 90 };
    private readonly CheckBox _repeat = new() { Content = "Ulangi sesuai jumlah gift / tap" };
    private readonly TextBlock _keyText = new() { Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _hold = new() { Width = 90 };
    private readonly TextBlock _soundName = new() { Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Slider _vol = new() { Minimum = 1, Maximum = 200, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 300 };
    private readonly TextBlock _volLabel = new() { Foreground = Cyan, FontWeight = FontWeights.SemiBold, Width = 56, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _volWarn = new() { Foreground = Brushes.Orange, FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _mediaName = new() { Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly CheckBox _hookOn = new() { Content = "Kirim webhook saat aturan berjalan" };
    private readonly ComboBox _hookMethod = new() { Width = 90 };
    private readonly TextBox _hookUrl = new();
    private readonly TextBox _hookBody = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 80, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBlock _hookStatus = new() { Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap };

    private Action? _closeMedia;

    public RuleEditorWindow(GiftRule source, bool isNew)
    {
        _r = source.Clone();

        Title = isNew ? "Aturan Baru" : "Pengaturan Aturan";
        SourceInitialized += (s, e) => DarkTitleBar.Apply(this);
        Width = 640;
        SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0F, 0x1E));
        Foreground = Brushes.White;

        Build();
        Load();

        Closed += (s, e) => { SoundService.Stop(); _closeMedia?.Invoke(); };
    }

    // ---------- Susunan tampilan ----------

    private static TextBlock Header(string text, Brush accent) => new()
    {
        Text = text, Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 18, 0, 6)
    };

    private static TextBlock Label(string text) => new()
    {
        Text = text, Foreground = Muted, Margin = new Thickness(0, 8, 0, 3)
    };

    private static Button Btn(string text, RoutedEventHandler click)
    {
        var b = new Button { Content = text, Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(6, 0, 0, 0) };
        b.Click += click;
        return b;
    }

    private void Build()
    {
        var root = new StackPanel { Margin = new Thickness(22, 14, 22, 18) };

        root.Children.Add(_enabled);

        // --- Pemicu ---
        root.Children.Add(Header("Pemicu", Cyan));
        _trigger.Items.Add("Gift"); _trigger.Items.Add("Tap-tap"); _trigger.Items.Add("Follow"); _trigger.Items.Add("Share");
        _trigger.SelectionChanged += (s, e) => { if (_trigger.SelectedIndex >= 0) { _r.TriggerIndex = _trigger.SelectedIndex; RefreshTriggerUi(); } };
        root.Children.Add(_trigger);

        // gift
        var giftContent = new DockPanel();
        giftContent.Children.Add(new TextBlock { DockPanel.Dock = Dock.Right, Text = "▾", Foreground = Cyan, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) });
        var giftStack = new StackPanel();
        giftStack.Children.Add(_giftText);
        giftStack.Children.Add(_giftInfo);
        giftContent.Children.Add(giftStack);
        _giftBtn.Content = giftContent;
        _giftBtn.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        _giftBtn.Padding = new Thickness(10, 6, 10, 6);
        _giftBtn.Click += PickGift;
        _giftRow.Children.Add(Label("Gift"));
        _giftRow.Children.Add(_giftBtn);
        root.Children.Add(_giftRow);

        // tap
        _likeRow.Children.Add(new TextBlock { Text = "Jalankan tiap", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 10, 8, 0) });
        _likeRow.Children.Add(_likeCount);
        _likeRow.Children.Add(new TextBlock { Text = "tap", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 10, 0, 0) });
        root.Children.Add(_likeRow);
        _socialInfo.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(_socialInfo);

        // --- Efek ---
        root.Children.Add(Header("Efek", Cyan));
        root.Children.Add(Label("Nama efek (tampil di overlay menu gift; nama sama = digabung)"));
        root.Children.Add(_effect);

        var timing = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var d = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
        d.Children.Add(Label("Durasi (detik)"));
        d.Children.Add(_duration);
        timing.Children.Add(d);
        var rep = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        _repeat.Margin = new Thickness(0, 0, 0, 8);
        rep.Children.Add(_repeat);
        timing.Children.Add(rep);
        root.Children.Add(timing);

        // --- Tombol ---
        root.Children.Add(Header("Tombol keyboard", Cyan));
        var keyRow = new DockPanel();
        var keyBtn = Btn("Atur...", PickKey);
        DockPanel.SetDock(keyBtn, Dock.Right);
        keyRow.Children.Add(keyBtn);
        keyRow.Children.Add(_keyText);
        root.Children.Add(keyRow);
        var holdRow = new StackPanel { Orientation = Orientation.Horizontal };
        holdRow.Children.Add(new TextBlock { Text = "Tahan tombol (ms)", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 8, 8, 0) });
        holdRow.Children.Add(_hold);
        _hold.Margin = new Thickness(0, 8, 0, 0);
        root.Children.Add(holdRow);

        // --- Suara ---
        root.Children.Add(Header("Suara", Cyan));
        var soundRow = new DockPanel();
        var sTest = Btn("Tes", TestSound);
        var sPick = Btn("Pilih...", PickSound);
        var sClear = Btn("Kosongkan", (s, e) => { _r.SoundId = ""; RefreshMedia(); });
        DockPanel.SetDock(sTest, Dock.Right); DockPanel.SetDock(sClear, Dock.Right); DockPanel.SetDock(sPick, Dock.Right);
        soundRow.Children.Add(sTest); soundRow.Children.Add(sClear); soundRow.Children.Add(sPick);
        soundRow.Children.Add(_soundName);
        root.Children.Add(soundRow);

        root.Children.Add(Label("Volume suara ini (1 – 200%)"));
        var volRow = new StackPanel { Orientation = Orientation.Horizontal };
        volRow.Children.Add(_vol);
        volRow.Children.Add(_volLabel);
        var reset = Btn("100%", (s, e) => _vol.Value = 100);
        volRow.Children.Add(reset);
        root.Children.Add(volRow);
        _volWarn.Margin = new Thickness(0, 4, 0, 0);
        root.Children.Add(_volWarn);
        _vol.ValueChanged += (s, e) =>
        {
            int v = (int)_vol.Value;
            _r.SoundVolume = v;
            _volLabel.Text = v + "%";
            _volWarn.Text = v > 100 ? "Di atas 100% suara diperkeras. File yang sudah keras bisa terdengar pecah; turunkan kalau begitu." : "";
        };

        // --- Media ---
        root.Children.Add(Header("Media (GIF / video)", Cyan));
        var mediaRow = new DockPanel();
        var mTest = Btn("Tes", TestMedia);
        var mPick = Btn("Pilih...", PickMedia);
        var mClear = Btn("Kosongkan", (s, e) => { _r.MediaId = ""; RefreshMedia(); });
        DockPanel.SetDock(mTest, Dock.Right); DockPanel.SetDock(mClear, Dock.Right); DockPanel.SetDock(mPick, Dock.Right);
        mediaRow.Children.Add(mTest); mediaRow.Children.Add(mClear); mediaRow.Children.Add(mPick);
        mediaRow.Children.Add(_mediaName);
        root.Children.Add(mediaRow);

        // --- Webhook ---
        root.Children.Add(Header("Webhook", Cyan));
        root.Children.Add(_hookOn);
        root.Children.Add(Label("Metode dan URL"));
        var urlRow = new DockPanel();
        _hookMethod.Items.Add("POST"); _hookMethod.Items.Add("GET");
        DockPanel.SetDock(_hookMethod, Dock.Left);
        _hookMethod.Margin = new Thickness(0, 0, 8, 0);
        urlRow.Children.Add(_hookMethod);
        urlRow.Children.Add(_hookUrl);
        root.Children.Add(urlRow);
        root.Children.Add(Label("Isi (POST). Kata kunci: {user} {gift} {count} {index} {effect}"));
        root.Children.Add(_hookBody);
        var hookTest = Btn("Tes webhook", TestWebhook);
        hookTest.Margin = new Thickness(0, 8, 0, 0);
        hookTest.HorizontalAlignment = HorizontalAlignment.Left;
        root.Children.Add(hookTest);
        _hookStatus.Margin = new Thickness(0, 4, 0, 0);
        root.Children.Add(_hookStatus);

        // --- Tombol bawah ---
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var cancel = new Button { Content = "Batal", Padding = new Thickness(18, 7, 18, 7), IsCancel = true };
        var save = new Button { Content = "Simpan", Padding = new Thickness(22, 7, 22, 7), Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        if (Application.Current.TryFindResource("PrimaryButton") is Style ps) save.Style = ps;
        if (Application.Current.TryFindResource("GhostButton") is Style gs) cancel.Style = gs;
        save.Click += (s, e) => { if (Commit()) DialogResult = true; };
        bottom.Children.Add(cancel);
        bottom.Children.Add(save);
        root.Children.Add(bottom);

        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = root };
    }

    // ---------- Isi awal ----------

    private void Load()
    {
        _enabled.IsChecked = _r.Enabled;
        _trigger.SelectedIndex = _r.TriggerIndex;
        _effect.Text = _r.EffectName;
        _likeCount.Text = _r.LikeCountText;
        _duration.Text = _r.DurationText;
        _repeat.IsChecked = _r.RepeatByCount;
        _hold.Text = _r.HoldMs.ToString();
        _vol.Value = _r.SoundVolume;
        _volLabel.Text = _r.SoundVolume + "%";
        _volWarn.Text = _r.SoundVolume > 100 ? "Di atas 100% suara diperkeras. File yang sudah keras bisa terdengar pecah; turunkan kalau begitu." : "";
        _hookOn.IsChecked = _r.WebhookEnabled;
        _hookMethod.SelectedItem = _r.WebhookMethod;
        _hookUrl.Text = _r.WebhookUrl;
        _hookBody.Text = _r.WebhookBody;
        RefreshTriggerUi();
        RefreshMedia();
        RefreshKey();
        RefreshGift();
    }

    private void RefreshTriggerUi()
    {
        _giftRow.Visibility = _r.IsGift ? Visibility.Visible : Visibility.Collapsed;
        _likeRow.Visibility = _r.IsLike ? Visibility.Visible : Visibility.Collapsed;
        _socialInfo.Visibility = _r.IsFollowOrShare ? Visibility.Visible : Visibility.Collapsed;
        _socialInfo.Text = _r.SocialInfoText;
    }

    private void RefreshGift() { _giftText.Text = _r.GiftDisplay; _giftInfo.Text = _r.GiftInfoText; }
    private void RefreshKey() => _keyText.Text = "Tombol: " + _r.KeyDisplay;
    private void RefreshMedia()
    {
        _soundName.Text = string.IsNullOrEmpty(_r.SoundName) ? "(tanpa suara)" : _r.SoundName;
        _mediaName.Text = string.IsNullOrEmpty(_r.MediaName) ? "(tanpa media)" : _r.MediaName;
    }

    // ---------- Aksi ----------

    private void PickGift(object s, RoutedEventArgs e)
    {
        var dlg = new GiftPickerWindow(_r.GiftId, _r.GiftName) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Selected != null)
        {
            _r.GiftId = dlg.Selected.Id;
            _r.GiftName = dlg.Selected.Name;
            if (string.IsNullOrWhiteSpace(_effect.Text)) _effect.Text = dlg.Selected.Name;
            RefreshGift();
        }
    }

    private void PickKey(object s, RoutedEventArgs e)
    {
        var dlg = new KeyPickerWindow(_r.Key) { Owner = this };
        if (dlg.ShowDialog() == true) { _r.Key = dlg.Result; RefreshKey(); }
    }

    private void PickSound(object s, RoutedEventArgs e)
    {
        var dlg = new LibraryPickerWindow(AppData.KindSound, _r.SoundId) { Owner = this };
        if (dlg.ShowDialog() == true) { _r.SoundId = dlg.SelectedId; RefreshMedia(); }
    }

    private void PickMedia(object s, RoutedEventArgs e)
    {
        var dlg = new LibraryPickerWindow(AppData.KindMedia, _r.MediaId) { Owner = this };
        if (dlg.ShowDialog() == true) { _r.MediaId = dlg.SelectedId; RefreshMedia(); }
    }

    private void TestSound(object s, RoutedEventArgs e)
    {
        var path = _r.ResolvedSoundPath;
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            MessageBox.Show(this, "Belum ada file suara yang dipilih.");
            return;
        }
        SoundService.Play(path, _r.SoundVolume);
    }

    private void TestMedia(object s, RoutedEventArgs e)
    {
        var path = _r.ResolvedMediaPath;
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            MessageBox.Show(this, "Belum ada file media yang dipilih.");
            return;
        }
        _closeMedia?.Invoke();
        _closeMedia = MediaPopup.Show(path, TimeSpan.FromSeconds(5), Math.Min(1.0, _r.SoundVolume / 100.0));
    }

    private async void TestWebhook(object s, RoutedEventArgs e)
    {
        string method = _hookMethod.SelectedItem as string ?? "POST";
        _hookStatus.Foreground = Muted;
        _hookStatus.Text = "Mengirim...";
        try
        {
            var vars = new Dictionary<string, string>
            {
                ["user"] = "Budi", ["gift"] = _r.GiftName, ["count"] = "1", ["index"] = "1",
                ["effect"] = _effect.Text
            };
            string status = await WebhookService.SendAsync(_hookUrl.Text, method, _hookBody.Text, vars);
            _hookStatus.Foreground = Brushes.LightGreen;
            _hookStatus.Text = "Berhasil: " + status;
        }
        catch (Exception ex)
        {
            _hookStatus.Foreground = Brushes.Orange;
            _hookStatus.Text = "Gagal: " + ex.Message;
        }
    }

    // ---------- Simpan ----------

    private bool Commit()
    {
        if (!double.TryParse(_duration.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var dur) || dur <= 0)
        {
            MessageBox.Show(this, "Durasi harus berupa angka lebih dari 0.");
            return false;
        }
        if (!int.TryParse(_hold.Text.Trim(), out var hold) || hold < 0)
        {
            MessageBox.Show(this, "Lama tahan tombol harus berupa angka (milidetik).");
            return false;
        }
        if (_hookOn.IsChecked == true)
        {
            if (!Uri.TryCreate(_hookUrl.Text.Trim(), UriKind.Absolute, out var u) ||
                (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
            {
                MessageBox.Show(this, "URL webhook harus diawali http:// atau https://");
                return false;
            }
        }

        _r.Enabled = _enabled.IsChecked == true;
        _r.EffectName = _effect.Text.Trim();
        if (_r.IsLike) _r.LikeCountText = _likeCount.Text;
        _r.DurationText = _duration.Text;
        _r.RepeatByCount = _repeat.IsChecked == true;
        _r.HoldMs = hold;
        _r.WebhookEnabled = _hookOn.IsChecked == true;
        _r.WebhookMethod = _hookMethod.SelectedItem as string ?? "POST";
        _r.WebhookUrl = _hookUrl.Text.Trim();
        _r.WebhookBody = _hookBody.Text;
        return true;
    }
}
