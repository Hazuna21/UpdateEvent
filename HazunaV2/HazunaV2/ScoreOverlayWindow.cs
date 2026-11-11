using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace HazunaV2;

// Overlay kecil "WIN x / 50" (dipakai di OBS / TikTok Studio, bisa digeser dengan klik kiri)
public class ScoreOverlayWindow : Window
{
    private readonly GameScore _score;
    private readonly TextBlock _value = new();
    private readonly TextBlock _target = new();
    private readonly Border _card = new();
    private readonly ScaleTransform _pulse = new(1, 1);
    private int _last;

    public ScoreOverlayWindow(GameScore score)
    {
        _score = score;
        _last = score.Value;

        Title = "Skor Overlay";
        SizeToContent = SizeToContent.WidthAndHeight;
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

        var label = new TextBlock
        {
            Text = "WIN",
            Foreground = new SolidColorBrush(Color.FromRgb(0xC4, 0xB5, 0xFD)),
            FontSize = 22,
            FontWeight = FontWeights.Black,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 14, 6)
        };

        _value.Foreground = Brushes.White;
        _value.FontSize = 60;
        _value.FontWeight = FontWeights.Black;
        _value.VerticalAlignment = VerticalAlignment.Bottom;
        _value.RenderTransform = _pulse;
        _value.RenderTransformOrigin = new Point(0.5, 0.6);

        _target.Foreground = new SolidColorBrush(Color.FromRgb(0xA7, 0x8B, 0xFA));
        _target.FontSize = 30;
        _target.FontWeight = FontWeights.Bold;
        _target.VerticalAlignment = VerticalAlignment.Bottom;
        _target.Margin = new Thickness(8, 0, 0, 8);

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(label);
        row.Children.Add(_value);
        row.Children.Add(_target);

        _card.Padding = new Thickness(22, 8, 22, 8);
        _card.CornerRadius = new CornerRadius(16);
        _card.Background = new LinearGradientBrush(
            Color.FromArgb(235, 0x1B, 0x10, 0x3A), Color.FromArgb(235, 0x0B, 0x07, 0x1C), 90);
        _card.BorderBrush = new SolidColorBrush(Color.FromRgb(0x8B, 0x5C, 0xF6));
        _card.BorderThickness = new Thickness(2);
        _card.Effect = new DropShadowEffect { Color = Color.FromRgb(0x8B, 0x5C, 0xF6), BlurRadius = 18, ShadowDepth = 0, Opacity = 0.7 };
        _card.Child = row;
        _card.Margin = new Thickness(14);
        Content = _card;

        MouseLeftButtonDown += (s, e) => DragMove();

        Action handler = Refresh;
        _score.Changed += handler;
        Closed += (s, e) => _score.Changed -= handler;

        Refresh();
    }

    public void SetChromaKey(bool on)
    {
        Background = on ? new SolidColorBrush(Color.FromRgb(0, 255, 0)) : Brushes.Transparent;
    }

    private void Refresh()
    {
        _value.Text = _score.Value.ToString();
        _target.Text = "/ " + _score.Target;

        if (_score.Value != _last)
        {
            bool up = _score.Value > _last;
            _last = _score.Value;

            _value.Foreground = new SolidColorBrush(up ? Color.FromRgb(0x86, 0xEF, 0xAC) : Color.FromRgb(0xFC, 0xA5, 0xA5));
            var back = new ColorAnimation(Colors.White, TimeSpan.FromMilliseconds(900)) { BeginTime = TimeSpan.FromMilliseconds(500) };
            ((SolidColorBrush)_value.Foreground).BeginAnimation(SolidColorBrush.ColorProperty, back);

            var grow = new DoubleAnimation(1.35, 1.0, TimeSpan.FromMilliseconds(450))
            {
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }
            };
            _pulse.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            _pulse.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        }
    }
}
