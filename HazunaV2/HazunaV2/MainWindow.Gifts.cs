using System.Windows;
using System.Windows.Threading;

namespace HazunaV2;

public partial class MainWindow
{
    private void InitGifts()
    {
        // Simpan daftar gift secara berkala (hanya kalau ada perubahan)
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        timer.Tick += (s, e) => GiftCatalog.Flush();
        timer.Start();
    }

    private void PickGift_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GiftRule rule) return;

        var dlg = new GiftPickerWindow(rule.GiftId, rule.GiftName) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Selected != null)
        {
            rule.GiftId = dlg.Selected.Id;
            rule.GiftName = dlg.Selected.Name;
            RuleStore.Save(_rules);
        }
    }
}