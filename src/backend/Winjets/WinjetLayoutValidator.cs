// language: C# 12, file: WinjetLayoutValidator.cs, target: Windows 11 / .NET 8, validation de l'arbre de mise en page de widget.json
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
namespace PanneauxGlass.Winjets;

// Parcourt les mises en page et vérifie : types de blocs/conteneurs dans la liste fermée, énumérations, conditions, images sous
// assets/, couleurs = rôles de palette seulement, actions (openUrl vers un hôte déclaré, setState sur une clé déclarée).
internal static class WinjetLayoutValidator
{
    const int MaxNodes = 500, MaxTreeDepth = 12, MaxChildren = 64;

    static readonly string[] Align = { "start", "center", "end", "stretch" };
    static readonly string[] Text = { "start", "center", "end" };

    // « type.propriété » -> valeurs permises ; « *.propriété » s'applique à tous les types.
    static readonly Dictionary<string, string[]> Enums = new(StringComparer.Ordinal)
    {
        ["*.align"] = Align,
        ["text.style"] = new[] { "title", "label", "body", "caption", "display", "mono" },
        ["text.align"] = Text,
        ["text.tone"] = new[] { "primary", "secondary", "accent" },
        ["image.fit"] = new[] { "contain", "cover" },
        ["clock.variant"] = new[] { "analog", "digital" },
        ["calendar.variant"] = new[] { "month", "day" },
        ["calendar.firstDay"] = new[] { "monday", "sunday" },
        ["chart.variant"] = new[] { "line", "area", "bars", "candles" },
        ["visualizer.source"] = new[] { "spectrum" },
        ["progress.variant"] = new[] { "ring", "bar" },
        ["dots.pattern"] = new[] { "pulse", "ripple", "torus" },
        ["dots.shape"] = new[] { "circle", "square" }
    };

    static readonly string[] ContainerProps = { "type", "children", "gap", "padding", "align", "grow", "visibleWhen", "action" };

    // Propriétés permises et obligatoires par type (format v1 : toute autre propriété est une erreur).
    static readonly Dictionary<string, string[]> Props = new(StringComparer.Ordinal)
    {
        ["column"] = ContainerProps,
        ["row"] = ContainerProps,
        ["stack"] = ContainerProps,
        ["grid"] = ContainerProps.Append("columns").ToArray(),
        ["text"] = new[] { "type", "text", "style", "align", "maxLines", "tone", "action" },
        ["image"] = new[] { "type", "src", "fit", "radius", "action" },
        ["clock"] = new[] { "type", "variant", "showSeconds", "seconds", "format", "timezone" },
        ["calendar"] = new[] { "type", "variant", "firstDay" },
        ["list"] = new[] { "type", "items", "title", "subtitle", "trailing", "max" },
        ["chart"] = new[] { "type", "series", "variant", "axes", "period", "color", "hover", "valueFormat" },
        ["stat"] = new[] { "type", "value", "label", "delta", "action" },
        ["progress"] = new[] { "type", "value", "variant", "label" },
        ["dots"] = new[] { "type", "matrix", "pattern", "shape" },
        ["buttons"] = new[] { "type", "options", "bind" },
        ["visualizer"] = new[] { "type", "bars", "source" },
        ["media"] = new[] { "type", "showArt", "showProgress", "showControls" },
        ["spacer"] = new[] { "type" },
        ["divider"] = new[] { "type" }
    };

    static readonly Dictionary<string, string[]> Required = new(StringComparer.Ordinal)
    {
        ["text"] = new[] { "text" }, ["image"] = new[] { "src" }, ["clock"] = new[] { "variant" }, ["calendar"] = new[] { "variant" },
        ["list"] = new[] { "items", "title" }, ["chart"] = new[] { "series" }, ["stat"] = new[] { "value" },
        ["progress"] = new[] { "value" }, ["buttons"] = new[] { "options", "bind" },
        ["column"] = new[] { "children" }, ["row"] = new[] { "children" }, ["stack"] = new[] { "children" }, ["grid"] = new[] { "children" },
        ["dots"] = Array.Empty<string>(), ["visualizer"] = Array.Empty<string>(), ["media"] = Array.Empty<string>(), ["spacer"] = Array.Empty<string>(), ["divider"] = Array.Empty<string>()
    };

    static readonly string[] ColorProperties = { "color", "background", "fill", "stroke", "border", "tint" };
    static readonly Regex LiteralColor = new(@"^\s*(#[0-9a-fA-F]{3,8}|(rgb|hsl)a?\s*\()", RegexOptions.Compiled);
    static readonly string[] ActionTypes = { "setState", "refresh", "openUrl", "openSettings", "media.playPause", "media.next", "media.prev" };

    sealed class Walk
    {
        internal int Nodes;
        internal WinjetManifest Manifest = null!;
        internal string? Dir;
        internal HashSet<string> StateKeys = null!;
        internal WinjetIssues Issues = null!;
    }

