// Process separe : "sample <dossier>" imprime R,G,B de l'icone shell ; "watch <dossier> <ms>" : cache chaud + pompe de messages.
using System;
using System.Runtime.InteropServices;
using System.Threading;
using D = System.Drawing;
static class Program
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct FileInfo
    {
        public IntPtr Icon; public int Index; public uint Attr; [MarshalAs(UnmanagedType.ByValTStr,
            SizeConst = 260)] public string Display; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string Type;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path, uint attrs, ref FileInfo info,
        uint size, uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    static D.Color Sample(string folder)
    {
        var info = new FileInfo();
        SHGetFileInfo(folder, 0, ref info, (uint)Marshal.SizeOf<FileInfo>(), 0x100);
        if (info.Icon == IntPtr.Zero)
            return D.Color.Empty;
        try
        {
            using var icon = D.Icon.FromHandle(info.Icon);
            using var bmp = icon.ToBitmap();
            return bmp.GetPixel(bmp.Width / 2, bmp.Height * 3 / 4);
        }
        finally { DestroyIcon(info.Icon); }
    }
    static string Watch(string folder, int ms)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        var first = Sample(folder);
        var t0 = DateTime.UtcNow;
        using var form = new System.Windows.Forms.Form();
        form.Show();
        while (DateTime.UtcNow < end)
        {
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(100);
            if (Math.Abs(Sample(folder).R - first.R) > 60)
                return "CHANGED after " + (int)(DateTime.UtcNow - t0).TotalMilliseconds + " ms";
        }
        return "UNCHANGED for " + ms + " ms";
    }
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length > 2 && args[0] == "watch")
        {
            Console.WriteLine(Watch(args[1], int.Parse(args[2])));
            return 0;
        }
        if (args.Length > 1 && args[0] == "sample")
        {
            var c = Sample(args[1]);
            Console.WriteLine(c.R + "," + c.G + "," + c.B);
            return 0;
        }
        return 1;
    }
}
