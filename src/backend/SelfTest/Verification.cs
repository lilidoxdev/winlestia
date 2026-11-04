// language: C# 12, file: Verification.cs, target: Windows 11 / .NET 8, integration checks without desktop mutations
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PanneauxGlass.SelfTest;

internal static class Verification
{
    internal static int Web()
    {
        var checks = new List<object>();
        int failed = 0;
        void Check(bool condition, string name)
        {
            checks.Add(new { Name = name, Pass = condition });
            if (!condition)
                failed++;
        }
        Store.Load();
        foreach (string invalid in new[] { "javascript:alert(1)", "file:///C:/Windows", "https://a:b@example.com" })
        {
            bool rejected = false;
            try
            {
                WebIcons.Parse(invalid);
            }
            catch (FormatException)
            {
                rejected = true;
            }
            Check(rejected, "Refus du protocole ou des identifiants : " + invalid.Split(':')[0]);
        }
        Check(WebIcons.Parse("github.com").AbsoluteUri == "https://github.com/", "HTTPS added to short addresses");
        foreach (string address in new[] { "https://mail.google.com", "https://github.com" })
        {
            var result = Task.Run(() => WebIcons.Resolve(WebIcons.Parse(address), CancellationToken.None))
                .GetAwaiter()
                .GetResult();
            var image = WebIcons.Image(result.Path) as BitmapSource;
            var rgba = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            var bytes = new byte[rgba.PixelWidth * rgba.PixelHeight * 4];
            rgba.CopyPixels(bytes, rgba.PixelWidth * 4, 0);
            int transparent = 0,
                opaque = 0;
            for (int i = 3; i < bytes.Length; i += 4)
            {
                if (bytes[i] == 0)
                    transparent++;
                if (bytes[i] > 0)
                    opaque++;
            }
            Check(
                !result.Fallback && transparent > 0 && opaque > 0 && Path.GetExtension(result.Path) == ".png",
                "Logo officiel PNG avec alpha : " + result.Name
            );
        }
        string fallback = Path.Combine(Store.Root, "web-icons", "verification-fallback.png");
        WebIcons.Default(fallback);
        Check(File.Exists(fallback) && WebIcons.Image(fallback) != null, "Logo de secours local utilisable");
        try
        {
            var fixture = Task.Run(WebFixture.Run).GetAwaiter().GetResult();
            Check(fixture.discovered, "HTML discovery, title and relative icon on a local test site");
            Check(fixture.offline, "Site unavailable: automatic fallback PNG creation");
        }
        catch (Exception ex)
        {
            Store.Log("Fixture web: " + ex);
            Check(false, "Discovery and offline fallback");
        }
        var input = new Preferences
        {
            Websites = new()
            {
                new()
                {
                    Name = "GitHub",
                    Url = "https://github.com/",
                    IconPath = fallback,
                },
            },
        };
        var roundtrip = JsonSerializer.Deserialize<Preferences>(
            JsonSerializer.Serialize(input, Store.Json),
            Store.Json
        )!;
        Check(
            roundtrip.Websites.Count == 1 && roundtrip.Websites[0].Url == input.Websites[0].Url,
            "Persistance des sites web"
        );
        input.Websites = null!;
        input.LauncherTools = null!;
        input.Normalize();
        Check(
            input.Websites != null && input.LauncherTools != null,
            "Migration of preferences predating the two bands"
        );
        File.WriteAllText(
            Path.Combine(Store.Root, "verification-web.json"),
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

    internal static int Run()
    {
        var checks = new List<object>();
        int failed = 0;
        void Check(bool condition, string name)
        {
            checks.Add(new { Name = name, Pass = condition });
            if (!condition)
                failed++;
        }
        Store.Load();
        var entries = Store.Entries;
        Check(entries.Count >= 300, "Catalogue local complet");
        Check(entries.Select(e => e.Id).Distinct().Count() == entries.Count, "Identifiants uniques");
        var local = entries.Where(e => e.Target.Length > 2 && e.Target[1] == ':');
        Check(
            local.All(e => File.Exists(e.Target) || Directory.Exists(e.Target)),
            "Toutes les cibles locales existent"
        );
        Check(
            entries.Any(e => e.Name == "Google Chrome" && Store.BoardFor(e) == "apps"),
            "Chrome est dans Applications"
        );
        Check(entries.Any(e => e.Name == "Canada" && Store.BoardFor(e) == "documents"), "Canada est dans Documents");
        Check(
            entries.Any(e => e.Name == "Cheats et scripts" && Store.BoardFor(e) == "projects"),
            "Projects are sorted"
        );
        string[] games =
        {
            "Aniimo Legend",
            "Assetto Corsa EVO",
            "FragPunk",
            "Planet Coaster 2",
            "Roblox",
            "SnowRunner",
            "Solitaire & Casual Games",
        };
        var installedGames = entries.Where(e => games.Contains(e.Name)).ToArray();
        Check(
            installedGames.Length > 0
                && installedGames.All(e =>
                    File.Exists(e.IconPath) && Path.GetExtension(e.IconPath).ToLowerInvariant() is ".png" or ".ico"
                ),
            "Logos of installed games extracted from installed resources"
        );
        Check(
            Store
                .State.Boards.Select(x => x.Id)
                .OrderBy(x => x)
                .SequenceEqual(new[] { "apps", "documents", "games", "projects" }),
            "Quatre panneaux identifiables et persistants"
        );
        var state = JsonSerializer.Deserialize<Preferences>(
            JsonSerializer.Serialize(Store.State, Store.Json),
            Store.Json
        )!;
        Check(
            state.Tint == Store.State.Tint
                && state.IconSize == Store.State.IconSize
                && state.Boards.Count == 4
                && state.Favorites.SequenceEqual(Store.State.Favorites),
            "Aller-retour complet de la personnalisation JSON"
        );
        var invalid = new Preferences
        {
            Tint = double.NaN,
            IconSize = 500,
            Boards = new() { new() { Id = "absent" } },
        };
        invalid.Normalize();
        Check(
            invalid.Tint == .2 && invalid.IconSize == 80 && invalid.Boards.Count == 4,
            "Invalid settings bounded and recovered"
        );
        Check(
            Calculator.TryEvaluate("12*7", out double product) && Math.Abs(product - 84) < 1e-9,
            "Calcul direct saisi dans la recherche"
        );
        Check(
            Calculator.TryEvaluate("(18+6)/4", out double share)
                && Math.Abs(share - 6) < 1e-9
                && Calculator.TryEvaluate("2 400 / 60", out double minutes)
                && Math.Abs(minutes - 40) < 1e-9,
            "Priorities, parentheses and spaces in the calculation"
        );
        Check(
            !Calculator.TryEvaluate("chrome", out _) && !Calculator.TryEvaluate("https://github.com", out _),
            "An ordinary search is never evaluated"
        );
        var palette = CommandPalette.Match("chrome", 8);
        Check(palette.Any(e => e.Name == "Google Chrome"), "Palette : la recherche retrouve Chrome");
        Check(CommandPalette.Match("", 5).Count() == 5, "Palette: the empty list stays bounded");
        Check(
            ClockWindow.Greeting(new DateTime(2026, 10, 24, 22, 0, 0)).Contains("Bonsoir")
                && ClockWindow.Greeting(new DateTime(2026, 10, 24, 9, 0, 0)).Contains("Bonjour"),
            "Salutation de l’horloge selon l’heure"
        );
        string alarm = FocusAlarm.EnsureFile();
        var header = new byte[4];
        using (var reader = File.OpenRead(alarm))
            reader.Read(header, 0, 4);
        Check(
            System.Text.Encoding.ASCII.GetString(header) == "RIFF" && new FileInfo(alarm).Length > 40000,
            "End chime: local PCM WAV generated"
        );
        var themed = new Preferences { Theme = "sakura" };
        themed.Normalize();
        Check(themed.Theme == "sakura", "The chosen theme survives normalization");
        var broken = new Preferences { Theme = "inexistant" };
        broken.Normalize();
        Check(broken.Theme == "nuit", "An unknown theme reverts to Nuit boréale on opening");
        Check(
            FocusTimer.DurationText(3600) == "1 h 00 min"
                && FocusTimer.DurationText(90) == "1 min 30 s"
                && FocusTimer.DurationText(25) == "25 s",
            "Free durations readable in hours, minutes and seconds"
        );
        var noteState = JsonSerializer.Deserialize<Preferences>(
            JsonSerializer.Serialize(new Preferences { Scratch = "ligne 1\nligne 2" }, Store.Json),
            Store.Json
        )!;
        Check(noteState.Scratch == "ligne 1\nligne 2", "The quick note survives JSON saving");
        string rescue = Path.Combine(Store.Root, "SECOURS_BUREAU.exe"),
            copy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SECOURS_BUREAU.exe");
        Check(File.Exists(rescue) && File.Exists(copy), "Deux copies du secours sont disponibles");
        if (File.Exists(rescue) && File.Exists(copy))
        {
            Check(
                SHA256.HashData(File.ReadAllBytes(rescue)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(copy))),
                "Copie de secours identique dans Documents"
            );
            using var reader = new BinaryReader(File.OpenRead(rescue));
            reader.BaseStream.Position = 0x3c;
            int pe = reader.ReadInt32();
            reader.BaseStream.Position = pe + 24 + 112 + 14 * 8;
            Check(reader.ReadUInt32() == 0, "Native rescue independent of the .NET runtime");
        }
        Check(File.Exists(Path.Combine(Store.Root, "bureau-avant-glass.tsv")), "Original backup of positions kept");
        var report = new
        {
            At = DateTimeOffset.Now,
            Passed = checks.Count - failed,
            Failed = failed,
            Checks = checks,
        };
        File.WriteAllText(Path.Combine(Store.Root, "verification.json"), JsonSerializer.Serialize(report, Store.Json));
        return failed == 0 ? 0 : 1;
    }
}
