// FullscreenWatcher probe: a borderless form covering the primary monitor must appear in Current within 500 ms, be cleared on restore/close; a normal window must never count.
using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using PanneauxGlass;

class Probe
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr h);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern void keybd_event(byte vk, byte sc, uint f, UIntPtr e);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr h, int c);

    class Popup : Form
    {
        protected override CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                p.Style = unchecked((int)0x80000000) | 0x00040000 | 0x10000000;
                return p;
            }
        }
    } // WS_POPUP|WS_THICKFRAME|WS_VISIBLE, no caption

    static void Focus(Form f)
    {
        keybd_event(0x12, 0, 0, UIntPtr.Zero);
        keybd_event(0x12, 0, 2, UIntPtr.Zero);
        SetForegroundWindow(f.Handle);
        f.Activate();
    }

    static bool Wait(Func<bool> c, int ms)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end)
        {
            Application.DoEvents();
            if (c())
                return true;
            Thread.Sleep(10);
        }
        return c();
    }

    [STAThread]
    static int Main()
    {
        if (Environment.GetEnvironmentVariable("FSW_WATCH") is { } ws)
        {
            FullscreenWatcher.IgnoredPids = () =>
                System.Diagnostics.Process.GetProcessesByName("pythonw").Select(p => (uint)p.Id).ToArray();
            FullscreenWatcher.Changed += () =>
                Console.WriteLine(
                    $"{DateTime.Now:HH:mm:ss.fff} changed [{string.Join(",", FullscreenWatcher.Current)}]"
                );
            FullscreenWatcher.Start();
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} watching");
            var endW = DateTime.Now.AddSeconds(int.Parse(ws));
            string lastC = "?";
            while (DateTime.Now < endW)
            {
                var c = string.Join(",", FullscreenWatcher.Compute());
                if (c != lastC)
                {
                    lastC = c;
                    Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} compute [{c}]");
                }
                Thread.Sleep(50);
            }
            FullscreenWatcher.Stop();
            return 0;
        } // live probe: FSW_WATCH=<seconds>
        FullscreenWatcher.IncludeOwnProcessForTest = true;
        FullscreenWatcher.IgnoredPids = () =>
            System.Diagnostics.Process.GetProcessesByName("pythonw").Select(p => (uint)p.Id).ToArray(); // the running shell frontend (production ignores it too)
        var screen = Screen.PrimaryScreen!;
        int fired = 0;
        FullscreenWatcher.Changed += () => fired++;
        FullscreenWatcher.Start();
        if (!Wait(() => FullscreenWatcher.Current.Length == 0, 500))
        {
            Console.WriteLine("FAIL initial not empty: " + string.Join(",", FullscreenWatcher.Current));
            return 1;
        }
        var normal = new Form
        {
            StartPosition = FormStartPosition.Manual,
            Bounds = new Rectangle(screen.Bounds.X + 100, screen.Bounds.Y + 100, 600, 400),
        };
        normal.Show();
        Focus(normal);
        if (Wait(() => FullscreenWatcher.Current.Length > 0, 500))
        {
            Console.WriteLine("FAIL normal window counted");
            return 2;
        }
        Console.WriteLine("normal window: not fullscreen ok");
        var wa = screen.WorkingArea; // Electron-like maximized frameless window: work area plus ~8 px resize-border overhang, never the whole monitor
        var maxed = new Popup { StartPosition = FormStartPosition.Manual, Bounds = Rectangle.Inflate(wa, 8, 8) };
        maxed.Show();
        Focus(maxed);
        if (Wait(() => FullscreenWatcher.Current.Length > 0, 600))
        {
            Console.WriteLine("FAIL maximized captionless window counted");
            return 7;
        }
        Console.WriteLine("maximized captionless: not fullscreen ok");
        maxed.Close();
        Console.WriteLine(
            "two-monitor case: "
                + (Screen.AllScreens.Length > 1 ? "monitors present but not automated" : "SKIPPED (1 monitor)")
        );
        var full = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.Manual,
            Bounds = screen.Bounds,
            TopMost = true,
        };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        full.Show();
        Focus(full);
        bool on = Wait(() => FullscreenWatcher.Current.Length == 1, 500);
        Console.WriteLine(
            "fullscreen detected="
                + on
                + " in "
                + sw.ElapsedMilliseconds
                + " ms current=["
                + string.Join(",", FullscreenWatcher.Current)
                + "] device="
                + screen.DeviceName
        );
        Console.WriteLine(
            "direct Compute=["
                + string.Join(",", FullscreenWatcher.Compute())
                + "] fg=0x"
                + GetForegroundWindow().ToString("x")
                + " full=0x"
                + full.Handle.ToString("x")
        );
        if (!on || FullscreenWatcher.Current[0] != screen.DeviceName)
            return 3;
        full.Bounds = new Rectangle(screen.Bounds.X + 100, screen.Bounds.Y + 100, 600, 400);
        Focus(full);
        if (!Wait(() => FullscreenWatcher.Current.Length == 0, 800))
        {
            Console.WriteLine("FAIL not cleared after restore");
            return 4;
        }
        Console.WriteLine("restore cleared ok");
        full.Bounds = screen.Bounds;
        Focus(full);
        if (!Wait(() => FullscreenWatcher.Current.Length == 1, 500))
        {
            Console.WriteLine("FAIL second entry");
            return 5;
        }
        full.Close();
        normal.Close();
        if (!Wait(() => FullscreenWatcher.Current.Length == 0, 800))
        {
            Console.WriteLine("FAIL not cleared after close");
            return 6;
        }
        FullscreenWatcher.Stop();
        Console.WriteLine("Changed fired " + fired + " times; all ok");
        return 0;
    }
}
