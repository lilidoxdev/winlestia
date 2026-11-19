// language: C# 12, file: QtShellBridge.Actions.Quick.cs, target: Windows 11 / .NET 8, actions du pont : disposition du dock et réglages
// rapides
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
namespace PanneauxGlass;

internal sealed partial class QtShellBridge
{
    static List<string>? StringList(JsonElement args, string name)
        => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString() ?? "").ToList()
            : null;

    [BridgeAction("dockLayout")]
    void OnDockLayout(BridgeCall c)
    {
        (Store.State.DockBar, Store.State.DockDrawer) = DockItems.Normalise(StringList(c.Args, "bar"), StringList(c.Args, "drawer"));
        Store.Save();
    }

    Action? quickPush;

    // Premier instantané : abonne le pont aux changements du cache (radios, micro) ; l'évènement « quick » invite le client à relire
    // l'état.
    object QuickSnapshot()
    {
        if (quickPush == null)
        {
            quickPush = () => _ = dispatcher.BeginInvoke((Action)(() => { if (!disposed) Send("quick"); }));
            QuickSettings.Shared.Changed += quickPush;
        }
        return QuickSettings.Shared.Snapshot();
    }

    static readonly string[] QuickIds = { "wifi", "bluetooth", "airplane", "micMute", "keepAwake", "nightLight", "focus" };

    [BridgeAction("quickToggle")]
    void OnQuickToggle(BridgeCall c)
    {
        string id = Text(c.Args, "id");
        if (!QuickIds.Contains(id))
            return;
        bool? enabled = c.Args.TryGetProperty("enabled", out var e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? e.ValueKind == JsonValueKind.True
            : null;
        // Le canal du pont est séquentiel : le travail (WinRT, COM) part en tâche de fond, l'évènement « quick » rafraîchit l'interface.
        _ = Task.Run(async () =>
        {
            try
            {
                await QuickSettings.Shared.Toggle(id, enabled);
            }
            catch (Exception ex) { Store.Log("quickToggle " + id + ": " + ex.Message); }
        });
    }
}
