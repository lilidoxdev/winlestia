// C# 12 / .NET 8 / Windows 11. Verify native Windows-key policy without injecting desktop input.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
namespace PanneauxGlass;
internal static class QtShellVerification {
    internal static int Run(){var checks=new List<object>();bool pass=true;void Check(string name,bool ok){checks.Add(new{name,ok});pass&=ok;}
        var p=new DockKeyPolicy();Check("Left Win down is held",p.Feed(0x5B,false).Consume);Check("Left Win up reveals",p.Feed(0x5B,true).Reveal);
        p=new();p.Feed(0x5C,false);Check("Right Win reveals",p.Feed(0x5C,true).Reveal);
        p=new();p.Feed(0x5B,false);p.Feed(0x5B,false);Check("Win auto-repeat reveals once",p.Feed(0x5B,true).Reveal);
        p=new();p.Feed(0x5B,false);var chord=p.Feed(0x45,false);Check("Win E forwards Win before E",!chord.Consume&&chord.Down==0x5B);Check("Win E keeps E up",!p.Feed(0x45,true).Consume);var end=p.Feed(0x5B,true);Check("Win E releases forwarded Win without dock",end.Consume&&!end.Reveal&&end.Up==0x5B);
        p=new();p.Feed(0x5B,false);p.Feed(0x5C,false);Check("Two Windows keys do not reveal early",!p.Feed(0x5B,true).Reveal);Check("Two Windows keys reveal on final release",p.Feed(0x5C,true).Reveal);
        p=new();p.Feed(0x5B,false);p.Feed(0x44,false);Check("Shutdown releases forwarded modifier",p.Reset()==0x5B);Check("Reset is idempotent",p.Reset()==0);
        Check("A stale outside click cannot close a newly opened panel",!QtShellBridge.KeepEvent("close",4,5));Check("A current outside click closes the panel",QtShellBridge.KeepEvent("close",5,5));Check("A shortcut remains valid across navigation",QtShellBridge.KeepEvent("palette",4,5));
        File.WriteAllText(Path.Combine(Store.Root,"verification-qt-keys.json"),JsonSerializer.Serialize(new{pass,checks},Store.Json));return pass?0:1;
    }
}
