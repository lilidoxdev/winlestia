// C#12 / TrayProtocol : lecture d'un SHELLTRAYDATA, suivi des icônes et messages de menu contextuel.
using System;
using System.Collections.Generic;
using System.Text;
using PanneauxGlass;
namespace Winlestia.Tests;
public class TrayProtocolTests
{
    static byte[] Buffer(int message, int window, uint id, uint flags, uint callback, string tip, uint version)
    {
        var b = new byte[960];
        BitConverter.GetBytes(TrayProtocol.Signature).CopyTo(b, 0);
        BitConverter.GetBytes(message).CopyTo(b, 4);
        BitConverter.GetBytes(window).CopyTo(b, 12);
        BitConverter.GetBytes(id).CopyTo(b, 16);
        BitConverter.GetBytes(flags).CopyTo(b, 20);
        BitConverter.GetBytes(callback).CopyTo(b, 24);
        Encoding.Unicode.GetBytes(tip).CopyTo(b, 32);
        BitConverter.GetBytes(version).CopyTo(b, 808);
        return b;
    }
    [Fact]
    public void ParseAndTrack()
    {
        var icons = new Dictionary<(long, uint), TrayIcon>();
        Assert.True(TrayProtocol.TryParse(Buffer(0, 0x1234, 7, 5, 0x8001, "Tip", 0), out int m, out var icon, out uint f), "parse");
        Assert.True(m == 0 && icon.Window == (IntPtr)0x1234 && icon.Id == 7 && icon.Callback == 0x8001 && icon.Tip == "Tip", "fields");
        Assert.True(TrayProtocol.Apply(icons, m, icon, f) && icons.Count == 1, "add");
        TrayProtocol.TryParse(Buffer(4, 0x1234, 7, 0, 0, "", 4), out m, out icon, out f);
        Assert.True(TrayProtocol.Apply(icons, m, icon, f) && icons[(0x1234, 7)].Version == 4, "version");
        TrayProtocol.TryParse(Buffer(1, 0x1234, 7, 4, 0, "New", 0), out m, out icon, out f);
        TrayProtocol.Apply(icons, m, icon, f);
        Assert.True(icons[(0x1234, 7)].Tip == "New" && icons[(0x1234, 7)].Callback == 0x8001, "modifyKeepsCallback");
        TrayProtocol.TryParse(Buffer(2, 0x1234, 7, 0, 0, "", 0), out m, out icon, out f);
        Assert.True(TrayProtocol.Apply(icons, m, icon, f) && icons.Count == 0, "delete");
        var bad = Buffer(0, 1, 1, 0, 0, "", 0);
        bad[0] = 0;
        Assert.True(!TrayProtocol.TryParse(bad, out _, out _, out _) && !TrayProtocol.TryParse(new byte[8], out _, out _, out _), "reject");
    }
    [Fact]
    public void ContextMessages()
    {
        var v4 = new TrayIcon { Window = (IntPtr)1, Id = 9, Callback = 0x8001, Version = 4 };
        var one = TrayProtocol.ContextMessages(v4, 100, 200);
        Assert.True(one.Length == 1 && one[0].Message == 0x8001 && one[0].WParam == (IntPtr)((200 << 16) | 100)
            && one[0].LParam == (IntPtr)((9 << 16) | 0x7B), "v4");
        var legacy = TrayProtocol.ContextMessages(new TrayIcon { Id = 9, Callback = 0x8001, Version = 3 }, 1, 2);
        Assert.True(legacy.Length == 2 && legacy[0].WParam == (IntPtr)9 && legacy[0].LParam == (IntPtr)0x204
            && legacy[1].LParam == (IntPtr)0x205, "legacy");
    }
}
