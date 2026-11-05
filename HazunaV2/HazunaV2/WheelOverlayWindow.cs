using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace HazunaV2;

// Overlay roda yang selalu tampil. Sektornya sama besar; peluang ditentukan oleh bobot (di luar jendela ini).
public class WheelOverlayWindow : Window
{
    private const double Size = 360;
    private const double R = 168;
    private const double C = Size / 2;

    private static readonly Color[] Palette =
    {
        Color.FromRgb(0x7C, 0x3A, 0xED), Color.FromRgb(0x0E, 0x9F, 0xBF), Color.FromRgb(0xDB, 0x27, 0x77),
        Color.FromRgb(0xD9, 0x77, 0x06), Color.FromRgb(0x05, 0x96, 0x69), Color.FromRgb(0x4F, 0x46, 0xE5),
        Color.FromRgb(0xE1, 0x1D, 0x48), Color.FromRgb(0x25, 0x63, 0xEB)
    };

    private readonly WheelConfig _cfg;
    private readonly Canvas _wheel = new() { Width = Size, Height = Size };
    private readonly RotateTransform _rot = new(0, C, C);
    private readonly TextBlock _title = new();
    private readonly TextBlock _sub = new();
    private readonly TextBlock _result = new();
    private int[] _order = Array.Empty<int>();      // urutan tampil: posisi sektor -> nomor isi
    private double _angle;
    private bool _spinning;

    public bool IsSpinning => _spinning;

    public WheelOverlayWindow(WheelConfig cfg)
    {
        _cfg = cfg;

        Title = "Lucky Wheel Overlay";
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Left = cfg.PosX ?? 40;
        Top = cfg.PosY ?? 120;
        Opacity = 0;

        _title.Text = cfg.Name.ToUpperInvariant();
        _title.Foreground = Brushes.White;
        _title.FontSize = 26;
        _title.FontWeight = FontWeights.Black;
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _title.Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 1, Opacity = 0.9 };

