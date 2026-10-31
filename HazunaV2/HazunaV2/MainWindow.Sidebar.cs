using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace HazunaV2;

public partial class MainWindow
{
    private const double SidebarWide = 230;
    private const double SidebarNarrow = 74;

    private bool _sidebarCollapsed;

    private void InitSidebar()
    {
        _sidebarCollapsed = _settings.SidebarCollapsed;
        Sidebar.Width = _sidebarCollapsed ? SidebarNarrow : SidebarWide;
        ApplySidebarVisuals(animate: false);
    }

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        _sidebarCollapsed = !_sidebarCollapsed;

        _settings.SidebarCollapsed = _sidebarCollapsed;
        SettingsStore.Save(_settings);

        var width = new DoubleAnimation(
            _sidebarCollapsed ? SidebarNarrow : SidebarWide,
            TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        Sidebar.BeginAnimation(FrameworkElement.WidthProperty, width);

        ApplySidebarVisuals(animate: true);
    }

    private void ApplySidebarVisuals(bool animate)
    {
        double angle = _sidebarCollapsed ? 180 : 0;

        if (animate)
        {
            ChevronRotate.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty,
                new DoubleAnimation(angle, TimeSpan.FromMilliseconds(260)));
        }
        else
        {
            ChevronRotate.Angle = angle;
        }

        // Saat terlipat: judul disembunyikan; teks menu terpotong oleh template, nama muncul sebagai tooltip
        TitleText.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;

        foreach (var child in NavPanel.Children)
        {
            if (child is RadioButton rb)
                rb.ToolTip = _sidebarCollapsed ? rb.Content : null;
        }
    }
}