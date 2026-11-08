// language: C# 12, file: QtShellBridge.Actions.cs, target: Windows 11 / .NET 8, table de dispatch des actions du pont
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    static readonly Lazy<IReadOnlyDictionary<string, Func<QtShellBridge, BridgeCall, Task>>> actionTable = new(BuildActionTable);

    // Exposé pour le test de contrat.
    internal static IReadOnlyCollection<string> ActionNames => actionTable.Value.Keys.ToArray();

    static IReadOnlyDictionary<string, Func<QtShellBridge, BridgeCall, Task>> BuildActionTable()
    {
        var table = new Dictionary<string, Func<QtShellBridge, BridgeCall, Task>>(StringComparer.Ordinal);
        foreach (var method in typeof(QtShellBridge).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            var names = method.GetCustomAttributes<BridgeActionAttribute>().Select(a => a.Name).ToArray();
            if (names.Length == 0)
                continue;
            Func<QtShellBridge, BridgeCall, Task> handler;
            if (method.ReturnType == typeof(Task))
                handler = method.CreateDelegate<Func<QtShellBridge, BridgeCall, Task>>();
            else
            {
                var sync = method.CreateDelegate<Action<QtShellBridge, BridgeCall>>();
                handler = (bridge, call) =>
                {
                    sync(bridge, call);
                    return Task.CompletedTask;
                };
            }
            foreach (string name in names)
                table.Add(name, handler);
        }
        return table;
    }

    // Une commande inconnue est ignorée : la réponse porte simplement l'état courant.
    Task DispatchAction(string command, BridgeCall call)
        => actionTable.Value.TryGetValue(command, out var handler) ? handler(this, call) : Task.CompletedTask;

    // La réponse contient déjà l'état : rien d'autre à faire.
    [BridgeAction("state")]
    void OnState(BridgeCall c)
    {
    }

    [BridgeAction("dictation")]
    void OnDictation(BridgeCall c)
    {
        var d = dictation.Snapshot();
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
            spectrum = spectrum.Values
        }, Json);
    }
}
