// language: C# 12, file: ShellVerification.cs, target: Windows 11 / .NET 8, shell regression checks
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;

namespace PanneauxGlass.SelfTest;

internal static class ShellVerification
{
    internal static int Run()
    {
        var app = new Application();
        Store.Load();
        UI.Install();
        int failed = 0;
        var checks = new List<object>();
        void Check(bool result, string name)
        {
            checks.Add(new { Name = name, Pass = result });
            if (!result)
                failed++;
        }
        var original = Store.State;
        var state = JsonSerializer.Deserialize<Preferences>(
            JsonSerializer.Serialize(original, Store.Json),
            Store.Json
        )!;
        Store.State = state;
        state.ShellRevision = 0;
        var favorites = state.Favorites.ToArray();
        var recent = state.Recent.ToArray();
        var websites = state.Websites.Select(s => s.Id).ToArray();
        string old = state.Theme;
        ShellSettings.Migrate(false);
        Check(
            state.ShellRevision == 1 && state.Theme == "shell" && state.PreviousTheme == old,
            "Migration to the native theme with old theme kept"
        );
        Check(
            favorites.SequenceEqual(state.Favorites)
                && recent.SequenceEqual(state.Recent)
                && websites.SequenceEqual(state.Websites.Select(s => s.Id)),
            "Migration: favorites, recents and sites kept"
        );
        state.ReplaceTaskbar = false;
        state.Theme = "shell-dark";
        ShellSettings.Migrate(false);
        Check(!state.ReplaceTaskbar && state.Theme == "shell-dark", "Idempotent migration: later choices kept");
        Check(
            Theme.Find("shell")!.Light && Theme.Find("shell")!.Opaque && !Theme.Find("shell-dark")!.Light,
            "Native light/dark themes without invisible glass composition"
        );
        Check(Marshal.SizeOf<TaskbarNative.BarData>() == 48, "AppBar x64 structure conforms to the Windows ABI");
        var lease = TaskbarNative.Snapshot();
        Check(lease.State is >= 0 and <= 3, "Real, non-mutating read of the Windows taskbar state");
        foreach (MotionKind kind in Enum.GetValues<MotionKind>())
        {
            var curve = Motion.Curve(kind);
            double previous = -1;
            bool valid = Math.Abs(curve.Ease(0)) < .001 && Math.Abs(curve.Ease(1) - 1) < .001;
            for (int i = 0; i <= 100; i++)
            {
                double sample = curve.Ease(i / 100d);
                valid &= double.IsFinite(sample) && sample >= previous - .00001;
                previous = sample;
            }
            Check(valid, "Continuous curve without backtracking: " + kind);
        }
        var graph = new TelemetryGraph("#A77967");
        graph.Measure(new Size(300, 80));
        graph.Arrange(new Rect(0, 0, 300, 80));
        graph.Push(30);
        graph.Push(45);
        Theme.Use("shell-dark");
        Check(!UI.Brush("#A77967").IsFrozen, "Charts do not freeze shared brushes during a theme change");
        var bounds = new Rect(0, 0, 1920, 1080);
        var layout = ControlPairLayout.Calculate(bounds, false, false, false);
        Check(
            layout.Home.Top >= 0 && layout.Home.Bottom <= bounds.Bottom && layout.Home.Width <= bounds.Width,
            "Native layout without an extra clock strip"
        );
        state.MotionLevel = 0;
        Check(!Motion.On, "Reduced motion: animations disabled");
        Store.State = original;
        File.WriteAllText(
            Path.Combine(Store.Root, "verification-shell.json"),
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
}