    internal static void Validate(JsonElement widget, WinjetManifest manifest, string? dir, HashSet<string> stateKeys, WinjetIssues issues)
    {
        var ctx = new Walk { Manifest = manifest, Dir = dir, StateKeys = stateKeys, Issues = issues };
        if (issues.Arr(widget, "layouts", "$", true, 16) is not { } layouts)
            return;
        if (layouts.GetArrayLength() == 0)
        {
            issues.Add("$.layouts", "au moins une mise en page est nécessaire.");
            return;
        }
        int index = 0;
        foreach (var layout in layouts.EnumerateArray())
        {
            string at = "$.layouts[" + index++ + "]";
            if (layout.ValueKind != JsonValueKind.Object)
            {
                issues.Add(at, "doit être un objet.");
                continue;
            }
            issues.Only(layout, at, "minWidth", "minHeight", "root");
            Int(layout, "minWidth", at, 0, 5000, true, issues);
            Int(layout, "minHeight", at, 0, 5000, false, issues);
            if (issues.Obj(layout, "root", at, true) is { } root)
                Node(root, at + ".root", 1, ctx);
        }
        ScanLiteralColors(layouts, "$.layouts", issues, 0);
    }

    static void Node(JsonElement node, string at, int depth, Walk ctx)
    {
        var issues = ctx.Issues;
        if (++ctx.Nodes > MaxNodes)
        {
            if (ctx.Nodes == MaxNodes + 1)
                issues.Add(at, "trop de nœuds dans les mises en page (maximum " + MaxNodes + ").");
            return;
        }
        if (depth > MaxTreeDepth)
        {
            issues.Add(at, "mise en page trop profonde (maximum " + MaxTreeDepth + " niveaux).");
            return;
        }
        string? type = issues.Str(node, "type", at, true, 20);
        if (type == null)
            return;
        bool container = WinjetLimits.Containers.Contains(type);
        if (!container && !WinjetLimits.Blocks.Contains(type))
        {
            issues.Add(at + ".type", "type « " + type + " » inconnu.");
            return;
        }
        issues.Only(node, at, Props[type]);
        foreach (string need in Required[type])
            if (!node.TryGetProperty(need, out _))
                issues.Add(at + "." + need, "champ obligatoire.");
        if (type == "dots" && !node.TryGetProperty("matrix", out _) && !node.TryGetProperty("pattern", out _))
            issues.Add(at, "« matrix » ou « pattern » est obligatoire.");
        CommonProperties(node, at, ctx);
        foreach (var p in node.EnumerateObject())
        {
            // Une énumération peut être pilotée par un réglage : la valeur entière « {{ ... }} » est acceptée (le renderer retombe
            // sur la première valeur permise si le résultat n'en fait pas partie).
            if (p.Name != "type" && (Enums.TryGetValue(type + "." + p.Name, out var allowed) || Enums.TryGetValue("*." + p.Name, out allowed)))
                if (p.Value.ValueKind != JsonValueKind.String || (!allowed.Contains(p.Value.GetString()) && !IsExpression(p.Value.GetString())))
                    issues.Add(at + "." + p.Name, "valeur permise : " + string.Join(", ", allowed) + " (ou une expression {{ ... }}).");
            if (ColorProperties.Contains(p.Name) && (p.Value.ValueKind != JsonValueKind.String
                || !WinjetLimits.PaletteRoles.Contains(p.Value.GetString()!)))
                issues.Add(at + "." + p.Name, "couleur non permise : seuls les rôles de palette sont acceptés ("
                    + string.Join(", ", WinjetLimits.PaletteRoles) + ").");
        }
        if (container)
            Container(node, type, at, depth, ctx);
        else
            Block(node, type, at, ctx);
    }

    static bool IsExpression(string? s) => s != null && s.Length > 4 && s.StartsWith("{{", StringComparison.Ordinal)
        && s.EndsWith("}}", StringComparison.Ordinal) && s.IndexOf("{{", 2, StringComparison.Ordinal) < 0;

    static void Int(JsonElement obj, string name, string at, int min, int max, bool required, WinjetIssues issues)
    {
        if (issues.Num(obj, name, at, required) is { } n && (n != Math.Floor(n) || n < min || n > max))
            issues.Add(at + "." + name, "doit être un entier de " + min + " à " + max + ".");
    }

