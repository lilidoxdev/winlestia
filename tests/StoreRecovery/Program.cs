// Store.Read recovery: corrupt main + good .bak -> bak values; both corrupt -> defaults, .corrupt.bak kept.
using System;
using System.IO;
using PanneauxGlass;

static class Program
{
    static int total,
        passed;

    static void Check(bool ok, string name)
    {
        total++;
        if (ok)
            passed++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name);
    }

    static int Main()
    {
        string d = Path.Combine(Path.GetTempPath(), "winlestia-test");
        if (Directory.Exists(d))
            Directory.Delete(d, true);
        Directory.CreateDirectory(d);
        Environment.SetEnvironmentVariable("WINLESTIA_DATA_DIR", d);
        try
        {
            string f = Path.Combine(d, "preferences-glass.json");
            File.WriteAllText(f, "{ not json");
            File.WriteAllText(f + ".bak", "{\"PreviousTheme\":\"fromBak\"}");
            var p = Store.Read<Preferences>(f);
            Check(p.PreviousTheme == "fromBak", "corrupt main + good bak -> bak values");
            Check(
                File.Exists(f + ".corrupt.bak") && File.ReadAllText(f + ".corrupt.bak") == "{ not json",
                "corrupt copy kept"
            );
            Check(File.ReadAllText(f).Contains("fromBak"), "main restored from bak");
            File.WriteAllText(f, "{ not json");
            File.WriteAllText(f + ".bak", "also broken");
            File.Delete(f + ".corrupt.bak");
            p = Store.Read<Preferences>(f);
            Check(p.PreviousTheme == "", "both corrupt -> defaults");
            Check(
                File.Exists(f + ".corrupt.bak") && File.ReadAllText(f + ".corrupt.bak") == "{ not json",
                "both corrupt -> corrupt.bak kept"
            );
            File.Delete(f + ".bak");
            File.WriteAllText(f, "{ not json");
            Check(Store.Read<Preferences>(f).PreviousTheme == "", "corrupt main, no bak -> defaults");
            File.WriteAllText(f, "{\"PreviousTheme\":\"main\"}");
            File.WriteAllText(f + ".bak", "{\"PreviousTheme\":\"old\"}");
            Check(Store.Read<Preferences>(f).PreviousTheme == "main", "good main wins over bak");
            // locked main (transient IO error): defaults, nothing on disk touched, no .corrupt.bak
            File.Delete(f + ".corrupt.bak");
            string mainBefore = File.ReadAllText(f),
                bakBefore = File.ReadAllText(f + ".bak");
            using (new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                p = Store.Read<Preferences>(f);
            }
            Check(p.PreviousTheme == "", "locked main -> defaults");
            Check(
                File.ReadAllText(f) == mainBefore && File.ReadAllText(f + ".bak") == bakBefore,
                "locked main -> main and bak unchanged"
            );
            Check(!File.Exists(f + ".corrupt.bak"), "locked main -> no .corrupt.bak");
        }
        finally
        {
            try
            {
                Directory.Delete(d, true);
            }
            catch { }
        }
        Console.WriteLine($"TOTAL {passed}/{total}");
        return passed == total ? 0 : 1;
    }
}
