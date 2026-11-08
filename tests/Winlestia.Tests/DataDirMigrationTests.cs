// Store.MigrateLegacyData + WINLESTIA_DATA_DIR. Fixtures live under %TEMP%\WinlestiaDataDir and are deleted at the end.
using System;
using System.IO;
using PanneauxGlass;
namespace Winlestia.Tests;
public class DataDirMigrationTests
{
    static void Check(bool ok, string name) => Assert.True(ok, name);
    [Fact]
    public void AllChecks()
    {
        string d = Path.Combine(Path.GetTempPath(), "WinlestiaDataDir");
        if (Directory.Exists(d))
            Directory.Delete(d, true);
        try
        {
            string legacy = Path.Combine(d, "legacy"), data = Path.Combine(d, "data");
            Directory.CreateDirectory(Path.Combine(legacy, "clipboard"));
            Directory.CreateDirectory(Path.Combine(legacy, "pins"));
            File.WriteAllText(Path.Combine(legacy, "preferences-glass.json"), "{\"a\":1}");
            File.WriteAllText(Path.Combine(legacy, "preferences-glass.json.bak"), "{\"a\":0}");
            File.WriteAllText(Path.Combine(legacy, "clipboard", "c.txt"), "clip");
            File.WriteAllText(Path.Combine(legacy, "pins", "p.png"), "pin");
            Store.MigrateLegacyData(legacy, data);
            Check(File.ReadAllText(Path.Combine(data, "preferences-glass.json")) == "{\"a\":1}" && File.Exists(Path.Combine(data, "preferences-glass.json.bak")), "(a) prefs + bak copied");
            Check(File.Exists(Path.Combine(legacy, "preferences-glass.json")), "(a) legacy prefs kept (rollback)");
            Check(File.Exists(Path.Combine(data, "clipboard", "c.txt")) && File.Exists(Path.Combine(data, "pins", "p.png")) && !Directory.Exists(Path.Combine(legacy, "clipboard")) && !Directory.Exists(Path.Combine(legacy, "pins")), "(a) clipboard/pins moved");
            File.WriteAllText(Path.Combine(data, "preferences-glass.json"), "{\"a\":2}");
            Directory.CreateDirectory(Path.Combine(legacy, "clipboard"));
            File.WriteAllText(Path.Combine(legacy, "clipboard", "new.txt"), "n");
            Store.MigrateLegacyData(legacy, data);
            Check(File.ReadAllText(Path.Combine(data, "preferences-glass.json")) == "{\"a\":2}", "(b)(c) second run does not overwrite existing prefs");
            Check(!File.Exists(Path.Combine(data, "clipboard", "new.txt")) && File.Exists(Path.Combine(legacy, "clipboard", "new.txt")), "(b) existing data folder untouched, no merge");
            Store.MigrateLegacyData(Path.Combine(d, "absent"), Path.Combine(d, "data2"));
            Check(!File.Exists(Path.Combine(d, "data2", "preferences-glass.json")), "no legacy: nothing created, no throw");
            Store.MigrateLegacyData(legacy, legacy);
            Check(File.Exists(Path.Combine(legacy, "preferences-glass.json")) && File.Exists(Path.Combine(legacy, "clipboard", "new.txt")), "(e) legacy==data: no-op, nothing lost");
            string l2 = Path.Combine(d, "l2"), d2 = Path.Combine(d, "d2");
            Directory.CreateDirectory(l2);
            Directory.CreateDirectory(d2);
            File.WriteAllText(Path.Combine(l2, "preferences-glass.json.bak"), "{\"a\":0}");
            File.WriteAllText(Path.Combine(d2, "preferences-glass.json"), "{\"a\":5}");
            Store.MigrateLegacyData(l2, d2);
            Check(File.Exists(Path.Combine(d2, "preferences-glass.json.bak")) && File.ReadAllText(Path.Combine(d2, "preferences-glass.json")) == "{\"a\":5}", "(f) bak copied independently, prefs kept");
            string l3 = Path.Combine(d, "l3"), d3 = Path.Combine(d, "d3");
            Directory.CreateDirectory(Path.Combine(l3, "clipboard"));
            Directory.CreateDirectory(Path.Combine(d3, "clipboard.migrating"));
            File.WriteAllText(Path.Combine(l3, "clipboard", "c.txt"), "clip");
            File.WriteAllText(Path.Combine(d3, "clipboard.migrating", "stale.txt"), "x");
            using (var h = new FileStream(Path.Combine(l3, "clipboard", "c.txt"), FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                Store.MigrateLegacyData(l3, d3);
            }
            Check(File.ReadAllText(Path.Combine(d3, "clipboard", "c.txt")) == "clip" && !File.Exists(Path.Combine(d3, "clipboard", "stale.txt")) && !Directory.Exists(Path.Combine(d3, "clipboard.migrating")) && File.Exists(Path.Combine(l3, "clipboard", "c.txt")), "(g) Move fails -> copy fallback complete, stale leftover cleaned, legacy intact");
            string env = Path.Combine(d, "envdir");
            Environment.SetEnvironmentVariable("WINLESTIA_DATA_DIR", env);
            Check(Store.DataDir == env && Directory.Exists(env), "(d) WINLESTIA_DATA_DIR honored");
            Environment.SetEnvironmentVariable("WINLESTIA_DATA_DIR", null);
            Check(Store.DataDir == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Panneaux"), "default = LocalAppData/Panneaux");
        }
        finally { TempDir.Delete(d); }
    }
}
