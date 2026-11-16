// C#12 / Normalisation de la disposition du dock et choix des radios à rallumer après le mode avion (fonctions pures).
using System.Linq;
using PanneauxGlass;
using Xunit;
namespace Winlestia.Tests;
public class DockItemsTests
{
    [Fact]
    public void NullListsGiveDefaults()
    {
        var (bar, drawer) = DockItems.Normalise(null, null);
        Assert.Equal(DockItems.DefaultBar, bar);
        Assert.Equal(DockItems.DefaultDrawer, drawer);
    }

    [Fact]
    public void UnknownAndDuplicatesAreDropped_FirstWinsBarBeforeDrawer()
    {
        var (bar, drawer) = DockItems.Normalise(new[] { "volume", "nope", "volume", "settings" }, new[] { "volume", "wifi", "wifi", "" });
        Assert.Equal(new[] { "volume", "settings" }, bar);
        Assert.Equal(new[] { "wifi" }, drawer);
    }

    [Fact]
    public void LauncherInDrawerMovesToBar()
    {
        var (bar, drawer) = DockItems.Normalise(new[] { "settings" }, new[] { "wifi", "files", "search" });
        Assert.Equal(new[] { "settings", "files", "search" }, bar);
        Assert.Equal(new[] { "wifi" }, drawer);
    }

    [Fact]
    public void SettingsIsReAddedToBar()
    {
        var (bar, drawer) = DockItems.Normalise(new[] { "start" }, new[] { "wifi" });
        Assert.Equal(new[] { "start", "settings" }, bar);
        Assert.Equal(new[] { "wifi" }, drawer);
    }

    [Fact]
    public void SettingsInDrawerIsKept()
    {
        var (bar, drawer) = DockItems.Normalise(new[] { "start" }, new[] { "settings" });
        Assert.Equal(new[] { "start" }, bar);
        Assert.Equal(new[] { "settings" }, drawer);
    }

    [Fact]
    public void OrderIsKept()
    {
        var (bar, drawer) = DockItems.Normalise(new[] { "power", "settings", "start" }, new[] { "keepAwake", "wifi", "focus" });
        Assert.Equal(new[] { "power", "settings", "start" }, bar);
        Assert.Equal(new[] { "keepAwake", "wifi", "focus" }, drawer);
    }

    [Fact]
    public void DefaultsAreValidAndStable()
    {
        var (bar, drawer) = DockItems.Normalise(DockItems.DefaultBar, DockItems.DefaultDrawer);
        Assert.Equal(DockItems.DefaultBar, bar);
        Assert.Equal(DockItems.DefaultDrawer, drawer);
        Assert.All(bar.Concat(drawer), id => Assert.Contains(id, DockItems.All));
    }

    [Fact]
    public void AirplaneRestoresRememberedRadios()
    {
        var all = new[] { "wifi", "bluetooth", "cellular" };
        Assert.Equal(new[] { "bluetooth" }, DockItems.RadiosToRestore(new[] { "bluetooth" }, all, r => r, r => r != "cellular"));
    }

    [Fact]
    public void AirplaneFallsBackToWifiAndBluetooth()
    {
        var all = new[] { "wifi", "bluetooth", "cellular" };
        Assert.Equal(new[] { "wifi", "bluetooth" }, DockItems.RadiosToRestore(null, all, r => r, r => r != "cellular"));
        Assert.Equal(new[] { "wifi", "bluetooth" }, DockItems.RadiosToRestore(System.Array.Empty<string>(), all, r => r, r => r != "cellular"));
        Assert.Equal(new[] { "wifi", "bluetooth" }, DockItems.RadiosToRestore(new[] { "gone" }, all, r => r, r => r != "cellular"));
    }
}
