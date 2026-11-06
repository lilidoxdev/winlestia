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

    static JsonElement Schema() => JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "docs", "bridge-schema.json"))).RootElement.Clone();

    static string Bridge() => File.ReadAllText(Path.Combine(Root(), "src", "backend", "Bridge", "QtShellBridge.cs"));

    // Corps du `switch (command)` de Handle.
    static string Switch()
    {
        string s = Bridge();
        int a = s.IndexOf("switch (command)", StringComparison.Ordinal);
        int b = Regex.Match(s.Substring(a), @"^ {8}catch \(Exception ex\)", RegexOptions.Multiline).Index + a;
        Assert.True(a > 0 && b > a, "switchFound");
        return s.Substring(a, b - a);
    }

    static HashSet<string> Names(JsonElement e) => e.EnumerateObject().Select(p => p.Name).ToHashSet();

    [Fact]
    public void ProtocolVersionMatchesSchema()
    {
        Assert.Equal(QtShellBridge.ProtocolVersion, Schema().GetProperty("protocol").GetInt32());
    }

    [Fact]
    public void EveryHandledCommandIsInSchema()
    {
        var known = Names(Schema().GetProperty("commands"));
        string body = Switch();
        // `case "x":` au premier niveau du switch (16 espaces) ; les `case` imbriqués (preference) sont plus profonds.
        var cases = Regex.Matches(body, "^ {16}case \"([^\"]+)\":", RegexOptions.Multiline).Select(m => m.Groups[1].Value);
        var early = Regex.Matches(Bridge(), "command == \"([^\"]+)\"").Select(m => m.Groups[1].Value);
        var missing = cases.Concat(early).Where(n => !known.Contains(n)).Distinct().ToList();
        Assert.True(missing.Count == 0, "Commandes absentes de docs/bridge-schema.json : " + string.Join(", ", missing));
        Assert.True(cases.Count() > 50, "extractionPlausible");
    }

    [Fact]
    public void EverySchemaCommandIsHandledUnlessIgnored()
    {
        string all = Switch() + Bridge();
        foreach (var p in Schema().GetProperty("commands").EnumerateObject())
        {
            if (p.Value.TryGetProperty("ignoredByServer", out var ignored) && ignored.GetBoolean())
                continue;
            Assert.True(all.Contains("\"" + p.Name + "\"", StringComparison.Ordinal), "Commande du schéma non traitée : " + p.Name);
        }
    }

    [Fact]
    public void EveryPreferenceKeyIsInSchema()
    {
        var known = Names(Schema().GetProperty("preferenceKeys"));
        string body = Switch();
        int a = body.IndexOf("case \"preference\":", StringComparison.Ordinal);
        int b = body.IndexOf("case \"shortcut-reset\":", a, StringComparison.Ordinal);
        var keys = Regex.Matches(body.Substring(a, b - a), "^ {24}case \"([^\"]+)\":", RegexOptions.Multiline).Select(m => m.Groups[1].Value);
        var missing = keys.Where(k => !known.Contains(k)).ToList();
        Assert.True(missing.Count == 0, "Clés de préférence absentes du schéma : " + string.Join(", ", missing));
        Assert.True(keys.Count() > 20, "extractionPlausible");
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
            literals.AddRange(Regex.Matches(text, @"(?:QtShell\??\.Send|\bSend|\bsend|extra\?\.Invoke)\(""([^""]+)""").Select(m => m.Groups[1].Value));
        }
        Assert.True(literals.Count > 30, "extractionPlausible");
        var missing = literals.Where(l => !events.Contains(l) && !prefixes.Any(p => l.StartsWith(p, StringComparison.Ordinal))).Distinct().ToList();
        Assert.True(missing.Count == 0, "Évènements absents du schéma : " + string.Join(", ", missing));
    }

    [Fact]
    public void ReplyKeysAreInSchema()
    {
        var known = Names(Schema().GetProperty("replyKeys"));
        string s = Bridge();
        foreach (var key in new[] { "state", "events", "notice", "wallpaperCapture", "bookmarksItems", "windows", "fileResults", "wallpaperPicker", "dictation", "audioBars", "spectrum" })
        {
            Assert.True(known.Contains(key), "Clé de réponse absente du schéma : " + key);
            Assert.True(Regex.IsMatch(s, @"\b" + key + " ?="), "Clé de réponse non émise : " + key);
        }
    }
}
