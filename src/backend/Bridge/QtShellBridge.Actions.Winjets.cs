// language: C# 12, file: QtShellBridge.Actions.Winjets.cs, target: Windows 11 / .NET 8, actions du pont : Winjets (docs/WINJETS-DESIGN.md §4)
using System;
using System.Text.Json;
namespace PanneauxGlass;

internal sealed partial class QtShellBridge
{
    // Exécute l'action sur le service ; un message d'erreur devient le notice, l'état publié suit au prochain échange.
    void Winjet(BridgeCall c, Func<global::PanneauxGlass.Winjets.WinjetsService, string?> run)
    {
        if (winjets == null)
        {
            c.Notice = "Les Winjets ne sont pas disponibles.";
            return;
        }
        if (run(winjets) is { } error)
            c.Notice = error;
    }

    [BridgeAction("winjets.inspect")]
    void OnWinjetsInspect(BridgeCall c) => Winjet(c, w => w.Inspect(Text(c.Args, "path")));

    [BridgeAction("winjets.import")]
    void OnWinjetsImport(BridgeCall c) => Winjet(c, w => w.Import(Text(c.Args, "path")));

    [BridgeAction("winjets.uninstall")]
    void OnWinjetsUninstall(BridgeCall c) => Winjet(c, w => w.Uninstall(Text(c.Args, "packageId")));

    [BridgeAction("winjets.add")]
    void OnWinjetsAdd(BridgeCall c) => Winjet(c, w => w.Add(Text(c.Args, "packageId"), Text(c.Args, "screen"),
        c.Args.ValueKind == JsonValueKind.Object && c.Args.TryGetProperty("x", out _) ? Number(c.Args, "x") : null,
        c.Args.ValueKind == JsonValueKind.Object && c.Args.TryGetProperty("y", out _) ? Number(c.Args, "y") : null));

    [BridgeAction("winjets.remove")]
    void OnWinjetsRemove(BridgeCall c) => Winjet(c, w => w.Remove(Text(c.Args, "instanceId")));

    [BridgeAction("winjets.setHidden")]
    void OnWinjetsSetHidden(BridgeCall c) => Winjet(c, w => w.SetHidden(Text(c.Args, "instanceId"), Flag(c.Args, "hidden")));

    [BridgeAction("winjets.setOpacity")]
    void OnWinjetsSetOpacity(BridgeCall c) => Winjet(c, w => w.SetOpacity(Text(c.Args, "instanceId"), Member(c.Args, "opacity")));

    [BridgeAction("winjets.setGeometry")]
    void OnWinjetsSetGeometry(BridgeCall c) => Winjet(c, w => w.SetGeometry(Text(c.Args, "instanceId"), Member(c.Args, "geometry")));

    [BridgeAction("winjets.setSettings")]
    void OnWinjetsSetSettings(BridgeCall c) => Winjet(c, w => w.SetSettings(Text(c.Args, "instanceId"), Member(c.Args, "settings")));

    [BridgeAction("winjets.setState")]
    void OnWinjetsSetState(BridgeCall c) => Winjet(c, w => w.SetState(Text(c.Args, "instanceId"), Text(c.Args, "key"), Member(c.Args,
        "value")));

    [BridgeAction("winjets.setEditMode")]
    void OnWinjetsSetEditMode(BridgeCall c) => Winjet(c, w => w.SetEditMode(Flag(c.Args, "enabled")));

    [BridgeAction("winjets.refresh")]
    void OnWinjetsRefresh(BridgeCall c) => Winjet(c, w => w.Refresh(Text(c.Args, "instanceId")));

    [BridgeAction("winjets.recoverAll")]
    void OnWinjetsRecoverAll(BridgeCall c) => Winjet(c, w => w.RecoverAll());

    // Propriété JSON d'un argument (valeur nulle si absente : les validateurs la refusent).
    static JsonElement Member(JsonElement args, string name)
        => args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) ? v : default;
}
