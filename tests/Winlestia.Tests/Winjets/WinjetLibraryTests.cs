// C#12 / Tests de la bibliothèque (import, mise à jour, désinstallation) et des instances (persistance, réglages, reprise).
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using PanneauxGlass.Winjets;
using Xunit;
namespace Winlestia.Tests.Winjets;

public class WinjetLibraryTests
{
    static WinjetLibrary Lib(TempDir t, string? builtin = null) => new(t.Combine("winjets"), builtin, Pkg.Current);

    [Fact]
    public void ImportUpdateUninstall()
    {
        using var t = new TempDir();
        var lib = Lib(t);
        var r = lib.Import(Pkg.Default().WriteZip(t.Combine("a.zip")));
        Assert.True(r.Ok, string.Join("|", r.Errors));
        Assert.False(r.Updated);
        Assert.NotNull(lib.Find("com.test.demo"));

        var r2 = lib.Import(Pkg.Default(version: "1.1.0").WriteZip(t.Combine("b.zip")));
        Assert.True(r2.Ok);
        Assert.True(r2.Updated);
        Assert.Equal("1.1.0", lib.Find("com.test.demo")!.Manifest.Version);

        Assert.Null(lib.Uninstall("com.test.demo"));
        Assert.Null(lib.Find("com.test.demo"));
        Assert.False(Directory.Exists(Path.Combine(lib.PackagesDir, "com.test.demo")));
    }

    [Fact]
    public void InvalidImportLeavesInstalledVersion()
    {
        using var t = new TempDir();
        var lib = Lib(t);
        Assert.True(lib.Import(Pkg.Default().WriteZip(t.Combine("a.zip"))).Ok);
        var bad = Pkg.Default(version: "2.0.0").Set("assets/x.exe", "MZ").WriteZip(t.Combine("b.zip"));
        Assert.False(lib.Import(bad).Ok);
        Assert.Equal("1.0.0", lib.Find("com.test.demo")!.Manifest.Version);
    }

    [Fact]
    public void InterruptedSwapRestoresOldVersionAtStartup()
    {
        using var t = new TempDir();
        string root = t.Combine("winjets");
        Directory.CreateDirectory(Path.Combine(root, "packages"));
        Pkg.Default().WriteFolder(Directory.CreateDirectory(Path.Combine(root, "tmp", "old-com.test.demo~abc")).FullName);
        var lib = new WinjetLibrary(root, null, Pkg.Current);
        Assert.NotNull(lib.Find("com.test.demo"));
    }

    [Fact]
    public async System.Threading.Tasks.Task ConcurrentInspectsDoNotCollide()
    {
        using var t = new TempDir();
        var lib = Lib(t);
        string zip = Pkg.Default().WriteZip(t.Combine("a.zip"));
        var results = await System.Threading.Tasks.Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => System.Threading.Tasks.Task.Run(() => lib.Inspect(zip))));
        Assert.All(results, r => Assert.True(r.Ok, string.Join("|", r.Errors)));
        Assert.Equal(8, results.Select(r => r.Package!.Dir).Distinct().Count());
    }

    [Fact]
    public void InspectDoesNotInstall()
    {
        using var t = new TempDir();
        var lib = Lib(t);
        Assert.True(lib.Inspect(Pkg.Default().WriteZip(t.Combine("a.zip"))).Ok);
        Assert.Null(lib.Find("com.test.demo"));
    }

    [Fact]
    public void BuiltinCannotBeUninstalledOrOverridden()
    {
        using var t = new TempDir();
        string b = Directory.CreateDirectory(t.Combine("builtin")).FullName;
        Pkg.Default("com.test.core").WriteFolder(Directory.CreateDirectory(Path.Combine(b, "com.test.core")).FullName);
        var lib = Lib(t, b);
        Assert.True(lib.IsBuiltin("com.test.core"));
        Assert.NotNull(lib.Uninstall("com.test.core"));
        Assert.NotNull(lib.Find("com.test.core"));
        Assert.False(lib.Import(Pkg.Default("com.test.core", "2.0.0").WriteZip(t.Combine("a.zip"))).Ok);
    }

    [Fact]
    public void BuiltinDirFromEnvironment()
    {
        using var t = new TempDir();
        string b = Directory.CreateDirectory(t.Combine("builtin")).FullName;
        using var env = new EnvScope("WINLESTIA_WINJETS_BUILTIN", b);
        Assert.Equal(b.TrimEnd('\\', '/'), Path.GetFullPath(WinjetLibrary.ResolveBuiltinDir()!).TrimEnd('\\', '/'), true);
    }

    [Fact]
    public void DefaultRootFollowsDataDir()
    {
        using var t = new TempDir();
        using var env = new EnvScope("WINLESTIA_DATA_DIR", t.Path);
        Assert.StartsWith(t.Path, WinjetLibrary.DefaultRoot());
    }
}

public class WinjetInstancesTests
{
    static WinjetPackage Package(TempDir t)
    {
        string dir = Pkg.Default().WriteFolder(Directory.CreateDirectory(t.Combine("pkg")).FullName);
        using var v = WinjetValidator.ValidateDirectory(dir, Pkg.Current);
        Assert.True(v.Ok, string.Join("|", v.Errors));
        return v.Package!;
    }

    static JsonElement J(string s) => JsonDocument.Parse(s).RootElement.Clone();

    [Fact]
    public void AddPersistsAndReloads()
    {
        using var t = new TempDir();
        string file = t.Combine("instances.json");
        var pkg = Package(t);
        var a = new WinjetInstances(file);
        var inst = a.Add(pkg, "", 10, 20)!;
        Assert.StartsWith("w-", inst.Id);
        Assert.Equal("Paris", inst.Settings["city"]);
        var b = new WinjetInstances(file);
        Assert.Equal(inst.Id, Assert.Single(b.All).Id);
    }

