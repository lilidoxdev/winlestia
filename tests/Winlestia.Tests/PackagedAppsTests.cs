// C#12 / PackagedApps: chemin WindowsApps -> AUMID, migration des épingles (listes en mémoire, jamais l'état de l'utilisateur).
using System;
using System.Collections.Generic;
using System.Linq;
using PanneauxGlass;
namespace Winlestia.Tests;
public class PackagedAppsTests
{
    const string Claude = @"C:\Program Files\WindowsApps\Claude_4.29.0.0_x64__pzs8sxrjxfjjc\app\Claude.exe";
    const string Aumid = "Claude_pzs8sxrjxfjjc!Claude";
    static readonly Func<string, string, string?> None = (_, _) => null;
    static Entry[] Catalogue() => new[]
    {
        new Entry { Id = "uwp:Other_abc!App", Name = "Other", Target = @"shell:AppsFolder\Other_abc!App" },
        new Entry { Id = "uwp:" + Aumid, Name = "Claude", Target = @"shell:AppsFolder\" + Aumid, IconPath = @"C:\Logos\Claude.png" }
    };

    [Fact]
    public void ParsesWindowsAppsPath()
    {
        Assert.True(PackagedApps.TryParseWindowsAppsPath(Claude, out string name, out string pub, out string exe));
        Assert.Equal("Claude", name);
        Assert.Equal("pzs8sxrjxfjjc", pub);
        Assert.Equal(@"app\Claude.exe", exe);
        Assert.Equal("Claude_pzs8sxrjxfjjc", PackagedApps.PackageFamilyName(name, pub));
        Assert.True(PackagedApps.TryParseWindowsAppsPath(Claude.ToUpperInvariant(), out name, out _, out _));
        Assert.Equal("CLAUDE", name);
        Assert.True(PackagedApps.TryParseWindowsAppsPath(Claude.ToLowerInvariant(), out _, out pub, out _));
        Assert.Equal("pzs8sxrjxfjjc", pub);
        Assert.False(PackagedApps.TryParseWindowsAppsPath(@"C:\Program Files\Claude\Claude.exe", out _, out _, out _));
        Assert.False(PackagedApps.TryParseWindowsAppsPath("", out _, out _, out _));
    }

    [Fact]
    public void ResolvesAumidFromCatalogue()
    {
        Assert.Equal(Aumid, PackagedApps.ResolveAumid(Claude, Catalogue(), None));
        Assert.Null(PackagedApps.ResolveAumid(@"C:\Program Files\WindowsApps\Nope_1.0_x64__zzz\a.exe", Catalogue(), None));
        Assert.Null(PackagedApps.ResolveAumid(@"C:\x\y.exe", Catalogue(), None));
        var two = Catalogue().Concat(new[] { new Entry { Id = "uwp:Claude_pzs8sxrjxfjjc!Other", IconPath = @"C:\X\app\Other.exe" } });
        Assert.Equal("Claude_pzs8sxrjxfjjc!Other", PackagedApps.ResolveAumid(
            @"C:\Program Files\WindowsApps\Claude_4.29.0.0_x64__pzs8sxrjxfjjc\app\Other.exe", two, (_, _) => "Claude_pzs8sxrjxfjjc!Other"));
        Assert.Equal(Aumid, PackagedApps.ResolveAumid(Claude, two, (_, _) => Aumid));
    }

    [Fact]
    public void MigratesPins()
    {
        string oldId = "dock:" + Claude, newId = @"dock:shell:AppsFolder\" + Aumid;
        var pins = new List<string> { "a", oldId, "b", Claude, "dock:" + @"C:\Program Files\WindowsApps\Nope_1.0_x64__zzz\a.exe" };
        var custom = new List<Entry>
        {
            new Entry { Id = oldId, Name = "Mon Claude", Target = Claude, IconPath = Claude },
            new Entry { Id = pins[4], Name = "Nope", Target = @"C:\Program Files\WindowsApps\Nope_1.0_x64__zzz\a.exe" }
        };
        var favorites = new List<string> { oldId };
        var removed = new List<string>();
        Assert.True(PackagedApps.MigratePins(pins, custom, favorites, Catalogue(), removed, None));
        Assert.Equal(new[] { "a", newId, "b" }, pins.Take(3).ToArray());
        Assert.Equal(4, pins.Count);
        Assert.StartsWith("dock:", pins[3]);
        Assert.Contains("Nope", pins[3]);
        Assert.Equal(newId, custom[0].Id);
        Assert.Equal("Mon Claude", custom[0].Name);
        Assert.Equal(@"shell:AppsFolder\" + Aumid, custom[0].Target);
        Assert.Equal(@"C:\Logos\Claude.png", custom[0].IconPath);
        Assert.Equal(newId, favorites[0]);
        Assert.Equal(@"C:\Program Files\WindowsApps\Nope_1.0_x64__zzz\a.exe", custom[1].Target);
        // Idempotent
        Assert.False(PackagedApps.MigratePins(pins, custom, favorites, Catalogue(), removed, None));
    }

    [Fact]
    public void MergesDuplicateWithExistingPin()
    {
        string oldId = "dock:" + Claude, newId = @"dock:shell:AppsFolder\" + Aumid;
        var pins = new List<string> { newId, "x", oldId };
        var custom = new List<Entry>
        {
            new Entry { Id = newId, Target = @"shell:AppsFolder\" + Aumid },
            new Entry { Id = oldId, Target = Claude }
        };
        var removed = new List<string>();
        Assert.True(PackagedApps.MigratePins(pins, custom, new List<string>(), Catalogue(), removed, None));
        Assert.Equal(new[] { newId, "x" }, pins);
        Assert.Single(custom);
        Assert.Equal(new[] { oldId }, removed);
    }

    [Fact]
    public void IgnoresExecutionAliases()
    {
        Assert.False(PackagedApps.TryParseWindowsAppsPath(
            @"C:\Users\me\AppData\Local\Microsoft\WindowsApps\Claude_4.29.0.0_x64__pzs8sxrjxfjjc\a.exe", out _, out _, out _));
        Assert.False(PackagedApps.TryParseWindowsAppsPath(@"C:\Users\me\AppData\Local\Microsoft\WindowsApps\python.exe", out _, out _,
            out _));
        Assert.True(PackagedApps.TryParseWindowsAppsPath(@"D:\Program Files\WindowsApps\A_1_x64__b\a.exe", out _, out _, out _));
    }

    [Fact]
    public void MultiAppPackageUsesManifest()
    {
        var cat = new[]
        {
            new Entry { Id = "uwp:Multi_zzz1!First", IconPath = @"C:\L\first.png" },
            new Entry { Id = "uwp:Multi_zzz1!Second", IconPath = @"C:\L\second.png" }
        };
        string path = @"C:\Program Files\WindowsApps\Multi_1.0_x64__zzz1\bin\second.exe";
        string? seen = null;
        Assert.Equal("Multi_zzz1!Second", PackagedApps.ResolveAumid(path, cat, (f, e) => { seen = e; return "Multi_zzz1!Second"; }));
        Assert.Equal(@"bin\second.exe", seen);
        Assert.Equal("Multi_zzz2!First", PackagedApps.ResolveAumid(path.Replace("zzz1", "zzz2"), new[]
            { new Entry { Id = "uwp:Multi_zzz2!First" }, new Entry { Id = "uwp:Multi_zzz2!Second" } }, None));
    }

    [Fact]
    public void KeepsUserCustomisationsAndPreexistingDuplicates()
    {
        string oldId = "dock:" + Claude, newId = @"dock:shell:AppsFolder\" + Aumid;
        var prefs = new Preferences();
        prefs.Names[oldId] = "Perso";
        prefs.Categories[oldId] = "Cat";
        prefs.Icons[oldId] = @"C:\mine.png";
        var custom = new List<Entry> { new Entry { Id = oldId, Target = Claude, IconPath = @"C:\mine.png" } };
        var pins = new List<string> { "d", "d", oldId, "uwp:" + Aumid };
        Assert.True(PackagedApps.MigratePins(pins, custom, new List<string>(), Catalogue(), new List<string>(), None, prefs));
        Assert.Equal(new[] { "d", "d", newId }, pins);
        Assert.Equal("Perso", prefs.Names[newId]);
        Assert.Equal("Cat", prefs.Categories[newId]);
        Assert.Equal(@"C:\mine.png", prefs.Icons[newId]);
        Assert.False(prefs.Names.ContainsKey(oldId));
        Assert.Equal(@"C:\mine.png", custom[0].IconPath);
        // Fusion : les valeurs du survivant gagnent.
        prefs = new Preferences();
        prefs.Names[oldId] = "Ancien";
        prefs.Names[newId] = "Survivant";
        custom = new List<Entry> { new Entry { Id = newId, Target = @"shell:AppsFolder\" + Aumid }, new Entry {
            Id = oldId, Target = Claude } };
        PackagedApps.MigratePins(new List<string>(), custom, new List<string>(), Catalogue(), new List<string>(), None, prefs);
        Assert.Equal("Survivant", prefs.Names[newId]);
        Assert.False(prefs.Names.ContainsKey(oldId));
    }
}
