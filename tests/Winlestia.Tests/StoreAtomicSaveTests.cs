// Store.Save (atomic write + rolling .bak) and Store.MigrateLegacyData, in temp directories only.
using System;
using System.IO;
using System.Linq;
using PanneauxGlass;
using Winlestia.Shared;
using Xunit;

namespace Winlestia.Tests;

public class StoreAtomicSaveTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "winlestia-test-atomic-" + Guid.NewGuid().ToString("N"));
    readonly string? previousEnv = Environment.GetEnvironmentVariable("WINLESTIA_DATA_DIR");
    readonly Preferences previousState = Store.State;
    readonly string previousRoot = Store.Root;
    readonly System.Collections.Generic.List<Entry> previousEntries = Store.Entries;

    public StoreAtomicSaveTests()
    {
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("WINLESTIA_DATA_DIR", dir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("WINLESTIA_DATA_DIR", previousEnv);
        Store.State = previousState;
        Store.Root = previousRoot;
        Store.Entries = previousEntries;
        try
        {
            Directory.Delete(dir, true);
        }
        catch { }
    }

    [Fact]
    public void SaveWritesPreferencesKeepsBakAndLeavesNoTmp()
    {
        string f = Path.Combine(dir, "preferences-glass.json");
        Store.State = new Preferences { PreviousTheme = "first" };
        Store.Save();
        Assert.Contains("first", File.ReadAllText(f));
        Store.State = new Preferences { PreviousTheme = "second" };
        Store.Save();
        Assert.Contains("second", File.ReadAllText(f));
        Assert.Contains("first", File.ReadAllText(f + ".bak"));
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        Assert.Equal("second", Store.Read<Preferences>(f).PreviousTheme);
    }

    [Fact]
    public void SaveThenCorruptMainRecoversFromBak()
    {
        string f = Path.Combine(dir, "preferences-glass.json");
        Store.State = new Preferences { PreviousTheme = "kept" };
        Store.Save();
        Store.Save();
        File.WriteAllText(f, "{ broken");
        Assert.Equal("kept", Store.Read<Preferences>(f).PreviousTheme);
        Assert.Equal("{ broken", File.ReadAllText(f + ".corrupt.bak"));
    }

    [Fact]
    public void MissingMainAndBakGiveDefaults()
    {
        string f = Path.Combine(dir, "preferences-glass.json");
        Assert.Equal("", Store.Read<Preferences>(f).PreviousTheme);
        Assert.False(File.Exists(f + ".corrupt.bak"));
    }

    [Fact]
    public void AtomicFileReplaceWithBackupKeepsPreviousAndNoTmp()
    {
        string f = Path.Combine(dir, "x.txt");
        AtomicFile.WriteAllText(f, "one");
        AtomicFile.WriteAllText(f, "two", null, f + ".bak");
        Assert.Equal("two", File.ReadAllText(f));
        Assert.Equal("one", File.ReadAllText(f + ".bak"));
        Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
    }

    [Fact]
    public void MigrateLegacyDataCopiesPrefsAndBakAndMovesFolders()
    {
        string legacy = Path.Combine(dir, "legacy"),
            data = Path.Combine(dir, "data");
        Directory.CreateDirectory(Path.Combine(legacy, "pins", "sub"));
        Directory.CreateDirectory(Path.Combine(legacy, "clipboard"));
        File.WriteAllText(Path.Combine(legacy, "pins", "sub", "a.txt"), "A");
        File.WriteAllText(Path.Combine(legacy, "clipboard", "c.txt"), "C");
        File.WriteAllText(Path.Combine(legacy, "preferences-glass.json"), "{\"PreviousTheme\":\"old\"}");
        File.WriteAllText(Path.Combine(legacy, "preferences-glass.json.bak"), "{}");
        Store.MigrateLegacyData(legacy, data);
        Assert.Equal("A", File.ReadAllText(Path.Combine(data, "pins", "sub", "a.txt")));
        Assert.Equal("C", File.ReadAllText(Path.Combine(data, "clipboard", "c.txt")));
        Assert.Contains("old", File.ReadAllText(Path.Combine(data, "preferences-glass.json")));
        Assert.True(File.Exists(Path.Combine(data, "preferences-glass.json.bak")));
        // second run never overwrites existing data
        File.WriteAllText(Path.Combine(data, "preferences-glass.json"), "{\"PreviousTheme\":\"new\"}");
        Store.MigrateLegacyData(legacy, data);
        Assert.Contains("new", File.ReadAllText(Path.Combine(data, "preferences-glass.json")));
        Assert.Empty(Directory.GetDirectories(data, "*.migrating"));
    }

    [Fact]
    public void LoadReadsPreferencesAndCatalogueFromTempDirs()
    {
        string root = Path.Combine(dir, "root");
        Directory.CreateDirectory(root);
        Store.Root = root;
        File.WriteAllText(Path.Combine(dir, "preferences-glass.json"), "{\"PreviousTheme\":\"loaded\",\"GlassOpacity\":0.12}");
        File.WriteAllText(Path.Combine(root, "catalog.json"), "{\"Entries\":[{\"Id\":\"a\",\"Name\":\"Alpha\"}]}");
        Store.Load();
        Assert.Equal("loaded", Store.State.PreviousTheme);
        Assert.Equal(.95, Store.State.GlassOpacity, 6);
        Assert.Equal(1, Store.State.GlassOpacityRevision);
        Assert.Contains(Store.Entries, e => e.Id == "a");
    }

    [Fact]
    public void HealStaleExePicksNewestSiblingAppFolder()
    {
        foreach (string v in new[] { "app-1.0.9", "app-1.0.10", "app-1.0.10-beta" })
        {
            Directory.CreateDirectory(Path.Combine(dir, "sq", v));
            File.WriteAllText(Path.Combine(dir, "sq", v, "x.exe"), "");
        }
        string? healed = Store.HealStaleExe(Path.Combine(dir, "sq", "app-1.0.8", "x.exe"));
        Assert.Equal(Path.Combine(dir, "sq", "app-1.0.10", "x.exe"), healed);
        Assert.Null(Store.HealStaleExe(Path.Combine(dir, "sq", "plain", "x.exe")));
    }
}
