// C#12 / Contrat du pont Qt : tout message que le backend traite ou émet doit figurer dans docs/bridge-schema.json.
// Extraction statique des sources (pas de pipe, pas de bureau).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using PanneauxGlass;
namespace Winlestia.Tests;
public class BridgeContractTests
{
    static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Winlestia.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Racine du dépôt introuvable.");
    }

    static JsonElement Schema() => JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "docs",
        "bridge-schema.json"))).RootElement.Clone();

    // Tous les fichiers partiels du pont (QtShellBridge.cs, QtShellBridge.Pipe.cs, …).
    static string Bridge()
        => string.Concat(Directory.EnumerateFiles(Path.Combine(Root(), "src", "backend", "Bridge"), "QtShellBridge*.cs").OrderBy(f
            => f).Select(File.ReadAllText));

    static HashSet<string> Names(JsonElement e) => e.EnumerateObject().Select(p => p.Name).ToHashSet();

    [Fact]
    public void ProtocolVersionMatchesSchema()
    {
        Assert.Equal(QtShellBridge.ProtocolVersion, Schema().GetProperty("protocol").GetInt32());
    }

    [Fact]
    public void EveryHandlerIsInSchema()
    {
        var known = Names(Schema().GetProperty("commands"));
        var handlers = QtShellBridge.ActionNames;
        var missing = handlers.Where(n => !known.Contains(n)).ToList();
        Assert.True(missing.Count == 0, "Handlers absents de docs/bridge-schema.json : " + string.Join(", ", missing));
        Assert.True(handlers.Count > 50, "extractionPlausible");
    }

    [Fact]
    public void EverySchemaCommandHasAHandler()
    {
        var handlers = QtShellBridge.ActionNames.ToHashSet();
        var missing = Names(Schema().GetProperty("commands")).Where(n => !handlers.Contains(n)).ToList();
        Assert.True(missing.Count == 0, "Commandes du schéma sans handler : " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryPreferenceKeyIsInSchema()
    {
        var known = Names(Schema().GetProperty("preferenceKeys"));
        string text = File.ReadAllText(Path.Combine(Root(), "src", "backend", "Bridge", "QtShellBridge.Actions.Settings.cs"));
        int a = text.IndexOf("[BridgeAction(\"preference\")]", StringComparison.Ordinal);
        int b = text.IndexOf("[BridgeAction(", a + 1, StringComparison.Ordinal);
        Assert.True(a > 0, "handlerFound");
        string body = b > a ? text.Substring(a, b - a) : text.Substring(a);
        // `case` du switch sur la clé : un seul niveau d'indentation sous le switch du handler (12 espaces).
        var keys = Regex.Matches(body, "^ {12}case \"([^\"]+)\":", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
        var missing = keys.Where(k => !known.Contains(k)).ToList();
        Assert.True(missing.Count == 0, "Clés de préférence absentes du schéma : " + string.Join(", ", missing));
        Assert.True(keys.Count > 20, "extractionPlausible");
    }

    [Fact]
    public void EveryEmittedEventIsInSchema()
    {
        var schema = Schema();
        var events = schema.GetProperty("events").EnumerateArray().Select(e => e.GetString()!).ToHashSet();
        var prefixes = schema.GetProperty("eventPrefixes").EnumerateArray().Select(e => e.GetString()!).ToList();
        var literals = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root(), "src", "backend"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "VsCodeTheme", StringComparison.Ordinal))
                continue;
            string text = File.ReadAllText(file);
            literals.AddRange(Regex.Matches(text, @"(?:QtShell\??\.Send|\bSend|\bsend|extra\?\.Invoke)\(""([^""]+)""").Select(m
                => m.Groups[1].Value));
        }
        Assert.True(literals.Count > 30, "extractionPlausible");
        var missing = literals.Where(l => !events.Contains(l) && !prefixes.Any(p => l.StartsWith(p, StringComparison.Ordinal))).Distinct(
            ).ToList();
        Assert.True(missing.Count == 0, "Évènements absents du schéma : " + string.Join(", ", missing));
    }

    [Fact]
    public void ReplyKeysAreInSchema()
    {
        var known = Names(Schema().GetProperty("replyKeys"));
        string s = Bridge();
        foreach (var key in new[] { "state", "events", "notice", "wallpaperCapture", "bookmarksItems", "windows", "fileResults",
            "wallpaperPicker", "dictation", "audioBars", "spectrum" })
        {
            Assert.True(known.Contains(key), "Clé de réponse absente du schéma : " + key);
            Assert.True(Regex.IsMatch(s, @"\b" + key + " ?="), "Clé de réponse non émise : " + key);
        }
    }
}
