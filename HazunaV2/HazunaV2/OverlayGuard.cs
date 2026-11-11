using System.Runtime.InteropServices;
using System.Windows;

namespace HazunaV2;

// Menjaga overlay tetap terbuka dan berjalan walau jendela utama di-minimize:
// - overlay yang ikut ter-minimize (mis. "Tampilkan Desktop") dikembalikan lagi;
// - Windows tidak boleh menghemat daya aplikasi ini (EcoQoS) saat jendela utamanya di-minimize,
//   karena itu bisa memperlambat animasi, timer, dan tangkapan jendela di TikTok Studio.
public static class OverlayGuard
{
    public static void Apply(Window w)
    {
        w.StateChanged += (s, e) =>
        {
            if (w.WindowState == WindowState.Minimized)
                w.Dispatcher.BeginInvoke(() => w.WindowState = WindowState.Normal);
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr process, int infoClass, ref PowerThrottlingState info, int size);

    // Matikan penghematan daya otomatis untuk proses ini (Windows 10 1709+ / Windows 11)
    public static void DisablePowerThrottling()
    {
        try
        {
            var st = new PowerThrottlingState
            {
                Version = 1,
                ControlMask = 0x1 | 0x4,      // kecepatan eksekusi + resolusi timer
                StateMask = 0                 // 0 = jangan dihemat
            };
            SetProcessInformation(System.Diagnostics.Process.GetCurrentProcess().Handle, 4, ref st, Marshal.SizeOf<PowerThrottlingState>());
        }
        catch { /* versi Windows lama: tidak ada penghematan seperti ini */ }
    }
}
