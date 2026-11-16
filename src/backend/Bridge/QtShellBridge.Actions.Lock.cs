// language: C# 12, file: QtShellBridge.Actions.Lock.cs, target: Windows 11 / .NET 8, actions du pont : écran de verrouillage
using System.Text.Json;
namespace PanneauxGlass;

internal sealed partial class QtShellBridge
{
    LockService? lockService;

    // Créé à la demande sur le thread du dispatcher ; l'évènement « lock » invite le client à relire l'état.
    LockService Lock => lockService ??= new LockService(dispatcher, () => { if (!disposed) Send("lock"); },
        () => pipe is { IsConnected: true }, () => FullscreenWatcher.Current.Length > 0);

    internal bool IsLocked => lockService?.IsLocked == true;
    internal void LockNow() => Lock.LockNow();
    internal void StartLock() => Lock.Rearm();

    // Échec ouvert : fermeture, perte du client ou du processus frontal lèvent le verrou et retirent le hook.
    internal void ReleaseLock() => lockService?.Dispose();

    [BridgeAction("lockNow")]
    void OnLockNow(BridgeCall c) => Lock.LockNow();

    internal void RescueLock() => lockService?.Release(true);

    [BridgeAction("lockShown")]
    void OnLockShown(BridgeCall c) => lockService?.Shown();

    [BridgeAction("lockUnlock")]
    void OnLockUnlock(BridgeCall c) => Lock.Unlock(Text(c.Args, "password"));

    [BridgeAction("lockSetPassword")]
    void OnLockSetPassword(BridgeCall c) => Lock.SetPassword(Text(c.Args, "current"), Text(c.Args, "password"));

    [BridgeAction("lockSettings")]
    void OnLockSettings(BridgeCall c)
    {
        bool? enabled = c.Args.TryGetProperty("enabled", out var e) && e.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? e.ValueKind == JsonValueKind.True
            : null;
        int? idle = c.Args.TryGetProperty("idleSeconds", out var s) && s.TryGetInt32(out int seconds) ? seconds : null;
        Lock.ApplySettings(enabled, idle);
    }
}
