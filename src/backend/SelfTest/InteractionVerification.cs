// language: C# 12, file: InteractionVerification.cs, target: Windows 11 / .NET 8 WPF, native binding and interruption regression checks
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace PanneauxGlass.SelfTest;

internal static class InteractionVerification
{
    internal static int Shortcuts()
    {
        int failed = 0;
        var checks = new List<object>();
        void Check(bool pass, string name)
        {
            checks.Add(new { Name = name, Pass = pass });
            if (!pass)
                failed++;
        }
        Window first = new Window(),
            second = new Window();
        var a = new WindowInteropHelper(first).EnsureHandle();
        var b = new WindowInteropHelper(second).EnsureHandle();
        try
        {
            var settings = JsonSerializer.Deserialize<Preferences>(
                "{\"Favorites\":[\"keep\"],\"Shortcut\":null}",
                Store.Json
            )!;
            settings.Normalize();
            Check(
                settings.Shortcut.Key == 0x70
                    && settings.Shortcut.Modifiers == 2
                    && settings.Favorites.Single() == "keep",
                "Migration: Ctrl F1 added without losing customization"
            );
            Check(
                GlobalShortcut.Valid(2, 0x70)
                    && GlobalShortcut.Valid(7, 0x41)
                    && !GlobalShortcut.Valid(0, 0x70)
                    && !GlobalShortcut.Valid(2, 0x7B)
                    && !GlobalShortcut.Valid(3, 0x08),
                "Valid combinations accepted, bare keys and reserved F12 refused"
            );
            using (var binding = new GlobalShortcut(a, new() { Modifiers = 7, Key = 0x86 }))
            {
                Check(binding.Error == "", "Real Windows registration of the test shortcut");
                Check(Native.RegisterHotKey(b, 90, 0x4007, 0x85), "Concurrent reservation of a different combination");
                try
                {
                    Check(
                        !binding.TrySet(7, 0x85, out var error) && !string.IsNullOrWhiteSpace(error),
                        "Conflict detected and explained"
                    );
                    bool stolen = Native.RegisterHotKey(b, 91, 0x4007, 0x86);
                    Check(!stolen, "The previous shortcut stays reserved after a failure");
                    if (stolen)
                        Native.UnregisterHotKey(b, 91);
                }
                finally
                {
                    Native.UnregisterHotKey(b, 90);
                }
                Check(binding.TrySet(7, 0x84, out _), "Remplacement atomique par une combinaison libre");
                bool released = Native.RegisterHotKey(b, 92, 0x4007, 0x86);
                Check(released, "The old combination is released after success");
                if (released)
                    Native.UnregisterHotKey(b, 92);
            }
            bool disposed = Native.RegisterHotKey(b, 93, 0x4007, 0x84);
            Check(disposed, "Shortcuts are released on shutdown");
            if (disposed)
                Native.UnregisterHotKey(b, 93);
            Check(
                new Preferences().PaletteShortcut.Modifiers == 2 && new Preferences().PaletteShortcut.Key == 0x71,
                "Quick command: Ctrl F2 by default, never Ctrl K"
            );
            Check(
                Preferences
                    .DefaultQuickTabs()
                    .Where(t => t.Target.StartsWith("glass:"))
                    .All(t => QuickTabsWindow.Views.Any(v => v.Target == t.Target)),
                "Quick tabs: each default target is resolved by the editor"
            );
            Check(
                Preferences.DefaultQuickTabs().All(t => GlobalShortcut.Label(2, 0x71).Length > 0),
                "Shortcut and tabs coexist in a single preference"
            );
            Check(
                Theme.Catalogue.Count >= 4
                    && Theme.Catalogue.Select(t => t.Id).Distinct().Count() == Theme.Catalogue.Count,
                "At least four global themes, unique identifiers"
            );
            Check(
                Theme.Catalogue.All(t => t.Surface != t.HaloA && t.Accent != t.Text && t.RadiusScale is > 0 and <= 2),
                "Each theme provides glass, halos, accent and corner scale"
            );
            Check(
                Theme.Catalogue.Count(t => t.Kind != SurfaceKind.Glass) >= 4,
                "At least four themes change the material and shapes"
            );
            Check(
                Theme.Catalogue.Select(t => t.Tiles).Distinct().Count() >= 6
                    && Theme.Catalogue.Select(t => t.Heading).Distinct().Count() >= 4
                    && Theme.Catalogue.Select(t => t.Frame).Distinct().Count() >= 5,
                "Themes also vary layout, typography and control frame"
            );
            Check(
                new[] { "bento", "neon", "retro", "suisse", "zen" }.All(id => Theme.Find(id) != null),
                "Five additional materials are offered"
            );
            Check(
                Theme.Find("bento")!.Tiles == "bento" && Theme.Find("bento")!.Frame == "flat",
                "Bento: solid cells and flat controls"
            );
            Check(
                Theme.Find("neon")!.Frame == "neon" && Theme.Find("neon")!.ReflectionGain == 0,
                "Neon: glowing frames, no reflection"
            );
            Check(
                Theme.Find("retro")!.Heading == "pixel" && Theme.Find("retro")!.Frame == "bevel",
                "Retro: pixel typography and beveled buttons"
            );
            Check(
                Theme.Find("suisse")!.Tiles == "index" && Theme.Find("suisse")!.Frame == "outline",
                "Swiss: numbered index and wireframe frames"
            );
            Check(
                Theme.Find("zen")!.Tiles == "stack" && Theme.Find("zen")!.Heading == "serif",
                "Zen: wide rows and serif titles"
            );
            Theme.Use("amethyste");
            Check(
                UI.White.Color == Theme.Current.Text && Theme.Current.Id == "amethyste",
                "Changing theme recolors the shared text"
            );
            Theme.Use("calque");
            Check(
                Theme.Current.Light
                    && UI.White.Color == Theme.Current.Text
                    && Theme.Current.Tiles == "cards"
                    && Theme.Current.Heading == "serif",
                "Tracing paper: dark ink, serif titles and cards"
            );
            Theme.Use("encre");
            Check(
                UI.Font == UI.Mono && Theme.Current.Tiles == "list" && Theme.Current.Kind == SurfaceKind.Ink,
                "Encre : chasse fixe et listes"
            );
            Theme.Use("inconnu");
            Check(Theme.Current.Id == "nuit", "An unknown theme falls back to Nuit boréale");
            foreach (var dimensions in new[] { new Size(3440, 1440), new Size(1920, 1080), new Size(1366, 768) })
            {
                var work = new Rect(-50, 20, dimensions.Width, dimensions.Height);
                var left = ControlPairLayout.Calculate(work, false, true);
                Check(
                    work.Contains(left.Pc) && work.Contains(left.Home) && left.Pc.Right < left.Home.Left,
                    "PC panel on the left without overlap: " + dimensions
                );
                if (dimensions.Width >= 1700)
                {
                    var both = ControlPairLayout.Calculate(work, true, true);
                    Check(
                        work.Contains(both.Pc)
                            && work.Contains(both.Controls)
                            && both.Pc.Right < both.Home.Left
                            && both.Home.Right < both.Controls.Left,
                        "Three surfaces within the screen: " + dimensions
                    );
                }
            }
        }
        catch (Exception ex)
        {
            Check(false, ex.ToString());
        }
        finally
        {
            first.Close();
            second.Close();
        }
        File.WriteAllText(
            Path.Combine(Store.Root, "verification-shortcuts-5.5.json"),
            JsonSerializer.Serialize(
                new
                {
                    At = DateTimeOffset.Now,
                    Passed = checks.Count - failed,
                    Failed = failed,
                    Checks = checks,
                },
                Store.Json
            )
        );
        return failed == 0 ? 0 : 1;
    }

