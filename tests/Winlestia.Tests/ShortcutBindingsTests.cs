// C#12 / ShortcutBindings : aiguillage Resolve (secours > palette > paramètres > fond > épingle > capture > bascule) et
// réaffectations qui ne persistent que si Windows accepte. RegisterHotKey est remplacé par un registre en mémoire.
using System;
using System.Collections.Generic;
using System.Linq;
using PanneauxGlass;
namespace Winlestia.Tests;
public class ShortcutBindingsTests
{
    sealed class Rig : IDisposable
    {
        internal readonly Dictionary<int, (uint, uint)> Registered = new();
        internal readonly HashSet<(uint, uint)> Foreign = new();
        internal readonly ShortcutBindings Bindings;
        internal int RailRebuilds, LabelRefreshes;
        readonly TempDir dir = new();
        readonly Preferences state = Store.State;
        readonly string? env = Environment.GetEnvironmentVariable("WINLESTIA_DATA_DIR");

        internal Rig()
        {
            Environment.SetEnvironmentVariable("WINLESTIA_DATA_DIR", dir.Path);
            Store.State = new Preferences();
            GlobalShortcut.TestRegister = (w, id, m, k) =>
            {
                var key = (m & ~0x4000u, k);
                if (Foreign.Contains(key) || Registered.ContainsKey(id) || Registered.Values.Contains(key))
                    return 1409;
                Registered[id] = key;
                return 0;
            };
            GlobalShortcut.TestUnregister = (w, id) => Registered.Remove(id);
            var p = Store.State;
            var global = new GlobalShortcut(IntPtr.Zero, p.Shortcut, p.PaletteShortcut, p.PinShortcut, p.CaptureShortcut,
                p.WallpaperShortcut, p.SettingsShortcut);
            Bindings = new ShortcutBindings(global, () => RailRebuilds++, () => LabelRefreshes++);
        }

        public void Dispose()
        {
            GlobalShortcut.TestRegister = null;
            GlobalShortcut.TestUnregister = null;
            Store.State = state;
            Environment.SetEnvironmentVariable("WINLESTIA_DATA_DIR", env);
            dir.Dispose();
        }
    }

    [Fact]
    public void EveryBindingResolvesToItsOwnAction()
    {
        using var rig = new Rig();
        var byAction = Enumerable.Range(0, 40).GroupBy(rig.Bindings.Resolve).Where(g => g.Key != HotkeyAction.None)
            .ToDictionary(g => g.Key, g => g.ToArray());
        foreach (var action in Enum.GetValues<HotkeyAction>().Where(a => a != HotkeyAction.None))
            Assert.True(byAction.ContainsKey(action), "action sans identifiant : " + action);
        // La bascule garde l'identifiant historique 1 en plus de son identifiant courant.
        Assert.All(byAction.Where(kv => kv.Key != HotkeyAction.Toggle), kv => Assert.Single(kv.Value));
        Assert.Contains(1, byAction[HotkeyAction.Toggle]);
    }

    [Fact]
    public void RescueIsFixedAndTheLegacyIdToggles()
    {
        using var rig = new Rig();
        Assert.Equal(HotkeyAction.Rescue, rig.Bindings.Resolve(ShortcutBindings.RescueId));
        Assert.Equal(HotkeyAction.Toggle, rig.Bindings.Resolve(1));
        Assert.Equal(HotkeyAction.None, rig.Bindings.Resolve(0));
        Assert.Equal(HotkeyAction.None, rig.Bindings.Resolve(-1));
        Assert.Equal(HotkeyAction.None, rig.Bindings.Resolve(9999));
    }

    [Fact]
    public void RegisteredIdsFollowTheDocumentedSlots()
    {
        using var rig = new Rig();
        // Pin 7/8, capture 9/10, fond 11/12, paramètres 13/14 : les identifiants alternent pour permettre une réaffectation atomique.
        Assert.Contains(HotkeyAction.Pin, new[] { 7, 8 }.Select(rig.Bindings.Resolve));
        Assert.Contains(HotkeyAction.Capture, new[] { 9, 10 }.Select(rig.Bindings.Resolve));
        Assert.Contains(HotkeyAction.Wallpaper, new[] { 11, 12 }.Select(rig.Bindings.Resolve));
        Assert.Contains(HotkeyAction.Settings, new[] { 13, 14 }.Select(rig.Bindings.Resolve));
    }

    [Fact]
    public void ReassigningAlternatesTheIdAndKeepsExactlyOneActiveSlot()
    {
        using var rig = new Rig();
        int Slot() => new[] { 11, 12 }.Single(id => rig.Bindings.Resolve(id) == HotkeyAction.Wallpaper);
        int before = Slot();
        Assert.True(rig.Bindings.SetWallpaperShortcut(3, 0x4B, out string error), error);
        Assert.NotEqual(before, Slot());
        Assert.Equal(HotkeyAction.None, rig.Bindings.Resolve(before));
    }

    [Fact]
    public void AcceptedPaletteShortcutIsPersistedAndRefreshesLabels()
    {
        using var rig = new Rig();
        Assert.True(rig.Bindings.SetPaletteShortcut(3, 0x4B, out string error), error);
        Assert.Equal((3u, 0x4Bu), (Store.State.PaletteShortcut.Modifiers, Store.State.PaletteShortcut.Key));
        Assert.Equal(1, rig.LabelRefreshes);
        Assert.Equal(GlobalShortcut.Label(3, 0x4B), rig.Bindings.PaletteLabel);
    }

    [Fact]
    public void RefusedShortcutIsNeitherPersistedNorAnnounced()
    {
        using var rig = new Rig();
        var before = (Store.State.PinShortcut.Modifiers, Store.State.PinShortcut.Key);
        rig.Foreign.Add((3u, 0x4Bu));
        Assert.False(rig.Bindings.SetPinShortcut(3, 0x4B, out string error));
        Assert.NotEmpty(error);
        Assert.Equal(before, (Store.State.PinShortcut.Modifiers, Store.State.PinShortcut.Key));
        Assert.Equal(0, rig.LabelRefreshes);
    }

    [Fact]
    public void CaptureAndHomeShortcutsRefreshTheRightUi()
    {
        using var rig = new Rig();
        Assert.True(rig.Bindings.SetCaptureShortcut(3, 0x4C, out string error), error);
        Assert.Equal(1, rig.LabelRefreshes);
        Assert.Equal(0, rig.RailRebuilds);
        Assert.True(rig.Bindings.SetShortcut(3, 0x4D, "home", out error), error);
        Assert.Equal(1, rig.RailRebuilds);
        Assert.Equal(1, rig.LabelRefreshes);
        Assert.Equal((3u, 0x4Du), (Store.State.Shortcut.Modifiers, Store.State.Shortcut.Key));
    }

    [Fact]
    public void WallpaperShortcutIsPersistedWithoutLabelRefresh()
    {
        using var rig = new Rig();
        Assert.True(rig.Bindings.SetWallpaperShortcut(3, 0x4E, out string error), error);
        Assert.Equal(0x4Eu, Store.State.WallpaperShortcut.Key);
        Assert.Equal(0, rig.LabelRefreshes);
    }

    [Fact]
    public void DisposeReleasesEveryRegistration()
    {
        var rig = new Rig();
        Assert.NotEmpty(rig.Registered);
        rig.Bindings.Dispose();
        Assert.Empty(rig.Registered);
        rig.Dispose();
    }
}
