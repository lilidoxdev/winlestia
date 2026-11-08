// language: C# 12, file: QtShellBridge.Testing.cs, target: Windows 11 / .NET 8, point d'entrée du dispatch pour les tests
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
namespace PanneauxGlass;

internal sealed partial class QtShellBridge
{
    // Même chemin que Handle (table de dispatch réelle, sortie dans `call`) sans l'assemblage d'état, qui lit l'audio, le réseau
    // et la télémétrie. Renvoie la réponse brute éventuelle (null = réponse d'état à assembler).
    internal Task<string?> RunAction(string command, BridgeCall call) => actionTable.Run(this, command, call, Store.Log);

    // Évènements en attente, filtrés comme dans Handle, puis vidés.
    internal string[] DrainEvents()
    {
        var queued = events.Where(e => KeepEvent(e.name, e.epoch, frontendEpoch)).Select(e => e.name).ToArray();
        events.Clear();
        return queued;
    }
}