    static void CommonProperties(JsonElement node, string at, Walk ctx)
    {
        var issues = ctx.Issues;
        if (issues.Str(node, "visibleWhen", at, false, 400) is { } when && !WinjetCondition.TryParse(when, out _, out string why))
            issues.Add(at + ".visibleWhen", "expression invalide (" + why + ").");
        Int(node, "gap", at, 0, 64, false, issues);
        Int(node, "padding", at, 0, 64, false, issues);
        Int(node, "radius", at, 0, 200, false, issues);
        Int(node, "maxLines", at, 1, 20, false, issues);
        Int(node, "max", at, 1, 50, false, issues);
        Int(node, "bars", at, 8, 64, false, issues);
        if (issues.Num(node, "grow", at, false) is { } g && g < 0)
            issues.Add(at + ".grow", "doit être positif ou nul.");
        foreach (string key in new[] { "showSeconds", "seconds", "axes", "hover", "showArt", "showProgress", "showControls" })
            if (node.TryGetProperty(key, out var b) && b.ValueKind is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.String))
                issues.Add(at + "." + key, "doit être vrai/faux ou une expression.");
        if (node.TryGetProperty("action", out var action) && action.ValueKind != JsonValueKind.Null)
            Action(action, at + ".action", ctx);
    }

    static void Container(JsonElement node, string type, string at, int depth, Walk ctx)
    {
        var issues = ctx.Issues;
        if (type == "grid" && issues.Num(node, "columns", at, true) is { } cols && (cols < 1 || cols > 12 || cols != Math.Floor(cols)))
            issues.Add(at + ".columns", "doit être un entier de 1 à 12.");
        if (issues.Arr(node, "children", at, true, MaxChildren) is not { } children)
            return;
        int i = 0;
        foreach (var child in children.EnumerateArray())
        {
            string cat = at + ".children[" + i++ + "]";
            if (child.ValueKind != JsonValueKind.Object)
                issues.Add(cat, "doit être un objet.");
            else
                Node(child, cat, depth + 1, ctx);
        }
    }

    static void Block(JsonElement node, string type, string at, Walk ctx)
    {
        var issues = ctx.Issues;
        switch (type)
        {
            case "chart":
                issues.Str(node, "valueFormat", at, false, 20);
                break;
            case "image":
                if (issues.Str(node, "src", at, true, 200) is { } src)
                    WinjetDefinition.CheckAsset(src, ctx.Dir, at + ".src", issues);
                break;
            case "buttons":
                {
                    if (issues.Arr(node, "options", at, true, 16) is { } options)
                    {
                        int i = 0;
                        foreach (var o in options.EnumerateArray())
                        {
                            string oat = at + ".options[" + i++ + "]";
                            if (o.ValueKind != JsonValueKind.Object)
                                issues.Add(oat, "doit être { value, label }.");
                            else
                            {
                                issues.Str(o, "value", oat, true, 60);
                                issues.Str(o, "label", oat, true, 60);
                                issues.Only(o, oat, "value", "label");
                            }
                        }
                    }
                    string? bind = issues.Str(node, "bind", at, true, 40);
                    if (bind != null && !(bind.StartsWith("state.", StringComparison.Ordinal) && ctx.StateKeys.Contains(bind[6..])))
                        issues.Add(at + ".bind", "doit être « state.<clé> » avec une clé déclarée dans « state ».");
                    break;
                }
        }
    }

    static void Action(JsonElement action, string at, Walk ctx)
    {
        var issues = ctx.Issues;
        if (action.ValueKind != JsonValueKind.Object)
        {
            issues.Add(at, "doit être un objet.");
            return;
        }
        string? type = issues.Str(action, "type", at, true, 20);
        if (type == null)
            return;
        issues.Only(action, at, type switch
        {
            "setState" => new[] { "type", "key", "value" },
            "openUrl" => new[] { "type", "url" },
            _ => new[] { "type" }
        });
        switch (type)
        {
            case "setState":
                {
                    string? key = issues.Str(action, "key", at, true, 40);
                    if (key != null && !ctx.StateKeys.Contains(key))
                        issues.Add(at + ".key", "clé d'état « " + key + " » non déclarée dans « state ».");
                    if (!action.TryGetProperty("value", out var v) || v.ValueKind is not (JsonValueKind.String or JsonValueKind.Number
                        or JsonValueKind.True or JsonValueKind.False))
                        issues.Add(at + ".value", "doit être un texte, un nombre ou vrai/faux.");
                    break;
                }
            case "openUrl":
                {
                    string? url = issues.Str(action, "url", at, true, 500);
                    if (url == null)
                        break;
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0)
                        issues.Add(at + ".url", "doit être une adresse https:// valide.");
                    else if (!ctx.Manifest.AllowsHost(uri.IdnHost))
                        issues.Add(at + ".url", "l'hôte « " + uri.IdnHost + " » n'est déclaré ni dans permissions.network ni comme homepage.");
                    break;
                }
            case "refresh":
            case "openSettings":
            case "media.playPause":
            case "media.next":
            case "media.prev":
                break;
            default:
                issues.Add(at + ".type", "action « " + type + " » inconnue (" + string.Join(", ", ActionTypes) + ").");
                break;
        }
    }

    // Aucune couleur littérale (#fff, rgb(), hsl()) dans aucune chaîne du fichier.
    static void ScanLiteralColors(JsonElement e, string at, WinjetIssues issues, int depth)
    {
        if (depth > WinjetLimits.MaxJsonDepth)
            return;
        switch (e.ValueKind)
        {
            case JsonValueKind.String:
                if (LiteralColor.IsMatch(e.GetString() ?? ""))
                    issues.Add(at, "couleur littérale interdite : utiliser un rôle de palette.");
                break;
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                    ScanLiteralColors(p.Value, at + "." + p.Name, issues, depth + 1);
                break;
            case JsonValueKind.Array:
                int i = 0;
                foreach (var item in e.EnumerateArray())
                    ScanLiteralColors(item, at + "[" + i++ + "]", issues, depth + 1);
                break;
        }
    }
}
