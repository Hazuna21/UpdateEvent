using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HazunaV2;

// Dialog kecil untuk mengisi satu teks.
public class PromptWindow : Window
{
    private readonly TextBox _box = new();
    public string? Result { get; private set; }

    public PromptWindow(string title, string label, string initial = "")
    {
        Title = title;
        Width = 380;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Application.Current.TryFindResource("BgBrush") as Brush ?? Brushes.Black;
        SourceInitialized += (s, e) => DarkTitleBar.Apply(this);

        var root = new StackPanel { Margin = new Thickness(20) };
        root.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Application.Current.TryFindResource("TextBrush") as Brush ?? Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8)
        });
        _box.Text = initial;
        root.Children.Add(_box);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        Button Make(string text, string style, Action act)
        {
            var b = new Button { Content = text, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0) };
            if (Application.Current.TryFindResource(style) is Style st) b.Style = st;
            b.Click += (s, e) => act();
            return b;
        }
        row.Children.Add(Make("Batal", "GhostButton", Close));
        row.Children.Add(Make("OK", "PrimaryButton", () => { Result = _box.Text.Trim(); Close(); }));
        root.Children.Add(row);
        Content = root;

        Loaded += (s, e) => { _box.Focus(); _box.SelectAll(); };
    }

    public static string? Ask(Window owner, string title, string label, string initial = "")
    {
        var w = new PromptWindow(title, label, initial) { Owner = owner };
        w.ShowDialog();
        return w.Result;
    }
}
