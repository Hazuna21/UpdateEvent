using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace HazunaV2;

public static class DarkTitleBar
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int dark = 1;
            DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));      // mode gelap

            int caption = 0x0022170F;                                    // warna bilah judul (#0F1722)
            DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));   // hanya berpengaruh di Windows 11
        }
        catch
        {
            // Tidak didukung di versi Windows ini: abaikan saja
        }
    }
}