using System.Runtime.InteropServices;

namespace LifeDeck.Studio.Plugins;

public static class HotkeySender
{
    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint MAPVK_VK_TO_VSC = 0;

    public static void SendText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        try
        {
            System.Windows.Clipboard.SetText(text);
            SendHotkey("Ctrl+V");
        }
        catch
        {
            // Text actions are optional in v0.5.
        }
    }

    public static void SendHotkey(string hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey)) return;

        var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var keys = parts.Select(ParseKey).Where(k => k != 0).ToList();
        if (keys.Count == 0) return;

        // This intentionally uses the older keybd_event path because it worked
        // better with Discord global keybinds on the test setup than the newer
        // foreground/SendInput attempts.
        foreach (var k in keys)
        {
            keybd_event(k, Scan(k), 0, UIntPtr.Zero);
            Thread.Sleep(30);
        }

        Thread.Sleep(120);

        for (int i = keys.Count - 1; i >= 0; i--)
        {
            keybd_event(keys[i], Scan(keys[i]), KEYEVENTF_KEYUP, UIntPtr.Zero);
            Thread.Sleep(25);
        }
    }

    private static byte Scan(byte vk)
    {
        return (byte)(MapVirtualKey(vk, MAPVK_VK_TO_VSC) & 0xFF);
    }

    private static byte ParseKey(string s)
    {
        s = s.Trim().ToUpperInvariant();
        return s switch
        {
            "CTRL" or "CONTROL" => 0x11,
            "LCTRL" or "LEFTCTRL" or "LEFTCONTROL" => 0xA2,
            "RCTRL" or "RIGHTCTRL" or "RIGHTCONTROL" => 0xA3,
            "SHIFT" => 0x10,
            "LSHIFT" or "LEFTSHIFT" => 0xA0,
            "RSHIFT" or "RIGHTSHIFT" => 0xA1,
            "ALT" => 0x12,
            "LALT" or "LEFTALT" => 0xA4,
            "RALT" or "RIGHTALT" => 0xA5,
            "WIN" or "WINDOWS" => 0x5B,
            "ENTER" => 0x0D,
            "ESC" or "ESCAPE" => 0x1B,
            "SPACE" => 0x20,
            "TAB" => 0x09,
            "BACKSPACE" => 0x08,
            "DELETE" or "DEL" => 0x2E,
            "UP" => 0x26,
            "DOWN" => 0x28,
            "LEFT" => 0x25,
            "RIGHT" => 0x27,
            _ when s.Length == 1 && s[0] >= 'A' && s[0] <= 'Z' => (byte)s[0],
            _ when s.Length == 1 && s[0] >= '0' && s[0] <= '9' => (byte)s[0],
            _ when s.StartsWith("F") && int.TryParse(s.Substring(1), out var n) && n >= 1 && n <= 24 => (byte)(0x70 + n - 1),
            _ => 0
        };
    }
}
