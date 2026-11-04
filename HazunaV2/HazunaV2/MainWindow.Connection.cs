using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HazunaV2;

public partial class MainWindow
{
    private TikTokConnector _connector = null!;
    private LiveState _liveState = LiveState.Idle;
    private int _liveGiftCount;

    private void InitConnection()
    {
        _connector = new TikTokConnector();

        // Event dari pustaka datang dari thread lain: pindahkan ke thread tampilan
        _connector.Log += msg => Dispatcher.InvokeAsync(() => AddLog("[TikTok] " + msg));
        _connector.StateChanged += (state, msg) => Dispatcher.InvokeAsync(() => OnLiveState(state, msg));
        _connector.GiftReceived += gift => Dispatcher.InvokeAsync(() => OnLiveGift(gift));
        _connector.SocialReceived += s => Dispatcher.InvokeAsync(() =>
        {
            if (s.Kind == RuleTrigger.Join) OnViewerJoin(s.UserName, 0);
            else _engine.ProcessSocial(s.Kind, s.UserName, s.Count);
        });
        _connector.ChatReceived += chat => Dispatcher.InvokeAsync(() => OnLiveChat(chat));
        _connector.ChatReceived += chat => Dispatcher.InvokeAsync(() => OnChatForAi(chat));
        _connector.CatalogLoaded += list => Dispatcher.InvokeAsync(() => OnCatalog(list));

        ApplyLiveStateUi(LiveState.Idle, "TikTok: belum terhubung");
    }

    private void ToggleConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_connector.IsRunning)
        {
            _connector.Disconnect();
            return;
        }

        var user = _settings.TikTokUsername;
        if (string.IsNullOrWhiteSpace(user))
        {
            MessageBox.Show("Isi dulu nama akun TikTok di Settings, lalu klik Simpan Pengaturan.");
            return;
        }

        _liveGiftCount = 0;
        _joinSeen.Clear();
        _connector.Connect(user);
    }

        // Mengambil katalog gift dari web (API TikTools)
    private async void FetchWebCatalog_Click(object sender, RoutedEventArgs e)
    {
        var key = _settings.TikToolsApiKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            MessageBox.Show("Isi dulu API key TikTools di Settings, lalu klik Simpan Pengaturan.");
            return;
        }

        AddLog("[Web] Mengambil katalog gift dari TikTools...");
        var (list, error) = await TikToolsClient.FetchGiftsAsync(key);

        if (list == null)
        {
            AddLog("[Web] Gagal: " + error);
            MessageBox.Show("Gagal mengambil katalog:\n" + error);
            return;
        }

        if (list.Count == 0)
        {
            AddLog("[Web] Respons diterima tapi daftar gift kosong.");
            MessageBox.Show("Respons diterima, tapi daftar gift kosong.");
            return;
        }

        OnCatalog(list);
        MessageBox.Show($"{list.Count} gift diterima dari web.");
    }

    private void OnLiveState(LiveState state, string message)
    {
        ApplyLiveStateUi(state, message);
        AddLog("[TikTok] " + message.Replace("TikTok: ", ""));
    }

        private void ApplyLiveStateUi(LiveState state, string message)
    {
        _liveState = state;
        StatusText.Text = message;

        string color = state switch
        {
            LiveState.Connected => "#34D399",
            LiveState.Connecting => "#60A5FA",
            LiveState.Error => "#FB7185",
            _ => "#FBBF24"
        };
        StatusDot.Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

        ConnectBtn.Content = _connector.IsRunning ? "Putuskan" : "Hubungkan";
        ConnectBtn.Style = (Style)FindResource(_connector.IsRunning ? "DangerButton" : "GoButton");

        // Titik berdenyut saat terhubung atau sedang menghubungkan
        if (state == LiveState.Connected || state == LiveState.Connecting)
        {
            var pulse = new DoubleAnimation(1, 0.3,
                TimeSpan.FromMilliseconds(state == LiveState.Connected ? 1100 : 450))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            StatusDot.BeginAnimation(UIElement.OpacityProperty, pulse);
        }
        else
        {
            StatusDot.BeginAnimation(UIElement.OpacityProperty, null);
            StatusDot.Opacity = 1;
        }

        if (_currentPage == "Dashboard")
            RefreshDashboard();
    }

    private void OnLiveGift(LiveGift g)
    {
        _liveGiftCount++;
        GiftCatalog.Observe(g);
        _engine.Process(new GiftEvent(g.UserName, g.GiftName, g.Count, g.DiamondsEach, 0, g.GiftId));
        _giftsProcessed += g.Count;

        if (_currentPage == "Dashboard")
            RefreshDashboard();
    }

    // Katalog gift dari pustaka TikTok
    private void OnCatalog(List<LiveGiftInfo> list)
    {
        var (added, updated) = GiftCatalog.ImportCatalog(list);
        AddLog($"[TikTok] Katalog gift dimuat: {list.Count} gift ({added} baru, {updated} diperbarui)");

        // Aturan lama yang cocok lewat nama langsung diikat ke ID
        int bound = 0;
        foreach (var rule in _rules)
        {
            if (rule.GiftId != 0 || string.IsNullOrWhiteSpace(rule.GiftName)) continue;

            var known = GiftCatalog.FindByName(rule.GiftName);
            if (known != null && known.Id != 0)
            {
                rule.GiftId = known.Id;
                bound++;
            }
        }
        if (bound > 0)
        {
            RuleStore.Save(_rules);
            AddLog($"[TikTok] {bound} aturan terhubung ke ID gift dari katalog.");
        }

        foreach (var r in _rules)
        r.RefreshGiftInfo();

        if (_currentPage == "Dashboard")
            RefreshDashboard();
    }

        // Memuat katalog gift tanpa harus sedang LIVE
        // Impor daftar gift dengan menempel teks (tidak perlu LIVE)
    private void LoadCatalog_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new GiftImportWindow { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            AddLog($"[Daftar gift] Impor: {dlg.Added} gift baru, {dlg.Updated} diperbarui.");
            MessageBox.Show($"{dlg.Added} gift baru ditambahkan, {dlg.Updated} diperbarui.");

            if (_currentPage == "Dashboard")
                RefreshDashboard();
        }
    }

    private string LiveStatusShort() => _liveState switch
    {
        LiveState.Connecting => "TikTok: menghubungkan...",
        LiveState.Connected => $"TikTok: terhubung ({_liveGiftCount} gift diterima)",
        LiveState.Disconnected => "TikTok: terputus",
        LiveState.Error => "TikTok: ada kesalahan koneksi",
        _ => "TikTok: belum terhubung"
    };
}