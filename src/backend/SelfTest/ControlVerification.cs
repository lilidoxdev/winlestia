// language: C# 12, file: ControlVerification.cs, target: Windows 11 / .NET 8, isolated configuration and live readback checks
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace PanneauxGlass.SelfTest;

internal static class ControlVerification
{
    internal static async Task<int> Run(bool writeTest)
    {
        var checks = new List<object>();
        int failed = 0;
        void Check(bool pass, string name)
        {
            checks.Add(new { Name = name, Pass = pass });
            if (!pass)
                failed++;
        }
        var saved = Store.State;
        object? live = null;
        try
        {
            foreach (
                var size in new[]
                {
                    new Size(3440, 1440),
                    new Size(1920, 1080),
                    new Size(1366, 768),
                    new Size(1280, 720),
                }
            )
            {
                var work = new Rect(-100, 50, size.Width, size.Height);
                var p = ControlPairLayout.Calculate(work, true);
                Check(
                    work.Contains(p.Home)
                        && work.Contains(p.Controls)
                        && p.Controls.Left > p.Home.Right
                        && Math.Abs((p.Home.Left + p.Controls.Right) / 2 - (work.Left + work.Width / 2)) < .01,
                    "Centered pair without overlap: " + size
                );
            }
            var defaults = new ControlPreferences();
            Check(
                defaults.Modules.Count == 5
                    && defaults.Shortcuts.Select(s => s.Id).Distinct().Count() == defaults.Shortcuts.Count,
                "Cinq modules et raccourcis identifiables"
            );
            var preferences = JsonSerializer.Deserialize<Preferences>("{\"Favorites\":[\"steam\"]}", Store.Json)!;
            preferences.Normalize();
            Check(
                preferences.Controls.Modules.Count == 5 && preferences.Favorites.Single() == "steam",
                "Migration 5.2 sans perte des favoris"
            );
            preferences.Controls.Modules.Reverse();
            preferences.Controls.HiddenModules.Add("connections");
            preferences.Controls.ShowBrightness = false;
            preferences.Controls.Shortcuts.Add(
                new()
                {
                    Name = "Custom screen",
                    Target = "ms-settings:display",
                    Arguments = "",
                    Glyph = "\uE7F4",
                }
            );
            var copy = JsonSerializer.Deserialize<Preferences>(
                JsonSerializer.Serialize(preferences, Store.Json),
                Store.Json
            )!;
            Check(
                copy.Controls.Modules.SequenceEqual(preferences.Controls.Modules)
                    && copy.Controls.HiddenModules.Contains("connections")
                    && !copy.Controls.ShowBrightness
                    && copy.Controls.Shortcuts.Last().Name == "Custom screen",
                "Persistence of modules, order, icons and shortcuts"
            );
            var invalid = new ControlPreferences
            {
                Modules = new() { "invalid", "media", "media" },
                HiddenModules = new() { "invalid", "levels" },
            };
            invalid.Normalize();
            Check(
                invalid.Modules.Count == 5
                    && invalid.Modules.Distinct().Count() == 5
                    && invalid.HiddenModules.SequenceEqual(new[] { "levels" }),
                "Invalid module configuration recovered"
            );
            Check(
                WindowsShortcutEditor.ValidTarget("ms-settings:display")
                    && WindowsShortcutEditor.ValidTarget(@"C:\Program Files\Application\app.exe")
                    && !WindowsShortcutEditor.ValidTarget("javascript:alert(1)")
                    && !WindowsShortcutEditor.ValidTarget("x\ncalc.exe"),
                "Windows addresses and paths with spaces accepted, invalid targets refused"
            );
            Check(
                ControlCenterWindow.Time(125) == "2:05"
                    && ControlCenterWindow.Time(3601) == "1:00:01"
                    && ControlCenterWindow.Time(double.NaN) == "0:00",
                "Music durations and missing duration"
            );
            using var audio = new SystemAudio();
            var initial = audio.Read();
            Check(
                initial.Available && initial.Volume is >= 0 and <= 100 && !string.IsNullOrWhiteSpace(initial.Device),
                "Reading Windows volume and device"
            );
            if (writeTest && initial.Available)
            {
                double test = initial.Volume > 1 ? initial.Volume - 1 : initial.Volume + 1;
                try
                {
                    audio.SetVolume(test);
                    var readback = audio.Read();
                    Check(
                        readback.Available && Math.Abs(readback.Volume - test) < .1,
                        "Real volume change and read-back"
                    );
                }
                finally
                {
                    audio.SetVolume(initial.Volume);
                    audio.SetMute(initial.Muted);
                }
                var restored = audio.Read();
                Check(
                    Math.Abs(restored.Volume - initial.Volume) < .1 && restored.Muted == initial.Muted,
                    "Volume and mute restored after the test"
                );
            }
            var network = await new WindowsConnections().ReadAsync();
            Check(string.IsNullOrEmpty(network.Error), "Lecture des radios Wi-Fi/Bluetooth et de la connexion");
            Native.GetCursorPos(out var point);
            var bright = await MonitorBrightness.ReadAsync(point);
            Check(
                !bright.Available || bright.Value is >= 0 and <= 100,
                "Brightness read or unavailability explicitly reported"
            );
            if (writeTest && bright.Available)
            {
                double test = bright.Value > 1 ? bright.Value - 1 : bright.Value + 1;
                try
                {
                    await MonitorBrightness.SetAsync(point, test);
                    var readback = await MonitorBrightness.ReadAsync(point);
                    Check(
                        readback.Available && Math.Abs(readback.Value - test) < 1.1,
                        "Real brightness change and read-back"
                    );
                }
                finally
                {
                    await MonitorBrightness.SetAsync(point, bright.Value);
                }
                var restored = await MonitorBrightness.ReadAsync(point);
                Check(
                    restored.Available && Math.Abs(restored.Value - bright.Value) < 1.1,
                    "Brightness restored after the test"
                );
            }
            Store.State = new Preferences();
            using var media = new MediaController();
            await media.StartAsync();
            var song = await media.ReadAsync();
            Check(song != null, "Access to Windows music sessions");
            if (song?.Available == true)
                Check(
                    !string.IsNullOrWhiteSpace(song.Title) && song.Position >= 0 && song.Position <= song.Duration,
                    "Real title and bounded music progress"
                );
            media.Stop();
            Check(await media.ReadAsync() == null, "Music playback suspended when the panel is hidden");
            live = new
            {
                Audio = new
                {
                    initial.Available,
                    initial.Volume,
                    initial.Muted,
                    initial.Device,
                },
                Brightness = new
                {
                    bright.Available,
                    bright.Value,
                    bright.Device,
                },
                Network = new
                {
                    WifiAvailable = network.Wifi.Present,
                    BluetoothAvailable = network.Bluetooth.Present,
                    network.Internet,
                },
                Media = new
                {
                    Available = song?.Available,
                    App = song?.App,
                    Artwork = song?.Artwork != null,
                },
            };
        }
        catch (Exception ex)
        {
            Check(false, ex.ToString());
        }
        finally
        {
            Store.State = saved;
        }
        File.WriteAllText(
            Path.Combine(Store.Root, writeTest ? "verification-controls-write.json" : "verification-controls.json"),
            JsonSerializer.Serialize(
                new
                {
                    At = DateTimeOffset.Now,
                    Passed = checks.Count - failed,
                    Failed = failed,
                    WriteTest = writeTest,
                    Live = live,
                    Checks = checks,
                },
                Store.Json
            )
        );
        return failed == 0 ? 0 : 1;
    }
}
