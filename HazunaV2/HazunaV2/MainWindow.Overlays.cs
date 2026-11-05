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
