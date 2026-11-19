using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PanneauxGlass;
namespace Winlestia.Tests;

public class PeripheralBatteryTests
{
    static PeripheralCandidate C(string id, string name, string kind = "other", bool connected = true, int? percent = 50,
        string source = "bluetooth", bool approx = false) => new(id, name, source, kind, connected, percent, null, approx);

    [Theory]
    [InlineData(0x240404u, "headset")]   // audio / casque
    [InlineData(0x240418u, "headset")]   // audio / écouteurs
    [InlineData(0x002540u, "keyboard")]
    [InlineData(0x002580u, "mouse")]
    [InlineData(0x0025C0u, "keyboard")]  // combo
    [InlineData(0x000508u, "gamepad")]
    [InlineData(0x00010Cu, null)]
    public void ClassOfDevice(uint cod, string? expected) => Assert.Equal(expected, PeripheralLogic.KindFromClassOfDevice(cod));

    [Theory]
    [InlineData(0x03C1, "keyboard")]
    [InlineData(0x03C2, "mouse")]
    [InlineData(0x03C3, "gamepad")]
    [InlineData(0x03C4, "gamepad")]
    [InlineData(0x0941, "headset")]
    [InlineData(0x0000, null)]
    public void Appearance(int value, string? expected) => Assert.Equal(expected, PeripheralLogic.KindFromAppearance((ushort)value));

    [Theory]
    [InlineData("Sony WH-1000XM5 Headphones", "headset")]
    [InlineData("Casque Logitech", "headset")]
    [InlineData("AirPods Pro", "headset")]
    [InlineData("Xbox Wireless Controller", "gamepad")]
    [InlineData("Manette Switch", "gamepad")]
    [InlineData("MX Master Mouse", "mouse")]
    [InlineData("Souris sans fil", "mouse")]
    [InlineData("Clavier K380", "keyboard")]
    [InlineData("Keyboard K380", "keyboard")]
    [InlineData("Truc", null)]
    public void Names(string name, string? expected) => Assert.Equal(expected, PeripheralLogic.KindFromName(name));

    [Fact]
    public void DetectionOrder()
    {
        Assert.Equal("headset", PeripheralLogic.DetectKind(0x240404u, 0x03C1, "Keyboard"));
        Assert.Equal("keyboard", PeripheralLogic.DetectKind(null, 0x03C1, "Mouse"));
        Assert.Equal("mouse", PeripheralLogic.DetectKind(null, null, "Souris"));
        Assert.Equal("other", PeripheralLogic.DetectKind(0x00010Cu, 0, "Truc"));
    }

    [Fact]
    public void OverrideWins()
    {
        Assert.Equal("mouse", PeripheralLogic.ResolveKind("mouse", "headset"));
        Assert.Equal("headset", PeripheralLogic.ResolveKind(null, "headset"));
        Assert.Equal("headset", PeripheralLogic.ResolveKind("bogus", "headset"));
        var snap = PeripheralLogic.Build(new[] { C("bt:A", "Pad", "gamepad") }, null, new Dictionary<string, string> {
            ["bt:A"] = "mouse" });
        Assert.Equal("mouse", snap.Devices[0].Kind);
        Assert.Equal("gamepad", snap.Paired[0].DetectedKind);
        Assert.True(snap.Paired[0].Overridden);
    }

    [Theory]
    [InlineData(2, 0, 5)]
    [InlineData(2, 1, 25)]
    [InlineData(3, 2, 60)]
    [InlineData(2, 3, 100)]
    [InlineData(0, 3, null)]
    [InlineData(1, 3, null)]
    [InlineData(255, 3, null)]
    [InlineData(2, 9, null)]
    public void XInputLevels(int type, int level, int? expected) => Assert.Equal(expected, PeripheralLogic.MapXInputLevel((byte)type,
        (byte)level));

