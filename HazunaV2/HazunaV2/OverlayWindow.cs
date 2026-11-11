using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace HazunaV2;

public class OverlayWindow : Window
{
    private readonly Leaderboard _board;
    private readonly StackPanel _list = new();
    private readonly TextBlock _header = new();

    public OverlayWindow(Leaderboard board)
    {
        _board = board;

        Title = "Top Gifters Overlay";
        Width = 340;
        SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        OverlayGuard.Apply(this);
        ShowActivated = false;
        Left = 40;
        Top = 40;

        _header.Foreground = Brushes.White;
        _header.FontSize = 16;
        _header.FontWeight = FontWeights.Bold;
        _header.Margin = new Thickness(4, 0, 0, 8);
        _header.Effect = new DropShadowEffect { BlurRadius = 4, ShadowDepth = 1, Opacity = 0.9 };

        var root = new StackPanel { Margin = new Thickness(8) };
        root.Children.Add(_header);
        root.Children.Add(_list);
        Content = root;

        // Geser jendela dengan menahan klik kiri
        MouseLeftButtonDown += (s, e) => DragMove();

        NotifyCollectionChangedEventHandler handler = (s, e) => Refresh();
        _board.Top.CollectionChanged += handler;
        Closed += (s, e) => _board.Top.CollectionChanged -= handler;

        Refresh();
    }

    public void SetChromaKey(bool on)
    {
        Background = on
            ? new SolidColorBrush(Color.FromRgb(0, 255, 0))
            : Brushes.Transparent;
    }

    public void Refresh()
    {
        _header.Text = "TOP GIFTERS · " + (_board.SortByDiamonds ? "Diamonds" : "Jumlah Gift");
        _list.Children.Clear();

        if (_board.Top.Count == 0)
        {
            _list.Children.Add(new TextBlock
            {
                Text = "Belum ada gift",
                Foreground = Brushes.White,
                Opacity = 0.8,
                Margin = new Thickness(4)
            });
            return;
        }

        foreach (var entry in _board.Top)
            _list.Children.Add(BuildRow(entry));
    }

    private Border BuildRow(GifterEntry entry)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Brush rankBrush = entry.Rank switch
        {
            1 => new SolidColorBrush(Color.FromRgb(0xFF, 0xD5, 0x4F)),
            2 => new SolidColorBrush(Color.FromRgb(0xCF, 0xD8, 0xDC)),
            3 => new SolidColorBrush(Color.FromRgb(0xFF, 0xAB, 0x76)),
            _ => Brushes.White
        };

        var rank = new TextBlock
        {
            Text = entry.Rank.ToString(),
            Foreground = rankBrush,
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetColumn(rank, 0);

        var avatar = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(17),
            Background = new SolidColorBrush(OverlayTheme.Rgb(0x7C, 0x3A, 0xED)),
            Child = new TextBlock
            {
                Text = entry.Initial,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            }
        };
        Grid.SetColumn(avatar, 1);

        var name = new TextBlock
        {
            Text = entry.UserName,
            Foreground = Brushes.White,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(8, 0, 8, 0)
        };
        Grid.SetColumn(name, 2);

        var value = new TextBlock
        {
            Text = _board.SortByDiamonds ? entry.Diamonds.ToString("N0") : entry.GiftCount.ToString("N0"),
            Foreground = rankBrush,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(value, 3);

        grid.Children.Add(rank);
        grid.Children.Add(avatar);
        grid.Children.Add(name);
        grid.Children.Add(value);

        return new Border
        {
            Background = new SolidColorBrush(OverlayTheme.Argb(0xCC, 0x0B, 0x0F, 0x1E)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 6),
            Child = grid
        };
    }
}