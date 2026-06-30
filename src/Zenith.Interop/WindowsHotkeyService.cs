using System;
using System.Runtime.InteropServices;
using Zenith.Core;

namespace Zenith.Interop;

public class WindowsHotkeyService : IHotkeyService
{
    public event EventHandler? HotkeyTriggered;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int HOTKEY_ID = 9000;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint VK_R = 0x52; // 'R' key

    public void RegisterGlobalHotkey()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Ctrl + Shift + R
            RegisterHotKey(IntPtr.Zero, HOTKEY_ID, MOD_CONTROL | MOD_SHIFT, VK_R);
            Console.WriteLine("Registered Global Hotkey: Ctrl+Shift+R");
        }
    }

    public void UnregisterGlobalHotkey()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            UnregisterHotKey(IntPtr.Zero, HOTKEY_ID);
        }
    }

    // In a real WPF/WinForms app, we'd hook into the WndProc message loop to listen for WM_HOTKEY (0x0312).
    // In Avalonia, this requires a native message pump hook or low-level keyboard hook.
    public void SimulateHotkeyTrigger()
    {
        HotkeyTriggered?.Invoke(this, EventArgs.Empty);
    }
}
