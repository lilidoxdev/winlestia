// Small Win32 popup menu for our own commands (sort, quick access): same system dark/light popup theme as the shell menu, modal, returns the chosen id (0 = dismissed).
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace WinlestiaFiles;

internal static class NativeMenu
{
    // A null entry is a separator.
    internal sealed record Item(uint Id, string Text, bool Checked = false, bool Disabled = false, IReadOnlyList<Item?>? Sub = null);

    const uint MF_GRAYED = 0x1, MF_DISABLED = 0x2, MF_CHECKED = 0x8, MF_SEPARATOR = 0x800, MF_POPUP = 0x10, TPM_RIGHTBUTTON = 0x2, TPM_RETURNCMD = 0x100;
    [DllImport("user32.dll")] static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll")] static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool AppendMenuW(IntPtr menu, uint flags, UIntPtr id, string? text);
    [DllImport("user32.dll")] static extern uint TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out NPoint p);

    static void Fill(IntPtr menu, IReadOnlyList<Item?> items)
    {
        foreach (var item in items)
        {
            if (item == null)
            {
                AppendMenuW(menu, MF_SEPARATOR, UIntPtr.Zero, null);
                continue;
            }
            uint flags = (item.Checked ? MF_CHECKED : 0) | (item.Disabled ? MF_GRAYED | MF_DISABLED : 0);
            if (item.Sub != null)
            {
                var sub = CreatePopupMenu();
                Fill(sub, item.Sub);
                AppendMenuW(menu, flags | MF_POPUP, (UIntPtr)(ulong)sub.ToInt64(), item.Text);
            }
            else
                AppendMenuW(menu, flags, (UIntPtr)item.Id, item.Text);
        }
    }

    internal static uint Show(IntPtr hwnd, IReadOnlyList<Item?> items)
    {
        ShellMenu.InitMenuTheme(PanneauxGlass.Theme.Current.IsDark);
        var menu = CreatePopupMenu();
        if (menu == IntPtr.Zero)
            return 0;
        try
        {
            Fill(menu, items);
            GetCursorPos(out var at);
            SetForegroundWindow(hwnd);
            return TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, at.X, at.Y, hwnd, IntPtr.Zero);
        }
        finally { DestroyMenu(menu); }
    }
}