    internal static async void Run(Coordinator app)
    {
        int failed = 0;
        var checks = new List<object>();
        var measurements = new List<object>();
        void Check(bool pass, string name)
        {
            checks.Add(new { Name = name, Pass = pass });
            if (!pass)
                failed++;
        }
        async Task Measure(string name, int milliseconds)
        {
            using var process = Process.GetCurrentProcess();
            var start = process.TotalProcessorTime;
            var clock = Stopwatch.StartNew();
            await Task.Delay(milliseconds);
            process.Refresh();
            measurements.Add(
                new
                {
                    Name = name,
                    ElapsedMs = clock.Elapsed.TotalMilliseconds,
                    CpuMs = (process.TotalProcessorTime - start).TotalMilliseconds,
                    TotalCpuPercent = (process.TotalProcessorTime - start).TotalMilliseconds
                        / clock.Elapsed.TotalMilliseconds
                        / Environment.ProcessorCount
                        * 100,
                    WorkingSetMB = process.WorkingSet64 / 1048576d,
                    PrivateMB = process.PrivateMemorySize64 / 1048576d,
                    Samples = app.PcActivity.samples,
                    Sampling = app.PcActivity.running,
                }
            );
        }
        try
        {
            app.ShowHome();
            await Task.Delay(500);
            await Measure("Accueil stable", 8000);
            app.Collapse();
            await Task.Delay(20);
            app.Open();
            await Task.Delay(350);
            Check(
                app.UiActivity.open && !app.UiActivity.closing && app.UiActivity.visible >= 3,
                "Reopening during collapse: last intent honored"
            );
            for (int i = 0; i < 8; i++)
            {
                app.ToggleControls();
                await Task.Delay(28);
            }
            await Task.Delay(400);
            Check(app.UiActivity.open && !app.UiActivity.closing, "Eight rapid toggles leave no deferred close");
            for (int i = 0; i < 4; i++)
            {
                app.TogglePc();
                await Task.Delay(15);
            }
            app.TogglePc();
            for (int wait = 0; wait < 40 && app.PcActivity.samples == 0; wait++)
                await Task.Delay(500);
            Check(
                app.PcActivity.samples > 0 && app.PcActivity.running,
                "Rapid reopenings: one active collector and restart possible"
            );
            await Measure("Home + PC status", 12000);
            app.TogglePc();
            await Task.Delay(1500);
            long samples = app.PcActivity.samples;
            await Measure("Home after PC collapse", 6000);
            Check(
                !app.PcActivity.running && app.PcActivity.samples == samples,
                "No reading or sensor loop after hiding"
            );
            app.ShowWorkspace();
            await Task.Delay(350);
            app.ShowHome();
            await Task.Delay(350);
            Check(
                app.UiActivity.open && app.UiActivity.visible == 4,
                "Back from the four panels: home, clock and two arrows only"
            );
            app.CollapseImmediately();
            await Task.Delay(250);
            app.ShowPalette();
            await Task.Delay(400);
            Check(app.PaletteActivity && app.PaletteVisible, "Quick command available with launcher collapsed");
            app.ClosePalette();
            await Task.Delay(250);
            Check(!app.PaletteActivity && !app.PaletteVisible, "Quick command closed without leaving a ghost surface");
            app.ShowPalette();
            await Task.Delay(300);
            app.CollapseImmediately();
            await Task.Delay(250);
            Check(!app.PaletteVisible, "Replier l’espace ferme aussi la palette ouverte");
            var editor = new QuickTabsWindow(app) { ShowInTaskbar = true };
            editor.Show();
            await Task.Delay(350);
            bool editorOpened = editor.IsVisible;
            app.CollapseImmediately();
            await Task.Delay(350);
            Check(editorOpened && !editor.IsVisible, "Collapsing the space also closes the tab editor");
            await Measure("Interface collapsed", 6000);
            Check(
                !app.UiActivity.open && !app.UiActivity.closing && app.UiActivity.visible == 0,
                "Repli complet de toutes les surfaces"
            );
        }
        catch (Exception ex)
        {
            Check(false, ex.ToString());
        }
        File.WriteAllText(
            Path.Combine(Store.Root, "verification-interactions-5.5.json"),
            JsonSerializer.Serialize(
                new
                {
                    At = DateTimeOffset.Now,
                    Passed = checks.Count - failed,
                    Failed = failed,
                    Checks = checks,
                    Measurements = measurements,
                },
                Store.Json
            )
        );
        Application.Current.Shutdown(failed == 0 ? 0 : 1);
    }
}
