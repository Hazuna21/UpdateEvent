using System.Windows;
using System.Windows.Media.Animation;

namespace HazunaV2;

// Semua overlay (Top Gifters, Menu Gift, Skor) dibuat sekali saat aplikasi dibuka dan jendelanya selalu ada,
// jadi bisa dipilih di TikTok Studio kapan saja. Tombol hanya menampilkan / menyembunyikan isinya.
public partial class MainWindow
{
    private readonly HashSet<Window> _overlayShown = new();

    private OverlayWindow EnsureOverlay()
    {
        if (_overlay != null) return _overlay;

        var o = new OverlayWindow(_board);
        o.SetChromaKey(ChromaCheck.IsChecked == true);
        o.Opacity = 0;
        o.Closed += (s, a) => { _overlayShown.Remove(o); _overlay = null; UpdateAllOverlayBtn(); };
        _overlay = o;
        o.Show();
        return o;
    }

    private GiftMenuOverlay EnsureMenuOverlay()
    {
        if (_menuOverlay != null) return _menuOverlay;

        var o = new GiftMenuOverlay(_rules, _settings.MenuOverlayScale) { ExtraRules = WheelMenuRules };
        o.SetChromaKey(ChromaCheck.IsChecked == true);
        o.Opacity = 0;
        o.Closed += (s, a) => { _overlayShown.Remove(o); _menuOverlay = null; UpdateAllOverlayBtn(); };
        _menuOverlay = o;
        o.Show();
        return o;
    }

    private ScoreOverlayWindow EnsureScoreOverlay()
    {
        if (_scoreOverlay != null) return _scoreOverlay;

        var o = new ScoreOverlayWindow(_score);
        o.SetChromaKey(ChromaCheck.IsChecked == true);
        o.Opacity = 0;
        o.Closed += (s, a) => { _overlayShown.Remove(o); _scoreOverlay = null; UpdateAllOverlayBtn(); };
        _scoreOverlay = o;
        o.Show();
        return o;
    }

    // Ganti tema: overlay dibuat ulang dengan warna baru (posisi dan keadaan tampil dipertahankan)
    private void Theme_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_settingsReady || ThemeCombo.SelectedItem is not string name || name == OverlayTheme.Current.Name) return;

        _settings.OverlayTheme = name;
        SettingsStore.Save(_settings);
        OverlayTheme.Set(name);
        GiftMenuOverlay.ReloadTheme();

        var keep = new List<(Window Win, double L, double T, bool Shown, int Kind)>();
        if (_overlay != null) keep.Add((_overlay, _overlay.Left, _overlay.Top, _overlayShown.Contains(_overlay), 0));
        if (_menuOverlay != null) keep.Add((_menuOverlay, _menuOverlay.Left, _menuOverlay.Top, _overlayShown.Contains(_menuOverlay), 1));
        if (_scoreOverlay != null) keep.Add((_scoreOverlay, _scoreOverlay.Left, _scoreOverlay.Top, _overlayShown.Contains(_scoreOverlay), 2));

        foreach (var k in keep) k.Win.Close();

        foreach (var k in keep)
        {
            Window w = k.Kind switch { 0 => EnsureOverlay(), 1 => EnsureMenuOverlay(), _ => EnsureScoreOverlay() };
            w.Left = k.L;
            w.Top = k.T;
            if (k.Shown) SetOverlayVisible(w, true);
        }

        // Roda: dibuat ulang kalau tidak sedang berputar (kalau sedang berputar, tema berlaku di pembukaan berikutnya)
        if (_wheelWin != null && !_wheelBusy)
        {
            bool preview = _wheelPreview;
            _wheelWin.Close();
            EnsureWheelWindow();
            _wheelPreview = preview;
            if (preview) _wheelWin?.Reveal();
            SettleWheel();
            UpdateWheelOverlayBtn();
        }
        UpdateAllOverlayBtn();
    }

    private void EnsureAllOverlays()
    {
        EnsureOverlay();
        EnsureMenuOverlay();
        EnsureScoreOverlay();
        UpdateAllOverlayBtn();
    }

    private void SetOverlayVisible(Window w, bool visible)
    {
        if (visible) _overlayShown.Add(w); else _overlayShown.Remove(w);
        w.BeginAnimation(OpacityProperty, new DoubleAnimation(w.Opacity, visible ? 1 : 0, TimeSpan.FromMilliseconds(250)));
        UpdateAllOverlayBtn();
    }

    // Satu tombol untuk semua overlay: kalau ada yang terlihat, sembunyikan semua; kalau tidak, tampilkan semua
    private void ToggleAllOverlays_Click(object sender, RoutedEventArgs e)
    {
        if (_overlayShown.Count > 0)
        {
            foreach (var w in _overlayShown.ToList())
                SetOverlayVisible(w, false);
        }
        else
        {
            SetOverlayVisible(EnsureOverlay(), true);
            SetOverlayVisible(EnsureMenuOverlay(), true);
            SetOverlayVisible(EnsureScoreOverlay(), true);
        }
    }

    private void UpdateAllOverlayBtn()
    {
        OverlayToggleBtn.Content = _overlay != null && _overlayShown.Contains(_overlay) ? "Sembunyikan Overlay" : "Tampilkan Overlay";
        MenuOverlayBtn.Content = _menuOverlay != null && _overlayShown.Contains(_menuOverlay) ? "Sembunyikan Overlay Menu Gift" : "Tampilkan Overlay Menu Gift";
        ScoreOverlayBtn.Content = _scoreOverlay != null && _overlayShown.Contains(_scoreOverlay) ? "Sembunyikan Overlay Skor" : "Tampilkan Overlay Skor";
        AllOverlayBtn.Content = _overlayShown.Count > 0 ? "Sembunyikan Semua Overlay" : "Tampilkan Semua Overlay";
    }
}
