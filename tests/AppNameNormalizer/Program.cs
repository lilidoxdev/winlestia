// Characterization of app display-name normalisation (CatalogueWatchRules.CleanName -> AppNameNormalizer) + parity with Inventory.ps1 Clean-Name.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using PanneauxGlass;
using PanneauxGlass.Platform;

class Test
{
    static readonly string[] Inputs =
    {
        "CrimsonDesert",
        "HogwartsLegacy",
        "crimsondesert",
        "HOGWARTSLEGACY",
        "Crimson Desert",
        "CrimsonDesert 2",
        "My CrimsonDesert",
        "App 2.3.1",
        "Foo.exe - Raccourci",
        "Foo - Raccourci",
        "Foo.exe   -   raccourci",
        "Lancer Foo",
        "lancer   Bar",
        "Lancer",
        "VLC Media Player.exe",
        "vlc media player.exe",
        "VLC Media Player",
        "Foo®",
        "Foo™",
        "FooÂ® Bar™",
        "Foo (x64)",
        "Foo Setup",
        "Foo_Bar",
        "GTA V",
        "NVIDIA App",
        "Battlefield2042",
        "Foo - Windows10",
        "Foo-windows10",
        "Foo - Windows 10",
        "",
        "   ",
        "  Foo  ",
        "Éditeur",
        "Éditeur de texte - Raccourci",
        "Lancer Foo.exe - Raccourci",
        "Lancer Hogwarts Legacy",
        "Lancer CrimsonDesert - Raccourci",
        "Foo - Raccourci - Raccourci",
        "Raccourci",
        "Foo Raccourci",
    };

    static string[] Dump(Func<string, string> f)
    {
        var o = new string[Inputs.Length];
        for (int i = 0; i < o.Length; i++)
            o[i] = f(Inputs[i]);
        return o;
    }

    static int Main(string[] a)
    {
        if (a.Length > 0 && a[0] == "dump")
        {
            foreach (var s in Dump(CatalogueWatchRules.CleanName))
                Console.WriteLine(JsonSerializer.Serialize(s));
            return 0;
        }
        var r = new Dictionary<string, bool>();
        var cs = Dump(AppNameNormalizer.Normalize);
        for (int i = 0; i < Inputs.Length; i++)
            r["expected#" + i + " " + JsonSerializer.Serialize(Inputs[i])] = cs[i] == Expected[i];
        r["cleanNameWrapper"] = Array.TrueForAll(
            Inputs,
            s => CatalogueWatchRules.CleanName(s) == AppNameNormalizer.Normalize(s)
        );
        r["nullSafe"] = AppNameNormalizer.Normalize(null!) == "";
        string res = Parity(cs);
        Console.WriteLine("parity: " + res);
        if (res != "SKIP")
            r["psParity"] = res == "OK";
        int ok = 0;
        foreach (var v in r.Values)
            if (v)
                ok++;
        foreach (var k in r)
            if (!k.Value)
                Console.WriteLine(k.Key + "=false");
        Console.WriteLine("TOTAL " + ok + "/" + r.Count);
        return ok == r.Count ? 0 : 1;
    }

    static string Parity(string[] cs)
    {
        try
        {
            string? root = AppContext.BaseDirectory;
            while (root != null && !File.Exists(Path.Combine(root, "src", "backend", "Inventory.ps1")))
                root = Path.GetDirectoryName(root);
            if (root == null)
                return "SKIP";
            string script = Path.Combine(root, "tests", "AppNameNormalizer", "parity.ps1");
            string inv = Path.Combine(root, "src", "backend", "Inventory.ps1");
            if (!File.Exists(script))
                return "SKIP";
            string tmp = Path.Combine(Path.GetTempPath(), "nn-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            string inP = Path.Combine(tmp, "in.json"),
                outP = Path.Combine(tmp, "out.json");
            File.WriteAllText(inP, JsonSerializer.Serialize(Inputs));
            var psi = new ProcessStartInfo(
                "powershell",
                "-NoProfile -ExecutionPolicy Bypass -File \""
                    + script
                    + "\" -InventoryPath \""
                    + inv
                    + "\" -InputPath \""
                    + inP
                    + "\" -OutputPath \""
                    + outP
                    + "\""
            )
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            Process p;
            try
            {
                p = Process.Start(psi)!;
            }
            catch
            {
                return "SKIP";
            }
            if (!p.WaitForExit(20000))
            {
                try
                {
                    p.Kill(true);
                }
                catch { }
                return "FAIL timeout";
            }
            if (p.ExitCode != 0 || !File.Exists(outP))
                return "FAIL exit " + p.ExitCode + " " + p.StandardError.ReadToEnd();
            var ps = JsonSerializer.Deserialize<string[]>(File.ReadAllText(outP))!;
            bool same = true;
            for (int i = 0; i < Inputs.Length; i++)
                if (ps[i] != cs[i])
                {
                    same = false;
                    Console.WriteLine(
                        "DIVERGE "
                            + JsonSerializer.Serialize(Inputs[i])
                            + " C#="
                            + JsonSerializer.Serialize(cs[i])
                            + " PS="
                            + JsonSerializer.Serialize(ps[i])
                    );
                }
            try
            {
                Directory.Delete(tmp, true);
            }
            catch { }
            return same ? "OK" : "FAIL diverge";
        }
        catch (Exception e)
        {
            return "FAIL " + e.Message;
        }
    }

    // Recorded from the pre-refactor CatalogueWatchRules.CleanName.
    static readonly string[] Expected =
    {
        "Crimson Desert",
        "Hogwarts Legacy",
        "Crimson Desert",
        "Hogwarts Legacy",
        "Crimson Desert",
        "CrimsonDesert 2",
        "My CrimsonDesert",
        "App 2.3.1",
        "Foo",
        "Foo",
        "Foo",
        "Foo",
        "Bar",
        "Lancer",
        "VLC Media Player",
        "VLC Media Player",
        "VLC Media Player",
        "Foo",
        "Foo",
        "Foo Bar",
        "Foo (x64)",
        "Foo Setup",
        "Foo_Bar",
        "GTA V",
        "NVIDIA App",
        "Battlefield2042",
        "Foo",
        "Foo",
        "Foo - Windows 10",
        "",
        "",
        "Foo",
        "Éditeur",
        "Éditeur de texte",
        "Foo",
        "Hogwarts Legacy",
        "Crimson Desert",
        "Foo - Raccourci",
        "Raccourci",
        "Foo Raccourci",
    };
}
