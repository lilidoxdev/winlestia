using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace WinlestiaSetup;

sealed class Options
{
    public bool Uninstall, RemoveData, FromShell, DryRun, NoMotion, Relocated;
    public string? Lang, Screenshots, Wallpaper;
    public bool DataChosen;

    public static Options Parse(string[] a)
    {
        var o = new Options();
        for (int i = 0; i < a.Length; i++)
        {
            switch (a[i].ToLowerInvariant())
            {
                case "--uninstall": o.Uninstall = true; break;
                case "--keep-data": o.RemoveData = false; o.DataChosen = true; break;
                case "--remove-data": o.RemoveData = true; o.DataChosen = true; break;
                case "--from-shell": o.FromShell = true; break;
                case "--dry-run": o.DryRun = true; break;
                case "--no-motion": o.NoMotion = true; break;
                case "--relocated": o.Relocated = true; break;
                case "--lang": if (i + 1 < a.Length) o.Lang = a[++i]; break;
                case "--screenshots": if (i + 1 < a.Length) o.Screenshots = a[++i]; break;
                case "--wallpaper": if (i + 1 < a.Length) o.Wallpaper = a[++i]; break;
            }
        }
        return o;
    }
}

static class Log
{
    static readonly object Gate = new();
    public static readonly List<string> Lines = new();
    public static string? File;
    public static void Write(string s)
    {
        var line = DateTime.Now.ToString("HH:mm:ss.fff") + "  " + s;
        lock (Gate)
        {
            Lines.Add(line);
            if (File != null) { try { System.IO.File.AppendAllText(File, line + Environment.NewLine); } catch { } }
        }
    }
}

enum Mode { Install, Update, Modify, Uninstall }

static class Ctx
{
    public static Options O = new();
    public static InstallInfo Info = new();
    public static bool Demo;               // sans payload : tout le flux s'exécute, rien n'est écrit
    public static string Version = "0.0.0";
    public static Mode Mode = Mode.Install;
    public static Dictionary<string, bool> Features = new();
    public static bool NotifyUpdates;
    public static bool LaunchAfter = true;
    public static bool Screenshot;

    public static bool Dry => O.DryRun || Screenshot || (Demo && (Mode == Mode.Install || Mode == Mode.Update));
    public static string LocalApp => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static string DefaultDir => System.IO.Path.Combine(LocalApp, "Programs", "Winlestia");
    public static string BackupRoot => System.IO.Path.Combine(LocalApp, "Programs", "Winlestia-backups");
    public static string PrefsPath => System.IO.Path.Combine(LocalApp, "Panneaux", "preferences-glass.json");
    public static string InstallDir => Info.Exists && Info.Dir.Length > 0 ? Info.Dir : DefaultDir;

    public static void LoadVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0";
        Version = v.Split('+')[0];
    }
}

enum FeatureKind { Pref, Action, Locked }

sealed record Feature(string Id, FeatureKind Kind, string[] Keys, bool Default, string Glyph, string? Preview);

static class Features
{
    // Les clés sont les vraies propriétés de PanneauxGlass Preferences (preferences-glass.json). Les panneaux et les Winjets n'ont pas de
    // clé on/off.
    public static readonly Feature[] All =
    {
        new("taskbar", FeatureKind.Pref, new[] { "ReplaceTaskbar" }, true, "", "taskbar"),
        new("dock", FeatureKind.Pref, new[] { "DockVisible" }, true, "", "dock"),
        new("panels", FeatureKind.Locked, Array.Empty<string>(), true, "", "dashboard"),
        new("winjets", FeatureKind.Locked, Array.Empty<string>(), true, "", null),
        new("terminal", FeatureKind.Action, Array.Empty<string>(), false, "", "terminal"),
        new("pill", FeatureKind.Pref, new[] { "Pill" }, true, "", "media"),
        new("rails", FeatureKind.Pref, new[] { "SocialRailVisible", "BookmarksRailVisible" }, true, "", null),
    };

    public static void Reset()
    {
        Ctx.Features = All.ToDictionary(f => f.Id, f => f.Default);
        if (!Ctx.Info.Exists || Ctx.Screenshot) return;
        try
        {
            if (File.Exists(Ctx.PrefsPath))
            {
                var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Ctx.PrefsPath), documentOptions: new JsonDocumentOptions {
                    AllowTrailingCommas = true })?.AsObject();
                foreach (var f in All.Where(f => f.Kind == FeatureKind.Pref))
                    if (root != null && root.TryGetPropertyValue(f.Keys[0], out var n) && n != null) Ctx.Features[f.Id] =
                        n.GetValue<bool>();
            }
            Ctx.Features["terminal"] = File.Exists(Path.Combine(Ctx.LocalApp, @"Winlestia\terminal-backup\state.json"));
        }
        catch (Exception e) { Log.Write("Could not read current settings: " + e.Message); }
    }
}

static class Loc
{
    static Dictionary<string, string> en = new(), cur = new();
    public static string Lang = "en";
    public static readonly string[] Supported = { "en", "fr", "es", "de" };

    static Dictionary<string, string> Load(string lang)
    {
        string? json = null;
        var ext = Path.Combine(AppContext.BaseDirectory, "Strings", lang + ".json");
        if (File.Exists(ext)) json = File.ReadAllText(ext);
        else
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Strings/" + lang + ".json");
            if (s != null) json = new StreamReader(s).ReadToEnd();
        }
        if (json == null) return new();
        return JsonSerializer.Deserialize<Dictionary<string,
            string>>(json, new JsonSerializerOptions { AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip })
            ?? new();
    }

    public static void Init(string? forced)
    {
        en = Load("en");
        Set((forced ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName).ToLowerInvariant());
    }

    public static void Set(string lang)
    {
        Lang = lang;
        cur = lang == "en" ? en : Load(lang);
    }

    public static string T(string key) => cur.TryGetValue(key, out var v) ? v : en.TryGetValue(key, out var e) ? e : key;
    public static string F(string key, params object[] a) => string.Format(T(key), a);
    public static IEnumerable<string> AllValues(string key) =>
        Supported.Select(l => Load(l)).Select(d => d.TryGetValue(key, out var v) ? v : null).Where(v => v != null).Cast<string>()
            .Distinct();
}
