// C#12 / SecurityHardening: bookmark URI whitelist, private autostart temp path, FileSearch escaping (pure functions).
using System;
using System.Collections.Generic;
using System.IO;
using PanneauxGlass;
using PanneauxGlass.Explorer;
using PanneauxGlass.Platform;
using PanneauxGlass.Tools;

class Test
{
    static int Main()
    {
        var r = new Dictionary<string, bool>();
        Uri? u;
        string sql;
        string profile = @"C:\Users\Moi";
        // L1: bookmark URIs
        r["http"] = BookmarkUri.IsOpenable("http://example.com/a?b=1", out u) && u.Scheme == "http";
        r["https"] = BookmarkUri.IsOpenable("HTTPS://Example.com/", out u) && u.Scheme == "https";
        r["javascriptRefused"] = !BookmarkUri.IsOpenable("javascript:alert(1)", out _);
        r["fileRefused"] = !BookmarkUri.IsOpenable("file:///C:/Windows/System32/calc.exe", out _);
        r["fileUncHostRefused"] = !BookmarkUri.IsOpenable("file://server/share/x.html", out _);
        r["uncPathRefused"] =
            !BookmarkUri.IsOpenable(@"\\server\share\x", out _)
            && !BookmarkUri.IsOpenable("file://server/share/x", out _);
        r["msSettingsRefused"] = !BookmarkUri.IsOpenable("ms-settings:display", out _);
        r["dataRefused"] = !BookmarkUri.IsOpenable("data:text/html,<script>1</script>", out _);
        r["malformedRefused"] =
            !BookmarkUri.IsOpenable("http//no-colon", out _)
            && !BookmarkUri.IsOpenable("not a uri", out _)
            && !BookmarkUri.IsOpenable("", out _)
            && !BookmarkUri.IsOpenable("   ", out _)
            && !BookmarkUri.IsOpenable(null, out _)
            && !BookmarkUri.IsOpenable("/relative/path", out _);
        // L2: private temp location
        string local = @"C:\Users\Moi\AppData\Local";
        string a = AutostartPolicy.PrivateTaskFile(local),
            b = AutostartPolicy.PrivateTaskFile(local);
        r["taskFileUnderLocalAppData"] =
            a.StartsWith(Path.Combine(local, "Panneaux", "tmp") + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            && a.EndsWith(".xml");
        string realLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            realTask = AutostartPolicy.PrivateTaskFile(realLocal);
        r["taskFileNotSharedTemp"] =
            realLocal.Length > 0
            && !realTask.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)
            && realTask.StartsWith(
                Path.Combine(realLocal, "Panneaux", "tmp") + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase
            );
        r["taskFileUnique"] = a != b;
        // L5: FileSearch characterization
        r["sqlInjectionLiteral"] =
            FileQuery.TryBuild("a' OR 1=1 --", profile, out sql)
            && sql.EndsWith("CONTAINS(System.ItemNameDisplay,'\"a'' OR 1=1 --*\"')")
            && sql.IndexOf("'", sql.IndexOf("CONTAINS")) > 0;
        r["quotesBecomeBlanks"] =
            FileQuery.TryBuild("\"x\" OR \"y\"", profile, out sql)
            && sql.EndsWith("'\"x OR y*\"')")
            && !sql.Contains("\"x\"");
        r["wildcardsBecomeBlanks"] = FileQuery.TryBuild("100%_[a]*", profile, out sql) && sql.EndsWith("'\"100 a*\"')");
        r["profileQuoteDoubled"] =
            FileQuery.TryBuild("abc", @"C:\Users\O'Brien' OR 1=1 --", out sql)
            && sql.Contains(@"SCOPE='file:C:\Users\O''Brien'' OR 1=1 --' AND CONTAINS");
        r["profileNulRefused"] = !FileQuery.TryBuild("abc", @"C:\Users\a" + (char)0 + "b", out _);
        r["escapeLiteralDoubles"] = FileQuery.EscapeLiteral("a'b''c") == "a''b''''c";
        r["neutraliseKeepsQuote"] = FileQuery.NeutraliseContains("a'b") == "a'b";
        r["onlyOneStatement"] =
            FileQuery.TryBuild("x'; DROP TABLE y;--", profile, out sql) && sql.Split('\'').Length % 2 == 1;
        int ok = 0;
        foreach (var v in r.Values)
            if (v)
                ok++;
        foreach (var k in r)
            if (!k.Value)
                Console.WriteLine("FAIL " + k.Key);
        Console.WriteLine("TOTAL " + ok + "/" + r.Count);
        return ok == r.Count ? 0 : 1;
    }
}