        _sub.Foreground = new SolidColorBrush(Color.FromRgb(0xC4, 0xB5, 0xFD));
        _sub.FontSize = 14;
        _sub.FontWeight = FontWeights.SemiBold;
        _sub.HorizontalAlignment = HorizontalAlignment.Center;
        _sub.Margin = new Thickness(0, 0, 0, 8);
        _sub.Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.9 };

        _result.Foreground = Brushes.White;
        _result.FontSize = 22;
        _result.FontWeight = FontWeights.Black;
        _result.TextAlignment = TextAlignment.Center;
        _result.TextWrapping = TextWrapping.Wrap;
        _result.MaxWidth = Size;
        _result.MinHeight = 62;
        _result.Margin = new Thickness(0, 10, 0, 0);
        _result.HorizontalAlignment = HorizontalAlignment.Center;
        _result.Effect = new DropShadowEffect { Color = Color.FromRgb(0x8B, 0x5C, 0xF6), BlurRadius = 14, ShadowDepth = 0, Opacity = 1 };

        _wheel.RenderTransform = _rot;
        _wheel.CacheMode = new BitmapCache();

        // Wadah roda: roda yang berputar + hiasan tetap (bingkai, poros, penunjuk)
        var holder = new Grid { Width = Size, Height = Size, HorizontalAlignment = HorizontalAlignment.Center };
        holder.Children.Add(new Ellipse
        {
            Width = Size - 2, Height = Size - 2, Fill = new SolidColorBrush(Color.FromRgb(0x14, 0x0B, 0x2E)),
            Effect = new DropShadowEffect { Color = Color.FromRgb(0x8B, 0x5C, 0xF6), BlurRadius = 22, ShadowDepth = 0, Opacity = 0.8 }
        });
        holder.Children.Add(_wheel);
        holder.Children.Add(new Ellipse
        {
            Width = 2 * R + 8, Height = 2 * R + 8, Stroke = new SolidColorBrush(Color.FromRgb(0xC4, 0xB5, 0xFD)),
            StrokeThickness = 4, IsHitTestVisible = false
        });
        holder.Children.Add(new Ellipse
        {
            Width = 44, Height = 44, Fill = new SolidColorBrush(Color.FromRgb(0x1B, 0x10, 0x3A)),
            Stroke = Brushes.White, StrokeThickness = 3
        });
        holder.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M0,0 L28,0 L14,34 Z"),
            Fill = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)),
            Stroke = Brushes.White, StrokeThickness = 2,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, -6, 0, 0),
            Effect = new DropShadowEffect { BlurRadius = 6, ShadowDepth = 2, Opacity = 0.7 }
        });

        var root = new StackPanel { Margin = new Thickness(16) };
        root.Children.Add(_title);
        root.Children.Add(_sub);
        root.Children.Add(holder);
        root.Children.Add(_result);
        Content = root;

        MouseLeftButtonDown += (s, e) => DragMove();

        // Muncul perlahan; posisi terakhir disimpan saat ditutup
        Loaded += (s, e) => BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
        Closed += (s, e) => { _cfg.PosX = Left; _cfg.PosY = Top; };

        Rebuild();
    }

    // Menghilang perlahan lalu menutup jendela
    public void FadeOutAndClose()
    {
        var fade = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(350));
        fade.Completed += (s, e) => Close();
        BeginAnimation(OpacityProperty, fade);
    }

    public void SetChromaKey(bool on)
    {
        Background = on ? new SolidColorBrush(Color.FromRgb(0, 255, 0)) : Brushes.Transparent;
    }

    // Gambar ulang roda dari isi terbaru (dipanggil saat isi diubah)
    public void Rebuild()
    {
        _wheel.Children.Clear();
        _title.Text = _cfg.Name.ToUpperInvariant();
        _sub.Text = string.IsNullOrWhiteSpace(_cfg.GiftName) ? "" : "Kirim " + _cfg.GiftName + " untuk memutar";

        int n = _cfg.Entries.Count;
        if (n == 0)
        {
            _wheel.Children.Add(new Ellipse { Width = 2 * R, Height = 2 * R, Fill = new SolidColorBrush(Color.FromRgb(0x3B, 0x2A, 0x6B)) });
            Canvas.SetLeft(_wheel.Children[0], C - R);
            Canvas.SetTop(_wheel.Children[0], C - R);
            AddLabel("Isi roda dulu", C - 90, C - 14, 180, 28, 0, 18);
            return;
        }

        if (_order.Length != n)
            _order = Enumerable.Range(0, n).ToArray();

        double step = 360.0 / n;
        double fontSize = n > 16 ? 11 : n > 10 ? 13 : 16;

        for (int i = 0; i < n; i++)
        {
            var color = Palette[i % Palette.Length];
            if (n > 1 && n % Palette.Length == 1 && i == n - 1)
                color = Palette[(i + 3) % Palette.Length];      // sektor terakhir tidak sewarna dengan sektor pertama

            Shape sector;
            if (n == 1)
            {
                sector = new Ellipse { Width = 2 * R, Height = 2 * R };
                Canvas.SetLeft(sector, C - R);
                Canvas.SetTop(sector, C - R);
            }
            else
            {
                sector = new System.Windows.Shapes.Path { Data = SectorGeometry(i * step, (i + 1) * step) };
            }
            sector.Fill = new SolidColorBrush(color);
            sector.Stroke = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255));
            sector.StrokeThickness = 1.5;
            _wheel.Children.Add(sector);

            // Teks sepanjang jari-jari, rata kanan di dekat tepi
            double mid = (i + 0.5) * step;
            AddLabel(_cfg.Entries[_order[i]].Text, C + 36, C - 14, R - 48, 28, mid - 90, fontSize);
        }
    }

    // Acak posisi isi di roda (daftar isi di pengaturan tidak berubah)
    public void Shuffle()
    {
        int n = _cfg.Entries.Count;
        if (n < 2 || _spinning) return;

        _order = Enumerable.Range(0, n).ToArray();
        for (int i = n - 1; i > 0; i--)
        {
            int j = Random.Shared.Next(i + 1);
            (_order[i], _order[j]) = (_order[j], _order[i]);
        }
        Rebuild();
    }

    private static Point OnCircle(double deg)
    {
        double a = deg * Math.PI / 180.0;       // 0 = atas, searah jarum jam
        return new Point(C + R * Math.Sin(a), C - R * Math.Cos(a));
    }

    private static Geometry SectorGeometry(double fromDeg, double toDeg)
    {
        var fig = new PathFigure { StartPoint = new Point(C, C), IsClosed = true };
        fig.Segments.Add(new LineSegment(OnCircle(fromDeg), true));
        fig.Segments.Add(new ArcSegment(OnCircle(toDeg), new Size(R, R), 0, toDeg - fromDeg > 180, SweepDirection.Clockwise, true));
        var g = new PathGeometry();
        g.Figures.Add(fig);
        g.Freeze();
        return g;
    }

    private void AddLabel(string text, double left, double top, double width, double height, double rotateDeg, double fontSize)
    {
        var tb = new TextBlock
        {
            Text = text, Width = width, Height = height, FontSize = fontSize, FontWeight = FontWeights.Black,
            Foreground = Brushes.White, TextAlignment = TextAlignment.Right, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Effect = new DropShadowEffect { BlurRadius = 3, ShadowDepth = 1, Opacity = 0.9 },
            RenderTransform = new RotateTransform(rotateDeg, C - left, C - top)
        };
        Canvas.SetLeft(tb, left);
        Canvas.SetTop(tb, top);
        _wheel.Children.Add(tb);
    }

    // Putar sampai sektor `index` tepat di bawah penunjuk (atas). onDone dipanggil setelah berhenti.
    public void Spin(int index, double seconds, string user, Action onDone)
    {
        int n = _cfg.Entries.Count;
        if (n == 0 || index < 0 || index >= n)
        {
            onDone();
            return;
        }

        _spinning = true;
        _result.Text = user + " memutar roda...";

        if (_order.Length != n)
            _order = Enumerable.Range(0, n).ToArray();
        index = Array.IndexOf(_order, index);       // nomor isi -> posisi sektornya di roda

        double step = 360.0 / n;
        double jitter = (Random.Shared.NextDouble() - 0.5) * step * 0.6;      // berhenti tidak selalu di tengah sektor
        double target = -((index + 0.5) * step) + jitter;
        double final = target + 360.0 * Math.Ceiling((_angle + 360.0 * 5 - target) / 360.0);

        var anim = new DoubleAnimation(_angle, final, TimeSpan.FromSeconds(seconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        anim.Completed += (s, e) =>
        {
            _angle = final % 360.0;
            _rot.Angle = _angle;
            _spinning = false;
            onDone();
        };
        _rot.BeginAnimation(RotateTransform.AngleProperty, anim);
    }

    public void ShowResult(string user, string text, int scoreDelta)
    {
        _result.Text = text;      // hanya isi roda yang menang

        var scale = new ScaleTransform(1, 1);
        _result.RenderTransformOrigin = new Point(0.5, 0.5);
        _result.RenderTransform = scale;
        var grow = new DoubleAnimation(1.25, 1.0, TimeSpan.FromMilliseconds(500))
        {
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    public void ClearResult() => _result.Text = "";
}
