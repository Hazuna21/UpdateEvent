using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HazunaV2;

// Overlay "menu gift": kartu berisi judul (gift / tap-tap / follow / share), gambar, dan nama efek.
// Maksimal 15 kartu per halaman (5 kolom x 3 baris). Kalau lebih, halaman bergantian tiap 30 detik.
public class GiftMenuOverlay : Window
{
    private const int PerPage = 15;
    private const int Columns = 5;
    private const double CardW = 190, CardH = 240;
    private static readonly TimeSpan PageInterval = TimeSpan.FromSeconds(30);

    private static readonly Brush Line = Brushes.Black;
    private static readonly FontFamily CardFont = new("Segoe Print, Segoe UI, Arial");

    private static readonly Dictionary<string, ImageSource> ImageCache = new();

    private readonly ObservableCollection<GiftRule> _rules;
    private readonly Border _strip = new();
    private readonly TextBlock _pageText = new();
    private readonly DispatcherTimer _pageTimer = new() { Interval = PageInterval };
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };

    private int _page;

    public GiftMenuOverlay(ObservableCollection<GiftRule> rules)
    {
        _rules = rules;

        Title = "Menu Gift Overlay";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Left = 40;
        Top = 40;

        _strip.BorderBrush = Line;
        _strip.BorderThickness = new Thickness(0, 0, 3, 3);

        _pageText.Foreground = Brushes.White;
        _pageText.FontWeight = FontWeights.Bold;
        _pageText.FontSize = 18;
        _pageText.HorizontalAlignment = HorizontalAlignment.Right;
        _pageText.Margin = new Thickness(0, 6, 4, 0);
        _pageText.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.9 };

        var stack = new StackPanel { Margin = new Thickness(8) };
        stack.Children.Add(_strip);
        stack.Children.Add(_pageText);
        Content = new Viewbox { Stretch = Stretch.Uniform, Child = stack };

        // Geser jendela dengan menahan klik kiri
        MouseLeftButtonDown += (s, e) =>
        {
            if (e.OriginalSource is not ResizeGrip) DragMove();
        };

        // Ikuti perubahan aturan (tambah/hapus/ubah) dengan jeda singkat
        _rules.CollectionChanged += OnRulesChanged;
        foreach (var r in _rules) r.PropertyChanged += OnRuleProperty;

        _refreshTimer.Tick += (s, e) => { _refreshTimer.Stop(); Rebuild(false); };
        _pageTimer.Tick += (s, e) => Rebuild(true);

        Closed += (s, e) =>
        {
            _pageTimer.Stop();
            _refreshTimer.Stop();
            _rules.CollectionChanged -= OnRulesChanged;
            foreach (var r in _rules) r.PropertyChanged -= OnRuleProperty;
        };

        var rows = Math.Max(1, (int)Math.Ceiling(Math.Min(Visible().Count, PerPage) / (double)Columns));
        Width = Columns * CardW * 0.9 + 40;
        Height = rows * CardH * 0.9 + 70;

        Rebuild(false);
        _pageTimer.Start();
    }

    public void SetChromaKey(bool on)
    {
        Background = on ? new SolidColorBrush(Color.FromRgb(0, 255, 0)) : Brushes.Transparent;
    }

    private void OnRulesChanged(object? s, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null) foreach (GiftRule r in e.OldItems) r.PropertyChanged -= OnRuleProperty;
        if (e.NewItems != null) foreach (GiftRule r in e.NewItems) r.PropertyChanged += OnRuleProperty;
        ScheduleRefresh();
    }

    private void OnRuleProperty(object? s, PropertyChangedEventArgs e) => ScheduleRefresh();

    private void ScheduleRefresh()
    {
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    // Aturan yang tampil: aktif dan punya nama efek
    private List<GiftRule> Visible() =>
        _rules.Where(r => r.Enabled && !string.IsNullOrWhiteSpace(r.EffectName)).ToList();

    private void Rebuild(bool nextPage)
    {
        var items = Visible();
        int pages = Math.Max(1, (int)Math.Ceiling(items.Count / (double)PerPage));

        if (nextPage && pages > 1) _page = (_page + 1) % pages;
        if (_page >= pages) _page = 0;

        var pageItems = items.Skip(_page * PerPage).Take(PerPage).ToList();

        var grid = new UniformGrid { Columns = Columns };
        if (pageItems.Count == 0)
        {
            _strip.Child = new Border
            {
                Width = CardW * 3, Height = 110,
                BorderBrush = Line, BorderThickness = new Thickness(3, 3, 0, 0),
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xD6, 0xE8)),
                Child = Label("Belum ada efek.\nIsi kolom \"Efek (overlay)\" di Gift Rules.", 16, FontWeights.Bold)
            };
        }
        else
        {
            for (int i = 0; i < pageItems.Count; i++)
                grid.Children.Add(BuildCard(pageItems[i], i));
            _strip.Child = grid;
        }

        _pageText.Text = pages > 1 ? $"Halaman {_page + 1}/{pages}" : "";
        _pageText.Visibility = pages > 1 ? Visibility.Visible : Visibility.Collapsed;

        if (nextPage)
            _strip.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)));
    }

    private static readonly Color[] Tints =
    {
        Color.FromRgb(0xFF, 0xD3, 0xDC), Color.FromRgb(0xFF, 0xC9, 0xE0), Color.FromRgb(0xF6, 0xBF, 0xF2)
    };

    private Border BuildCard(GiftRule rule, int index)
    {
        var tint = Tints[index % Tints.Length];

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(58) });

        var title = Label(rule.OverlayTitle.ToUpperInvariant(), 21, FontWeights.Bold);
        var icon = BuildIcon(rule);
        var effect = Label(rule.EffectName.Trim().ToUpperInvariant(), 21, FontWeights.Bold);

        Grid.SetRow(title, 0);
        Grid.SetRow(icon, 1);
        Grid.SetRow(effect, 2);
        grid.Children.Add(title);
        grid.Children.Add(icon);
        grid.Children.Add(effect);

        return new Border
        {
            Width = CardW,
            Height = CardH,
            BorderBrush = Line,
            BorderThickness = new Thickness(3, 3, 0, 0),
            Background = new SolidColorBrush(Color.FromArgb(0xE6, tint.R, tint.G, tint.B)),
            Child = grid
        };
    }

    private static TextBlock Label(string text, double size, FontWeight weight) => new()
    {
        Text = text,
        FontFamily = CardFont,
        FontSize = size,
        FontWeight = weight,
        Foreground = Brushes.Black,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(6, 0, 6, 0)
    };

    private static FrameworkElement BuildIcon(GiftRule rule)
    {
        string glyph;
        Brush color = Brushes.Black;

        switch (rule.Trigger)
        {
            case RuleTrigger.Like: glyph = "❤"; color = new SolidColorBrush(Color.FromRgb(0xE5, 0x20, 0x2A)); break;
            case RuleTrigger.Follow: glyph = "\U0001F464"; break;
            case RuleTrigger.Share: glyph = "\U0001F517"; break;
            default:
                var known = rule.GiftId != 0 ? GiftCatalog.FindById(rule.GiftId) : GiftCatalog.FindByName(rule.GiftName);
                var img = LoadImage(known?.ImageUrl);
                if (img != null)
                    return new Image { Source = img, Stretch = Stretch.Uniform, Margin = new Thickness(10, 4, 10, 4) };
                glyph = "\U0001F381";
                break;
        }

        return new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe UI Emoji"),
            FontSize = 84,
            Foreground = color,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
    }

    private static ImageSource? LoadImage(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (ImageCache.TryGetValue(url, out var cached)) return cached;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(url);
            bmp.DecodePixelWidth = 220;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            ImageCache[url] = bmp;
            return bmp;
        }
        catch
        {
            return null;
        }
    }
}
