using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace HazunaV2;

// Dialog ubah satu tombol soundboard: nama, file suara, warna, volume, dan tombol pintas.
public class SoundboardEditWindow : Window
{
    public static readonly (string Key, string Label)[] Colors =
    {
        ("AccentBrush", "Ungu"), ("CyanBrush", "Cyan"), ("PinkBrush", "Pink"), ("GreenBrush", "Hijau"),
        ("AmberBrush", "Kuning"), ("BlueBrush", "Biru"), ("RoseBrush", "Merah")
    };

    private readonly TextBox _name = new();
    private readonly TextBox _file = new();
    private readonly ComboBox _color = new();
    private readonly Slider _volume = new() { Minimum = 10, Maximum = 200, SmallChange = 5, LargeChange = 10 };
    private readonly TextBlock _volumeText = new();
    private readonly TextBlock _hotkeyText = new();
    private string _hotkey;

    public bool Saved { get; private set; }
    public SoundboardItem Item { get; }

    public SoundboardEditWindow(SoundboardItem item)
    {
        Item = item;
        _hotkey = item.Hotkey;

        Title = "Ubah tombol soundboard";
        Width = 460;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Application.Current.TryFindResource("BgBrush") as Brush ?? Brushes.Black;
        SourceInitialized += (s, e) => DarkTitleBar.Apply(this);

        Brush Res(string k, Brush fb) => Application.Current.TryFindResource(k) as Brush ?? fb;
        TextBlock Label(string t) => new() { Text = t, Foreground = Res("MutedBrush", Brushes.Gray), FontSize = 12, Margin = new Thickness(0, 10, 0, 3) };
        Button Make(string text, string style, Action act)
        {
            var b = new Button { Content = text, Padding = new Thickness(14, 6, 14, 6), Margin = new Thickness(8, 0, 0, 0) };
            if (Application.Current.TryFindResource(style) is Style st) b.Style = st;
            b.Click += (s, e) => act();
            return b;
        }

        var root = new StackPanel { Margin = new Thickness(20) };

        root.Children.Add(Label("Nama tombol"));
        _name.Text = item.Name;
        root.Children.Add(_name);

        root.Children.Add(Label("File suara"));
        var fileRow = new DockPanel();
        var browse = Make("Pilih...", "GhostButton", Browse);
        DockPanel.SetDock(browse, Dock.Right);
        fileRow.Children.Add(browse);
        _file.Text = item.FilePath;
        _file.IsReadOnly = true;
        fileRow.Children.Add(_file);
        root.Children.Add(fileRow);

        root.Children.Add(Label("Warna tombol"));
        foreach (var c in Colors) _color.Items.Add(c.Label);
        _color.SelectedIndex = Math.Max(0, Array.FindIndex(Colors, c => c.Key == item.Color));
        _color.HorizontalAlignment = HorizontalAlignment.Left;
        _color.Width = 140;
        root.Children.Add(_color);

        root.Children.Add(Label("Volume tombol ini"));
        var volRow = new DockPanel();
        _volumeText.Width = 48;
        _volumeText.TextAlignment = TextAlignment.Right;
        _volumeText.Foreground = Res("CyanBrush", Brushes.Cyan);
        _volumeText.FontWeight = FontWeights.SemiBold;
        DockPanel.SetDock(_volumeText, Dock.Right);
        volRow.Children.Add(_volumeText);
        _volume.Value = Math.Clamp(item.Volume, 10, 200);
        _volume.ValueChanged += (s, e) => _volumeText.Text = (int)_volume.Value + "%";
        _volumeText.Text = (int)_volume.Value + "%";
        volRow.Children.Add(_volume);
        root.Children.Add(volRow);

        root.Children.Add(Label("Tombol pintas (berlaku juga saat game dibuka)"));
        var hkRow = new StackPanel { Orientation = Orientation.Horizontal };
        _hotkeyText.Foreground = Res("TextBrush", Brushes.White);
        _hotkeyText.VerticalAlignment = VerticalAlignment.Center;
        _hotkeyText.MinWidth = 150;
        hkRow.Children.Add(_hotkeyText);
        hkRow.Children.Add(Make("Atur...", "GhostButton", SetHotkey));
        hkRow.Children.Add(Make("Hapus", "GhostButton", () => { _hotkey = ""; ShowHotkey(); }));
        root.Children.Add(hkRow);
        ShowHotkey();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        buttons.Children.Add(Make("Batal", "GhostButton", Close));
        buttons.Children.Add(Make("Simpan", "PrimaryButton", Save));
        root.Children.Add(buttons);

        Content = root;
    }

    private void ShowHotkey() => _hotkeyText.Text = _hotkey.Length == 0 ? "(belum ada)" : Hotkeys.Pretty(_hotkey);

    private void Browse()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Pilih file suara",
            Filter = "File suara|*.mp3;*.wav;*.wma;*.m4a;*.aac|Semua file|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        _file.Text = dlg.FileName;
        if (string.IsNullOrWhiteSpace(_name.Text))
            _name.Text = Path.GetFileNameWithoutExtension(dlg.FileName);
    }

    private void SetHotkey()
    {
        var dlg = new HotkeyCaptureWindow(string.IsNullOrWhiteSpace(_name.Text) ? "tombol ini" : _name.Text, _hotkey) { Owner = this };
        dlg.ShowDialog();
        if (!dlg.Saved) return;
        _hotkey = dlg.Result;
        ShowHotkey();
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(_file.Text))
        {
            MessageBox.Show(this, "Pilih file suara dulu.", "Soundboard");
            return;
        }
        Item.Name = string.IsNullOrWhiteSpace(_name.Text) ? Path.GetFileNameWithoutExtension(_file.Text) : _name.Text.Trim();
        Item.FilePath = _file.Text;
        Item.Color = Colors[Math.Max(0, _color.SelectedIndex)].Key;
        Item.Volume = (int)_volume.Value;
        Item.Hotkey = _hotkey;
        Saved = true;
        Close();
    }
}
