using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HazunaV2;

public class SummaryWindow : Window
{
    public SummaryWindow(string report, string savedPath, Action? speak, Action openFolder)
    {
        Title = "Ringkasan LIVE";
        Width = 560;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0F, 0x1E));
        SourceInitialized += (s, e) => DarkTitleBar.Apply(this);

        var grid = new Grid { Margin = new Thickness(18) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var box = new TextBox
        {
            Text = report,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        Grid.SetRow(box, 0);
        grid.Children.Add(box);

        var note = new TextBlock
        {
            Text = string.IsNullOrEmpty(savedPath) ? "" : "Tersimpan otomatis: " + savedPath,
            Foreground = new SolidColorBrush(Color.FromRgb(0xA5, 0xB0, 0xD0)),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0)
        };
        Grid.SetRow(note, 1);
        grid.Children.Add(note);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        Button Make(string text, Action act, bool primary = false)
        {
            var b = new Button { Content = text, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0) };
            if (Application.Current.TryFindResource(primary ? "PrimaryButton" : "GhostButton") is Style st) b.Style = st;
            b.Click += (s, e) => act();
            return b;
        }
        buttons.Children.Add(Make("Salin", () => { try { Clipboard.SetText(report); } catch { } }));
        buttons.Children.Add(Make("Buka folder", openFolder));
        if (speak != null) buttons.Children.Add(Make("Bacakan penutup oleh AI", speak, primary: true));
        buttons.Children.Add(Make("Tutup", Close));
        Grid.SetRow(buttons, 2);
        grid.Children.Add(buttons);

        Content = grid;
    }
}
