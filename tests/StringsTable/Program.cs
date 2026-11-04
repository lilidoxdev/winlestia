// UI string table: every key has French and English, French values are the historical literals,
// and the language is resolved from <dataDir>\Caelestia\language.json, then the UI culture, then English.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using PanneauxGlass.Views;

class Test
{
    static int failures;

    static void Check(bool ok, string name)
    {
        if (!ok)
        {
            failures++;
            Console.WriteLine("FAIL " + name);
        }
    }

    static string Dir(string? json)
    {
        string d = Path.Combine(Path.GetTempPath(), "strings-table-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(d, "Caelestia"));
        if (json != null)
            File.WriteAllText(Path.Combine(d, "Caelestia", "language.json"), json);
        return d;
    }

    static int Main()
    {
        Check(Strings.All.Count > 100, "table is populated");
        foreach (var (key, v) in Strings.All)
        {
            Check(!string.IsNullOrWhiteSpace(v.Fr), "fr non-empty: " + key);
            Check(!string.IsNullOrWhiteSpace(v.En), "en non-empty: " + key);
        }
        var props = typeof(Strings)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name)
            .ToHashSet();
        foreach (string key in Strings.All.Keys)
            // Format templates ({0}) are reached through methods, dotted keys are looked up by prefix (CategoryLabel, KnownFolder).
            if (!key.Contains('.') && !Strings.All[key].Fr.Contains('{'))
                Check(props.Contains(key), "property exists: " + key);

        // French values are byte-identical to the original literals.
        Check(Strings.Get("Close", "fr") == "Fermer", "fr Close");
        Check(Strings.Get("Restart", "fr") == "Redémarrer", "fr Restart");
        Check(Strings.Get("ShutDown", "fr") == "Éteindre", "fr ShutDown");
        Check(
            Strings.Get("AddAFolderToYourSpace", "fr") == "Ajouter un dossier à ton espace",
            "fr AddAFolderToYourSpace"
        );
        Check(
            Strings.Get("PanneauxGlassControlCenter", "fr") == "Panneaux Glass — centre de contrôle",
            "fr PanneauxGlassControlCenter"
        );
        Check(Strings.Get("Close", "en") == "Close", "en Close");
        Check(Strings.Get("Close", "de") == "Close", "unknown language falls back to English");
        Check(Strings.Get("Restart", "es") == "Reiniciar", "es where translated");
        Check(Strings.Get("AddAFolderToYourSpace", "es") == "Add a folder to your space", "es falls back to en");

        // Fragments that are concatenated keep their boundary spaces and placeholders in every language.
        foreach (var (key, v) in Strings.All)
        {
            Check(v.Fr.StartsWith(' ') == v.En.StartsWith(' '), "en leading space: " + key);
            Check(v.Fr.EndsWith(' ') == v.En.EndsWith(' '), "en trailing space: " + key);
            foreach (string ph in new[] { "{0}", "{1}" })
                Check(v.Fr.Contains(ph) == v.En.Contains(ph), "en placeholder " + ph + ": " + key);
        }

        // Dotted lookups fall back to the original name when the key is missing.
        Check(Strings.KnownFolder("Desktop") == Strings.Get("Folder.Desktop", Strings.Language), "KnownFolder known");
        Check(Strings.KnownFolder("NoSuchFolder") == "NoSuchFolder", "KnownFolder missing returns the name");
        Check(Strings.CategoryLabel("NoSuchCategory") == "NoSuchCategory", "CategoryLabel missing returns the name");

        // Language resolution.
        Check(Strings.Resolve(Dir("{\"language\":\"en\"}"), "fr") == "en", "file en beats culture fr");
        Check(Strings.Resolve(Dir("{\"language\":\"fr\"}"), "en") == "fr", "file fr beats culture en");
        Check(Strings.Resolve(Dir("{\"language\":\"auto\"}"), "fr") == "fr", "auto uses culture fr");
        Check(Strings.Resolve(Dir(null), "fr") == "fr", "missing file uses culture fr");
        Check(Strings.Resolve(Dir(null), "de") == "en", "unsupported culture falls back to en");
        Check(Strings.Resolve(Dir("not json"), "es") == "es", "corrupt file uses culture");
        Check(Strings.Resolve(Dir("{\"language\":\"xx\"}"), "de") == "en", "unknown code falls back to en");
        Check(
            Strings.Resolve(Path.Combine(Path.GetTempPath(), "strings-table-absent"), "") == "en",
            "no dir, no culture -> en"
        );

        string? old = Environment.GetEnvironmentVariable("WINLESTIA_DATA_DIR");
        Environment.SetEnvironmentVariable("WINLESTIA_DATA_DIR", @"C:\winlestia-test-dir");
        Check(Strings.DefaultDataDir() == @"C:\winlestia-test-dir", "WINLESTIA_DATA_DIR honoured");
        Environment.SetEnvironmentVariable("WINLESTIA_DATA_DIR", old);

        Console.WriteLine(failures == 0 ? "StringsTable: all passed" : "StringsTable: " + failures + " failed");
        return failures == 0 ? 0 : 1;
    }
}