    [Fact]
    public void MergeDedupesXboxPad()
    {
        var pad = C("xinput:0", "Xbox controller 1", "gamepad", source: "xinput", approx: true, percent: 60);
        var btNoBattery = C("bt:A", "Xbox Wireless Controller", "gamepad", percent: null);
        var merged = PeripheralLogic.Merge(new[] { btNoBattery }, new[] { pad });
        Assert.Single(merged);
        Assert.Equal("bt:A", merged[0].Id);   // l'id bt: survit pour le masquage / genre
        Assert.Equal(60, merged[0].Percent);
        Assert.True(merged[0].Approximate);

        var btWithBattery = btNoBattery with { Percent = 80 };
        merged = PeripheralLogic.Merge(new[] { btWithBattery }, new[] { pad });
        Assert.Single(merged);
        Assert.Equal("bt:A", merged[0].Id);

        merged = PeripheralLogic.Merge(new[] { C("bt:B", "Casque", "headset") }, new[] { pad });
        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void FilteringAndSorting()
    {
        var snap = PeripheralLogic.Build(new[]
        {
            C("bt:1", "Zeta", "mouse"), C("bt:2", "Alpha", "mouse"), C("bt:3", "Casque", "headset"),
            C("bt:4", "Off", "keyboard", connected: false), C("bt:5", "Unknown", "keyboard", percent: null),
            C("bt:6", "Hid", "gamepad")
        }, new HashSet<string> { "bt:6" }, null);
        Assert.Equal(new[] { "bt:3", "bt:6", "bt:2", "bt:1" }, snap.Devices.Select(d => d.Id));
        Assert.True(snap.Devices.Single(d => d.Id == "bt:6").Hidden);
        Assert.Equal(6, snap.Paired.Count);
        Assert.False(snap.Paired.Single(p => p.Id == "bt:4").Connected);
    }

    [Fact]
    public void PreferencesRoundTrip()
    {
        var p = new Preferences();
        Assert.True(p.PeripheralBatteryEnabled);
        p.PeripheralHidden.Add("bt:1");
        p.PeripheralKindOverrides["bt:1"] = "mouse";
        var back = System.Text.Json.JsonSerializer.Deserialize<Preferences>(System.Text.Json.JsonSerializer.Serialize(p))!;
        Assert.Equal("bt:1", back.PeripheralHidden.Single());
        Assert.Equal("mouse", back.PeripheralKindOverrides["bt:1"]);
    }

    [Fact]
    public void ArctisStatusParser()
    {
        var on = new byte[65]; on[1] = 0xB0; on[2] = 3; on[3] = 3; on[4] = 1;
        Assert.Equal(new HeadsetStatus(true, 75, true), HidHeadsets.ParseArctis7PlusStatus(on));
        on[4] = 0; on[3] = 0;
        Assert.Equal(new HeadsetStatus(true, 0, false), HidHeadsets.ParseArctis7PlusStatus(on));
        on[3] = 4;
        Assert.Equal(100, HidHeadsets.ParseArctis7PlusStatus(on)!.Percent);
        on[2] = 1;
        Assert.False(HidHeadsets.ParseArctis7PlusStatus(on)!.Available);
        on[2] = 3; on[3] = 9;
        Assert.Null(HidHeadsets.ParseArctis7PlusStatus(on));
        on[3] = 2; on[1] = 0x07;   // autre rapport (pas l'écho de la requête)
        Assert.Null(HidHeadsets.ParseArctis7PlusStatus(on));
        Assert.Null(HidHeadsets.ParseArctis7PlusStatus(new byte[3]));
        // Le niveau observé (00 B0 03 03 03) donne 75, jamais une valeur négative ou hors bornes, quel que soit l'octet lu.
        Assert.Equal(75, HidHeadsets.ParseArctis7PlusStatus(new byte[] { 0x00, 0xB0, 0x03, 0x03, 0x03 })!.Percent);
        for (int b = 0; b < 256; b++)
        {
            var s = HidHeadsets.ParseArctis7PlusStatus(new byte[] { 0x00, 0xB0, 0x03, (byte)b, 0x00 });
            Assert.True(s == null || s.Percent is >= 0 and <= 100, $"octet {b:x2}");
        }
        Assert.Null(HidHeadsets.ParseArctis7PlusStatus(null));
        Assert.Equal("hid:1038:2212", HidHeadsets.Id(0x1038, 0x2212));
    }

    [Fact]
    public void SystemBatteryMapping()
    {
        Assert.Null(PeripheralLogic.SystemBattery(-1, false));
        var d = PeripheralLogic.SystemBattery(42, true)!;
        Assert.Equal(("system:battery", "laptop", "system", 42, true), (d.Id, d.DetectedKind, d.Source, d.Percent, d.Charging));
        Assert.Equal("", d.Name);
        var snap = PeripheralLogic.Build(new[] { d, C("bt:1", "Zed", "other") }, null, new Dictionary<string, string> {
            ["system:battery"] = "mouse" });
        Assert.Equal("laptop", snap.Devices.Last().Kind);   // genre non surchargeable... et trié en dernier
        Assert.Equal("system:battery", snap.Devices.Last().Id);
    }

    [Fact]
    public async Task ChangedOnlyWhenSnapshotDiffers()
    {
        int changes = 0;
        var gate = new SemaphoreSlim(0);
        var data = new List<PeripheralCandidate> { C("bt:1", "A") };
        using var service = new PeripheralBatteryService(() => Interlocked.Increment(ref changes), () =>
        {
            var copy = new List<PeripheralCandidate>(data);
            gate.Release();
            return Task.FromResult(copy);
        });
        service.SetActive(true);
        Assert.True(await gate.WaitAsync(5000));
        await Task.Delay(200);
        service.RefreshNow();
        Assert.True(await gate.WaitAsync(5000));
        await Task.Delay(200);
        Assert.Equal(1, changes);   // second scan identique : pas de notification
        data[0] = C("bt:1", "A", percent: 40);
        service.RefreshNow();
        Assert.True(await gate.WaitAsync(5000));
        await Task.Delay(200);
        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task ForcedRefreshScansWhileInactive()
    {
        using var done = new SemaphoreSlim(0);
        using var service = new PeripheralBatteryService(() => done.Release(), () => Task.FromResult(new List<PeripheralCandidate> {
            C("bt:1", "A") }));
        service.RefreshNow(force: true);
        Assert.True(await done.WaitAsync(5000));
        Assert.False(service.Active);
        Assert.Single(service.Candidates);
    }

    [Fact]
    public async Task ServicePollsOnlyWhileActive()
    {
        int scans = 0;
        using var changed = new SemaphoreSlim(0);
        using var service = new PeripheralBatteryService(() => changed.Release(), () =>
        {
            Interlocked.Increment(ref scans);
            return Task.FromResult(new List<PeripheralCandidate> { C("bt:1", "A") });
        });
        service.RefreshNow();   // inactif : ignoré
        Assert.Equal(0, scans);
        service.SetActive(true);
        Assert.True(await changed.WaitAsync(5000));
        Assert.Single(service.Candidates);
        service.SetActive(false);
        Assert.False(service.Active);
        Assert.Equal(1, scans);
    }
}