    [Fact]
    public void AddBoundsScreenAndCoordinates()
    {
        using var t = new TempDir();
        var pkg = Package(t);
        var a = new WinjetInstances(t.Combine("i.json"));
        var g = a.Add(pkg, new string('s', 500), 1e12, -1e12)!.Geometry;
        Assert.Equal("", g.Screen);
        Assert.Equal(100000, g.X);
        Assert.Equal(-100000, g.Y);
    }

    [Fact]
    public void OpacityDefaultsToNullClampsAndPersists()
    {
        using var t = new TempDir();
        string file = t.Combine("i.json");
        var a = new WinjetInstances(file);
        var inst = a.Add(Package(t), "", 0, 0)!;
        Assert.Null(inst.Opacity);
        Assert.True(a.SetOpacity(inst.Id, J("30")));
        Assert.Equal(30, a.Find(inst.Id)!.Opacity);
        Assert.True(a.SetOpacity(inst.Id, J("250")));
        Assert.Equal(100, a.Find(inst.Id)!.Opacity);
        Assert.True(a.SetOpacity(inst.Id, J("-5")));
        Assert.Equal(0, a.Find(inst.Id)!.Opacity);
        Assert.True(a.SetOpacity(inst.Id, J("42.6")));
        Assert.Equal(43, new WinjetInstances(file).Find(inst.Id)!.Opacity);
        Assert.False(a.SetOpacity(inst.Id, J("\"x\"")));
        Assert.False(a.SetOpacity("absent", J("10")));
        Assert.Equal(43, a.Find(inst.Id)!.Opacity);
        Assert.True(a.SetOpacity(inst.Id, J("null")));
        Assert.Null(new WinjetInstances(file).Find(inst.Id)!.Opacity);
    }

    [Fact]
    public void OpacityChangeSavesOnce()
    {
        using var t = new TempDir();
        var a = new WinjetInstances(t.Combine("i.json"));
        var inst = a.Add(Package(t), "", 0, 0)!;
        int saves = 0;
        a.Changed += () => saves++;
        a.SetOpacity(inst.Id, J("50"));
        a.SetOpacity(inst.Id, J("50"));
        Assert.Equal(1, saves);
    }

    [Fact]
    public void SettingsAreCoerced()
    {
        using var t = new TempDir();
        var pkg = Package(t);
        var a = new WinjetInstances(t.Combine("i.json"));
        var id = a.Add(pkg, "", null, null)!.Id;
        a.SetSettings(id, J("{\"city\":\"abcdefghijklmnopqrstuvwxyz\",\"decimals\":99,\"range\":\"nope\",\"showSeconds\":true,\"zzz\":1}"),
            pkg.Definition);
        var s = a.Find(id)!.Settings;
        Assert.Equal(10, ((string)s["city"]!).Length);
        Assert.Equal(6.0, Convert.ToDouble(s["decimals"]));
        Assert.Equal("7d", s["range"]);
        Assert.Equal(true, s["showSeconds"]);
        Assert.False(s.ContainsKey("zzz"));
    }

    [Fact]
    public void StateKeysLimitedToDeclared()
    {
        using var t = new TempDir();
        var pkg = Package(t);
        var a = new WinjetInstances(t.Combine("i.json"));
        var id = a.Add(pkg, "", null, null)!.Id;
        Assert.Null(a.SetState(id, "mode", J("\"pile\""), pkg.Definition));
        Assert.Equal("pile", a.Find(id)!.State["mode"]);
        Assert.NotNull(a.SetState(id, "undeclared", J("1"), pkg.Definition));
        Assert.NotNull(a.SetState(id, "mode", J("{}"), pkg.Definition));
    }

    [Fact]
    public void GeometryClampedToPackageSize()
    {
        using var t = new TempDir();
        var pkg = Package(t);
        var a = new WinjetInstances(t.Combine("i.json"));
        var id = a.Add(pkg, "", null, null)!.Id;
        a.SetGeometry(id, J("{\"x\":5,\"y\":6,\"w\":99999,\"h\":1}"), pkg.Definition);
        var g = a.Find(id)!.Geometry;
        Assert.Equal(720, g.W);
        Assert.Equal(168, g.H);
    }

    [Fact]
    public void RecoversFromBackupWhenMainFileCorrupt()
    {
        using var t = new TempDir();
        string file = t.Combine("i.json");
        var pkg = Package(t);
        var a = new WinjetInstances(file);
        a.Add(pkg, "", null, null);
        a.Add(pkg, "", null, null);
        Assert.True(File.Exists(file + ".bak"));
        File.WriteAllText(file, "{ corrompu");
        Assert.NotEmpty(new WinjetInstances(file).All);
    }

    [Fact]
    public void CorruptWithoutBackupStartsEmpty()
    {
        using var t = new TempDir();
        string file = t.Combine("i.json");
        File.WriteAllText(file, "n importe quoi");
        Assert.Empty(new WinjetInstances(file).All);
    }

    [Fact]
    public void RemoveAndRemoveForPackage()
    {
        using var t = new TempDir();
        var pkg = Package(t);
        var a = new WinjetInstances(t.Combine("i.json"));
        var id = a.Add(pkg, "", null, null)!.Id;
        a.Add(pkg, "", null, null);
        Assert.True(a.Remove(id));
        Assert.Equal(1, a.RemoveForPackage(pkg.Id));
        Assert.Empty(a.All);
    }
}
