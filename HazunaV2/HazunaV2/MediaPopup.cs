using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace HazunaV2;

public static class MediaPopup
{
    public static double Volume { get; set; } = 1.0;

    // Dipanggil saat media gagal diputar dari antrean (tanpa kotak pesan yang menghalangi)
    public static event Action<string>? Failed;

    // Mengembalikan fungsi penutup jendela (aman dipanggil berkali-kali)
    public static Action Show(string path, TimeSpan? duration = null, double? volume = null)
    {
        bool isGif = Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase);

        var media = new MediaElement
        {
            LoadedBehavior = MediaState.Manual,
            UnloadedBehavior = MediaState.Manual,
            Stretch = Stretch.Uniform,
            Volume = Math.Clamp(volume ?? MediaPopup.Volume, 0, 1),
            Opacity = 0,
            Source = new Uri(path)
        };

        var win = new Window
        {
            Title = "Pratinjau Media",
            Width = 480,
            Height = 480,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            Background = Brushes.Black,
            Content = media
        };

        bool closed = false;
        DispatcherTimer? timer = null;

        void CloseNow()
        {
            if (closed) return;
            closed = true;
            win.Close();
        }

        win.Loaded += (s, e) =>
        {
            media.Play();
            media.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
        };

        media.MediaEnded += (s, e) =>
        {
            if (isGif)
            {
                media.Position = TimeSpan.Zero;
                media.Play();
            }
            else
            {
                CloseNow();
            }
        };

        media.MediaFailed += (s, e) =>
        {
            var msg = e.ErrorException?.Message ?? "penyebab tidak diketahui";
            if (duration == null)
                MessageBox.Show("Gagal memutar media:\n" + msg, "Error Media");
            else
                Failed?.Invoke(msg);
            CloseNow();
        };

        // Dengan durasi: tutup tepat waktu. Tanpa durasi: GIF 5 detik, video sampai selesai.
        var limit = duration ?? (isGif ? TimeSpan.FromSeconds(5) : (TimeSpan?)null);
        if (limit != null)
        {
            timer = new DispatcherTimer { Interval = limit.Value };
            timer.Tick += (s, e) => CloseNow();
            timer.Start();
        }

        win.MouseDown += (s, e) => CloseNow();

        win.Closed += (s, e) =>
        {
            closed = true;
            timer?.Stop();
            media.Close();
        };

        win.Show();
        return CloseNow;
    }
}