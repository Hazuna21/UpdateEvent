using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HazunaV2;

// Soundboard: semua item jenis Suara di Media & Sounds jadi tombol yang bisa diklik untuk diputar langsung.
public partial class MainWindow
{
    private readonly List<SoundChannel> _boardChannels = new();
    private readonly SoundChannel _boardSingle = new();
    private static readonly string[] BoardColors = { "AccentBrush", "CyanBrush", "PinkBrush", "GreenBrush", "AmberBrush", "BlueBrush", "RoseBrush" };
    private bool _boardReady;

    private void InitSoundboard()
    {
        BoardVolumeSlider.Value = Math.Clamp(_settings.SoundboardVolume, 1, 200);
        BoardVolumeLabel.Text = _settings.SoundboardVolume + "%";
        BoardOverlapCheck.IsChecked = _settings.SoundboardOverlap;
        _boardSingle.Error += msg => Dispatcher.InvokeAsync(() => AddLog("[Soundboard] " + msg));
        _boardReady = true;
    }

    private void BuildSoundboard()
    {
        if (!_boardReady) return;

        string q = (BoardSearchBox.Text ?? "").Trim().ToLowerInvariant();
        BoardPanel.Children.Clear();

        var items = _library.Where(m => m.Kind == "Suara")
            .Where(m => q.Length == 0 || m.DisplayName.ToLowerInvariant().Contains(q))
            .ToList();

        int i = 0;
        foreach (var m in items)
        {
            bool exists = File.Exists(m.FilePath);
            var btn = new Button
            {
                Width = 150,
                Height = 64,
                Margin = new Thickness(0, 0, 10, 10),
                Padding = new Thickness(8, 4, 8, 4),
                Foreground = new SolidColorBrush(Color.FromRgb(0x10, 0x14, 0x37)),
                FontWeight = FontWeights.SemiBold,
                IsEnabled = exists,
                Opacity = exists ? 1 : 0.4,
                ToolTip = exists ? m.DisplayName : m.DisplayName + " (file hilang)",
                Tag = m,
                Content = new TextBlock
                {
                    Text = m.DisplayName,
                    TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxHeight = 52
                }
            };
            btn.SetResourceReference(Control.BackgroundProperty, BoardColors[i++ % BoardColors.Length]);
            btn.Click += BoardButton_Click;
            BoardPanel.Children.Add(btn);
        }

        BoardEmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        BoardEmptyText.Text = _library.Any(m => m.Kind == "Suara")
            ? "Tidak ada suara yang cocok dengan pencarian."
            : "Belum ada suara. Tambahkan di Media & Sounds, jenis \"Suara\".";
    }

    private void BoardButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: MediaItem m }) return;
        int vol = (int)BoardVolumeSlider.Value;

        if (BoardOverlapCheck.IsChecked == true)
        {
            // Tumpuk: tiap klik memakai jalur sendiri; jalur yang sudah selesai dibersihkan
            _boardChannels.RemoveAll(c => !c.IsPlaying);
            var ch = new SoundChannel();
            ch.Error += msg => Dispatcher.InvokeAsync(() => AddLog("[Soundboard] " + msg));
            _boardChannels.Add(ch);
            ch.Play(m.FilePath, vol);
        }
        else
        {
            _boardSingle.Play(m.FilePath, vol);
        }
    }

    private void BoardStop_Click(object sender, RoutedEventArgs e)
    {
        _boardSingle.Stop();
        foreach (var c in _boardChannels) c.Stop();
        _boardChannels.Clear();
    }

    private void BoardSearch_Changed(object sender, TextChangedEventArgs e) => BuildSoundboard();

    private void BoardVolume_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_boardReady) return;
        int v = (int)BoardVolumeSlider.Value;
        BoardVolumeLabel.Text = v + "%";
        _settings.SoundboardVolume = v;
        SettingsStore.Save(_settings);
    }

    private void BoardOverlap_Click(object sender, RoutedEventArgs e)
    {
        _settings.SoundboardOverlap = BoardOverlapCheck.IsChecked == true;
        SettingsStore.Save(_settings);
    }
}
