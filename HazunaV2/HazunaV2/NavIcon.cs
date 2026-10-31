using System.Windows;
using System.Windows.Media;

namespace HazunaV2;

// Menyimpan bentuk ikon pada tiap tombol menu samping
public static class NavIcon
{
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.RegisterAttached(
            "Data", typeof(Geometry), typeof(NavIcon), new PropertyMetadata(null));

    public static Geometry? GetData(DependencyObject obj) => (Geometry?)obj.GetValue(DataProperty);

    public static void SetData(DependencyObject obj, Geometry? value) => obj.SetValue(DataProperty, value);
}