#if WINLESTIA_SELFTEST
// WINLESTIA_FILES_SELFTEST=dnd : menu contextuel natif + glisser-déposer sur un dossier de fixtures (premier argument de ligne de
// commande, doit vivre sous %TEMP%\WinlestiaFilesOps).
// Fixtures : file001.txt..file012.txt, ctxtest.txt, dossiers Cible, Autre, Third. WINLESTIA_FILES_SHOTDIR = où les deux captures sont
// écrites. Seuls des verbes inoffensifs sont invoqués nativement ("properties", dont la fenêtre est refermée) ; la suppression n'est
// jamais envoyée à la Corbeille.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace WinlestiaFiles;

public sealed partial class MainWindow
{
    // Laisse le glisser-déposer simulé se stabiliser entre deux cycles (la fuite GDI se mesure par cycle).
    const int DndCycleGapMs = 30;
    // Pas de déplacement du curseur : assez lent pour que le glisser-déposer réel suive chaque position.
    const int DndCursorStepMs = 35;

    delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool PostMessageW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr SendMessageW(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RectN rect);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    [StructLayout(LayoutKind.Sequential)]
    struct RectN
    {
        public int L, T, R, B;
    }

    static IntPtr FindWindowTitled(string needle)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            var sb = new StringBuilder(256);
            if (IsWindowVisible(h) && GetWindowTextW(h, sb, 256) > 0 && sb.ToString().Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                found = h;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    NPoint RowPt(FileItem item)
    {
        // le rafraîchissement automatique a pu remplacer l'objet de ligne (dossier dont le contenu a changé)
        item = Active.Find(item.FullPath) ?? item;
        list.ScrollIntoView(item);
        list.UpdateLayout();
        var box = (FrameworkElement)list.ItemContainerGenerator.ContainerFromItem(item);
        var p = box.PointToScreen(new Point(Math.Min(300, box.ActualWidth / 2), box.ActualHeight / 2));
        return new NPoint((int)p.X, (int)p.Y);
    }

    static NPoint PtOf(FrameworkElement e, double x, double y)
    {
        var p = e.PointToScreen(new Point(x, y));
        return new NPoint((int)p.X, (int)p.Y);
    }

    static Border? FindPill(DependencyObject d)
    {
        for (int i = 0, n = VisualTreeHelper.GetChildrenCount(d); i < n; i++)
        {
            var c = VisualTreeHelper.GetChild(d, i);
            if (c is Border b && b.ToolTip is string)
                return b;
            if (FindPill(c) is { } found)
                return found;
        }
        return null;
    }

    // Capture de la zone d'écran autour de la fenêtre, prise par un processus séparé (le thread UI peut être dans une boucle de menu
    // modale) ; appuie ensuite sur Échap si demandé.
    Process? StartShot(string png, int delayMs, bool esc)
    {
        GetWindowRect(Hwnd, out var r);
        string script = $@"
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms,System.Drawing
Add-Type -Namespace N -Name W -MemberDefinition '[DllImport(""user32.dll"")] public static extern bool SetProcessDPIAware();
[DllImport(""user32.dll"")] public static extern void keybd_event(byte k, byte s, uint f, UIntPtr e);'
[void][N.W]::SetProcessDPIAware()
Start-Sleep -Milliseconds {delayMs}
$want = New-Object Drawing.Rectangle {Math.Max(0, r.L - 10)},{Math.Max(0, r.T - 10)},{r.R - r.L + 420},{r.B - r.T + 120}
$rect = [Drawing.Rectangle]::Intersect($want, [Windows.Forms.SystemInformation]::VirtualScreen)
$bmp = New-Object Drawing.Bitmap $rect.Width,$rect.Height
$g = [Drawing.Graphics]::FromImage($bmp);
$g.CopyFromScreen($rect.Location,[Drawing.Point]::Empty,$rect.Size);
$bmp.Save('{png}');
$g.Dispose();
$bmp.Dispose()
if ({(esc
    ? "$true"
    : "$false")}) {{ Start-Sleep -Milliseconds 500;
[N.W]::keybd_event(0x1B,0,0,[UIntPtr]::Zero);
[N.W]::keybd_event(0x1B,0,2,[UIntPtr]::Zero) }}";
        var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-STA", "-NoProfile", "-NonInteractive", "-EncodedCommand",
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) })
            psi.ArgumentList.Add(a);
        return Process.Start(psi);
    }
}
#endif
