using System.Runtime.InteropServices;

namespace HazunaV2;

public static class KeySender
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    private static readonly SemaphoreSlim Gate = new(1, 1);

    private static readonly Dictionary<string, ushort> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = 0x0D, ["Return"] = 0x0D, ["Space"] = 0x20, ["Tab"] = 0x09,
        ["Esc"] = 0x1B, ["Escape"] = 0x1B, ["Backspace"] = 0x08,
        ["Up"] = 0x26, ["Down"] = 0x28, ["Left"] = 0x25, ["Right"] = 0x27,
        ["Home"] = 0x24, ["End"] = 0x23, ["PageUp"] = 0x21, ["PageDown"] = 0x22,
        ["Insert"] = 0x2D, ["Delete"] = 0x2E,
        ["Shift"] = 0x10, ["Ctrl"] = 0x11, ["Control"] = 0x11, ["Alt"] = 0x12,
        ["Backtick"] = 0xC0, ["Minus"] = 0xBD, ["Equals"] = 0xBB,
        ["LBracket"] = 0xDB, ["RBracket"] = 0xDD, ["Backslash"] = 0xDC,
        ["Semicolon"] = 0xBA, ["Quote"] = 0xDE, ["Comma"] = 0xBC,
        ["Period"] = 0xBE, ["Slash"] = 0xBF
    };

    private static readonly Dictionary<char, ushort> Symbols = new()
    {
        [';'] = 0xBA, ['='] = 0xBB, [','] = 0xBC, ['-'] = 0xBD, ['.'] = 0xBE,
        ['/'] = 0xBF, ['`'] = 0xC0, ['['] = 0xDB, ['\\'] = 0xDC, [']'] = 0xDD,
        ['\''] = 0xDE
    };

        private static readonly HashSet<ushort> Extended = new()
    {
        0x26, 0x28, 0x25, 0x27, 0x24, 0x23, 0x21, 0x22, 0x2D, 0x2E
    };

    // ---------- Struktur Win32 ----------

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    // ---------- Parsing nama tombol ----------

    public static bool TryParse(string text, out List<ushort> vks, out string error)
    {
        vks = new List<ushort>();
        error = "";

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "kosong";
            return false;
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (TryParseOne(part, out var vk))
            {
                vks.Add(vk);
            }
            else
            {
                error = "nama tombol tidak dikenal: " + part;
                return false;
            }
        }

        if (vks.Count == 0)
        {
            error = "kosong";
            return false;
        }
        return true;
    }

    private static bool TryParseOne(string name, out ushort vk)
    {
        vk = 0;

        if (Names.TryGetValue(name, out vk))
            return true;

        if (name.Length == 1)
        {
            char c = char.ToUpperInvariant(name[0]);
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
            {
                vk = c;
                return true;
            }
            
            if (Symbols.TryGetValue(name[0], out vk))
            return true;
            return false;
        }

        if ((name[0] == 'F' || name[0] == 'f') &&
            int.TryParse(name.AsSpan(1), out var f) && f >= 1 && f <= 12)
        {
            vk = (ushort)(0x70 + f - 1);
            return true;
        }

        if (name.Length == 4 && name.StartsWith("Num", StringComparison.OrdinalIgnoreCase) &&
            char.IsDigit(name[3]))
        {
            vk = (ushort)(0x60 + (name[3] - '0'));
            return true;
        }

        return false;
    }

        // Menekan kombinasi tombol, menahannya selama 'hold', lalu melepasnya.
    public static async Task HoldAsync(string text, TimeSpan hold, CancellationToken ct = default)
    {
        if (!TryParse(text, out var vks, out var error))
            throw new ArgumentException(error);

        await Gate.WaitAsync(ct);
        int pressed = 0;
        try
        {
            foreach (var vk in vks)
            {
                Send(vk, false);
                pressed++;
            }
            await Task.Delay(hold, ct);
        }
        finally
        {
            // Selalu lepas tombol, termasuk saat dibatalkan, supaya tidak nyangkut
            for (int j = pressed - 1; j >= 0; j--)
                Send(vks[j], true);
            Gate.Release();
        }
    }

    private static void Send(ushort vk, bool keyUp)
    {
        ushort scan = (ushort)MapVirtualKey(vk, 0);

        uint flags = KEYEVENTF_SCANCODE;
        if (Extended.Contains(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        if (keyUp) flags |= KEYEVENTF_KEYUP;

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT { wVk = 0, wScan = scan, dwFlags = flags }
            }
        };

        uint sent = SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        if (sent == 0)
            throw new InvalidOperationException(
                "SendInput gagal (kode " + Marshal.GetLastWin32Error() + ")");
    }
}