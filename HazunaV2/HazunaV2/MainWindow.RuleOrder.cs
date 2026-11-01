using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HazunaV2;

// Mengubah urutan aturan dengan menggeser baris di Gift Rules, dan menduplikat aturan.
// Urutan di sini juga menentukan urutan kartu di overlay menu gift.
public partial class MainWindow
{
    private GiftRule? _dragRule;
    private DataGridRow? _dropRow;
    private static readonly Brush DropRowBrush = new SolidColorBrush(Color.FromRgb(0x4C, 0x2A, 0x85));

    // Pegangan "☰" di kolom pertama: tekan lalu geser untuk mulai memindahkan baris
    private void RuleHandle_Down(object sender, MouseButtonEventArgs e)
    {
        _dragRule = (sender as FrameworkElement)?.DataContext as GiftRule;
    }

    private void RuleHandle_Move(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragRule == null) return;
        if ((sender as FrameworkElement)?.DataContext is not GiftRule rule || rule != _dragRule) return;

        var dragged = _dragRule;
        _dragRule = null;

        RulesGrid.CommitEdit(DataGridEditingUnit.Row, true);
        RulesGrid.SelectedItem = dragged;
        DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(GiftRule), dragged), DragDropEffects.Move);
        ClearDropHighlight();
    }

    private void RulesGrid_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(GiftRule)))
        {
            e.Effects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = DragDropEffects.Move;
        e.Handled = true;

        var row = RowAt(e.OriginalSource);
        if (row != _dropRow)
        {
            ClearDropHighlight();
            _dropRow = row;
            if (_dropRow != null) _dropRow.Background = DropRowBrush;
        }

        // Gulir otomatis kalau baris diseret ke tepi atas atau bawah tabel
        var scroll = FindChild<ScrollViewer>(RulesGrid);
        if (scroll != null)
        {
            double y = e.GetPosition(RulesGrid).Y;
            if (y < 50) scroll.ScrollToVerticalOffset(scroll.VerticalOffset - 0.4);
            else if (y > RulesGrid.ActualHeight - 30) scroll.ScrollToVerticalOffset(scroll.VerticalOffset + 0.4);
        }
    }

    private void RulesGrid_DragLeave(object sender, DragEventArgs e) => ClearDropHighlight();

    private void RulesGrid_Drop(object sender, DragEventArgs e)
    {
        var row = RowAt(e.OriginalSource);
        ClearDropHighlight();

        if (e.Data.GetData(typeof(GiftRule)) is not GiftRule source || row?.Item is not GiftRule target || source == target)
            return;

        int from = _rules.IndexOf(source);
        int to = _rules.IndexOf(target);
        if (from < 0 || to < 0) return;

        _rules.Move(from, to);
        RulesGrid.SelectedItem = source;
        RulesGrid.ScrollIntoView(source);
        RuleStore.Save(_rules);
        e.Handled = true;
    }

    private void ClearDropHighlight()
    {
        _dropRow?.ClearValue(DataGridRow.BackgroundProperty);
        _dropRow = null;
    }

    private static DataGridRow? RowAt(object source)
    {
        var d = source as DependencyObject;
        while (d != null && d is not DataGridRow)
            d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        return d as DataGridRow;
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            var deeper = FindChild<T>(child);
            if (deeper != null) return deeper;
        }
        return null;
    }

    // Menyalin aturan terpilih tepat di bawahnya (berguna untuk satu efek dengan dua sumber)
    private void DuplicateRule_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGrid.SelectedItem is not GiftRule r)
        {
            MessageBox.Show("Pilih dulu aturan yang mau diduplikat.");
            return;
        }

        RulesGrid.CommitEdit(DataGridEditingUnit.Row, true);

        var copy = new GiftRule
        {
            Enabled = r.Enabled,
            Trigger = r.Trigger,
            LikeCount = r.LikeCount,
            GiftName = r.GiftName,
            GiftId = r.GiftId,
            Key = r.Key,
            HoldMs = r.HoldMs,
            RepeatByCount = r.RepeatByCount,
            DurationSeconds = r.DurationSeconds,
            SoundId = r.SoundId,
            MediaId = r.MediaId,
            EffectName = r.EffectName
        };

        _rules.Insert(_rules.IndexOf(r) + 1, copy);
        RulesGrid.SelectedItem = copy;
        RulesGrid.ScrollIntoView(copy);
        RuleStore.Save(_rules);
    }
}
