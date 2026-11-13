using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HazunaV2;

/// <summary>Daftar pembaruan aplikasi: versi terbaru di atas, lengkap dengan tanggal dan isinya.</summary>
public class HistoryWindow : Window
{
    static Brush B(string key, Brush fallback) => Application.Current.TryFindResource(key) as Brush ?? fallback;

    public HistoryWindow(IReadOnlyList<ReleaseNote> releases, string installedTag)
    {
        Title = "Apa yang baru";
        Width = 560;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = B("BgBrush", Brushes.Black);
        SourceInitialized += (s, e) => DarkTitleBar.Apply(this);

        var grid = new Grid { Margin = new Thickness(18) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var head = new TextBlock
        {
            Text = "Riwayat pembaruan (terbaru di atas)",
            Foreground = B("MutedBrush", Brushes.Gray),
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 10)
        };
        grid.Children.Add(head);

        var list = new StackPanel();
        if (releases.Count == 0)
            list.Children.Add(new TextBlock { Text = "Belum ada rilis.", Foreground = B("MutedBrush", Brushes.Gray) });

        foreach (var r in releases)
        {
            bool installed = string.Equals(r.Tag, installedTag, StringComparison.OrdinalIgnoreCase);
            var card = new Border
            {
                Background = B("SurfaceBrush", Brushes.DimGray),
                BorderBrush = installed ? B("AccentBrush", Brushes.Purple) : B("LineBrush", Brushes.Gray),
                BorderThickness = new Thickness(installed ? 2 : 1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 12),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var sp = new StackPanel();
            var top = new StackPanel { Orientation = Orientation.Horizontal };
            top.Children.Add(new TextBlock
            {
                Text = r.Tag,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = B("TextBrush", Brushes.White)
            });
            if (installed)
                top.Children.Add(new TextBlock
                {
                    Text = "  (terpasang)",
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Foreground = B("GreenBrush", Brushes.LightGreen)
                });
            top.Children.Add(new TextBlock
            {
                Text = "   " + r.Date.ToString("d MMM yyyy HH:mm"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Bottom,
                Foreground = B("MutedBrush", Brushes.Gray)
            });
            sp.Children.Add(top);
            sp.Children.Add(new TextBlock
            {
                Text = r.Notes.Length > 0 ? r.Notes : "(tanpa catatan)",
                Foreground = r.Notes.Length > 0 ? B("SoftTextBrush", Brushes.White) : B("MutedBrush", Brushes.Gray),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });
            card.Child = sp;
            list.Children.Add(card);
        }

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = list };
        Grid.SetRow(scroll, 1);
        grid.Children.Add(scroll);

        var close = new Button { Content = "Tutup", Padding = new Thickness(18, 6, 18, 6), HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        if (Application.Current.TryFindResource("GhostButton") is Style st) close.Style = st;
        close.Click += (s, e) => Close();
        Grid.SetRow(close, 2);
        grid.Children.Add(close);

        Content = grid;
    }
}
