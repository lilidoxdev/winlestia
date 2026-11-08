// language: C# 12, file: QtShellBridge.Actions.cs, target: Windows 11 / .NET 8, table de dispatch des actions du pont
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
namespace PanneauxGlass;

// Une méthode marquée [BridgeAction("nom")] traite l'action « nom » de docs/bridge-schema.json.
// Un test de contrat compare ces noms au schéma : une action ne peut plus exister d'un seul côté.
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class BridgeActionAttribute : Attribute
{
    internal BridgeActionAttribute(string name) => Name = name;

    internal string Name { get; }
}

// Entrées et sorties d'une requête. Les sorties (Notice, …) deviennent les clés de la réponse JSON.
internal sealed class BridgeCall
{
    internal BridgeCall(JsonElement args, string id)
    {
        Args = args;
        Id = id;
    }

    internal JsonElement Args { get; }
    internal string Id { get; }
    internal string? Notice { get; set; }
    internal object? WallpaperCapture { get; set; }
    internal object? BookmarksItems { get; set; }
    internal object? WindowItems { get; set; }
    internal object? WallpaperPicker { get; set; }
    internal object? FileItems { get; set; }

    // Réponse complète qui court-circuite l'état (sondes rapides du frontend : dictée, barres audio, spectre).
    internal string? RawReply { get; set; }
}

internal sealed partial class QtShellBridge
{
    static readonly BridgeActionTable<QtShellBridge> actionTable;

    // Table construite dès le premier accès au type (donc avant toute instance) : un doublon ou une signature invalide
    // fait échouer le démarrage au lieu de se révéler à la première action.
    static QtShellBridge() => actionTable = BridgeActionTable<QtShellBridge>.Build(typeof(QtShellBridge));

    // Exposé pour le test de contrat.
    internal static IReadOnlyCollection<string> ActionNames => actionTable.Names;

    // La réponse contient déjà l'état : rien d'autre à faire.
    [BridgeAction("state")]
    void OnState(BridgeCall c)
    {
    }

    [BridgeAction("dictation")]
    void OnDictation(BridgeCall c)
    {
        var d = system.DictationState();
        c.RawReply = JsonSerializer.Serialize(new
        {
            dictation = new
            {
                state = d.State,
                level = d.Level,
                levels = d.Levels,
                lang = d.Lang,
                text = d.Text,
                error = d.Error
            }
        }, Json);
    }

    [BridgeAction("audioBars")]
    void OnAudioBars(BridgeCall c)
    {
        var head = bars.Snapshot(false);
        var v = head.Seq == (int)Number(c.Args, "seq") ? head : bars.Snapshot(true);
        c.RawReply = JsonSerializer.Serialize(new
        {
            audioBars = new
            {
                seq = v.Seq,
                idle = v.Idle,
                bars = v.Bars.Length > 0 ? v.Bars : null
            }
        }, Json);
    }

    [BridgeAction("visualizer")]
    void OnVisualizer(BridgeCall c)
    {
        c.RawReply = JsonSerializer.Serialize(new
        {
            spectrum = system.SpectrumValues()
        }, Json);
    }
}
