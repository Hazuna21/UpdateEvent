using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace HazunaV2;

public class LibraryPickerWindow : Window
{
    private static readonly SolidColorBrush Muted = new(Color.FromRgb(0xA5, 0xB0, 0xD0));

    private readonly string _kind;
    private readonly ListBox _list = new();
    private readonly TextBlock _hint = new();

    // Id item yang dipilih; kosong berarti "tidak ada"
    public string SelectedId { get; private set; } = "";

    public LibraryPickerWindow(string kind, string currentId)
    {
        _kind = kind;
        bool isSound = kind == AppData.KindSound;

        Title = isSound ? "Pilih Suara dari Pustaka" : "Pilih Media dari Pustaka";
        Width = 480;
        Height = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0F, 0x1E));
        SourceInitialized += (s, e) => DarkTitleBar.Apply(this);

        var grid = new Grid { Margin = new Thickness(18) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _hint.Foreground = Muted;
        _hint.TextWrapping = TextWrapping.Wrap;
        _hint.Margin = new Thickness(0, 0, 0, 10);
        Grid.SetRow(_hint, 0);
        grid.Children.Add(_hint);

        _list.Background = new SolidColorBrush(Color.FromRgb(0x13, 0x1A, 0x2E));
        _list.BorderThickness = new Thickness(0);
        Grid.SetRow(_list, 1);
        grid.Children.Add(_list);

        var bottom = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        Grid.SetRow(bottom, 2);

        var add = new Button { Content = "+ Tambah File Baru...", Padding = new Thickness(12, 6, 12, 6) };
        DockPanel.SetDock(add, Dock.Left);
        add.Click += (s, e) => AddNew();

        var right = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        var cancel = new Button
        {
            Content = "Batal",
            IsCancel = true,
            Padding = new Thickness(16, 6, 16, 6),
            Margin = new Thickness(0, 0, 8, 0)
        };
        var ok = new Button { Content = "Pilih", IsDefault = true, Padding = new Thickness(18, 6, 18, 6) };
        ok.Click += (s, e) => Confirm();
        right.Children.Add(cancel);
        right.Children.Add(ok);

        bottom.Children.Add(add);
        bottom.Children.Add(right);
        grid.Children.Add(bottom);

        Content = grid;
        Populate(currentId);
    }

    private void Populate(string selectId)
    {
        _list.Items.Clear();
        ListBoxItem? toSelect = null;

        var none = MakeItem(null);
        _list.Items.Add(none);
        if (string.IsNullOrEmpty(selectId)) toSelect = none;

        int count = 0;
        foreach (var m in AppData.Library.Where(x => x.Kind == _kind))
        {
            var li = MakeItem(m);
            _list.Items.Add(li);
            count++;
            if (m.Id == selectId) toSelect = li;
        }

        string word = _kind == AppData.KindSound ? "suara" : "media";
        _hint.Text = count == 0
            ? $"Belum ada {word} di pustaka. Tambahkan file baru lewat tombol di bawah."
            : $"Pilih {word} dari pustaka. Klik dua kali untuk memilih cepat. " +
              "Urutan mengikuti pengaturan di menu Media & Sounds.";

        if (toSelect != null)
        {
            toSelect.IsSelected = true;
            _list.ScrollIntoView(toSelect);
        }
    }

    private ListBoxItem MakeItem(MediaItem? m)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = m == null ? "(tidak ada, kosongkan pilihan)" : m.DisplayName,
            Foreground = Brushes.White,
            FontSize = 14,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        if (m != null)
        {
            bool ok = m.Status == "Ada";
            panel.Children.Add(new TextBlock
            {
                Text = $"{m.FileName}  ·  {m.Status}",
                Foreground = ok ? Muted : Brushes.Orange,
                FontSize = 11,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        var item = new ListBoxItem
        {
            Content = panel,
            Tag = m,
            Padding = new Thickness(12, 7, 12, 7)
        };
        item.MouseDoubleClick += (s, e) => Confirm();
        return item;
    }

    private void Confirm()
    {
        if (_list.SelectedItem is ListBoxItem li)
        {
            SelectedId = (li.Tag as MediaItem)?.Id ?? "";
            DialogResult = true;
        }
    }

    private void AddNew()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Tambah file ke pustaka",
            Multiselect = true,
            Filter = AppData.DialogFilter(_kind)
        };
        if (dlg.ShowDialog() != true) return;

        MediaItem? last = null;
        foreach (var file in dlg.FileNames)
            last = AppData.FindOrAdd(file, _kind);

        Populate(last?.Id ?? "");
    }
}