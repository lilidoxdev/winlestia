// language: C# 12, file: QtShellBridge.Actions.Sensors.cs, target: Windows 11 / .NET 8, actions du pont : assistant de capteurs élevé
using System;
using System.Threading;

namespace PanneauxGlass;

internal sealed partial class QtShellBridge
{
    Lazy<SensorService>? sensorService;

    // L'évènement « sensors » invite le client à relire l'état (settings.sensors). Lazy : initialisation sûre entre fils.
    SensorService Sensors => (sensorService ?? InitSensors()).Value;

    Lazy<SensorService> InitSensors()
    {
        var created = new Lazy<SensorService>(() => new SensorService(() => { if (!disposed) Send("sensors"); }));
        return Interlocked.CompareExchange(ref sensorService, created, null) ?? created;
    }

    internal object SensorsSnapshot() => Sensors.Snapshot();

    [BridgeAction("sensorsInstall")]
    void OnSensorsInstall(BridgeCall c) => Sensors.Install(Flag(c.Args, "driver"));

    [BridgeAction("sensorsRemove")]
    void OnSensorsRemove(BridgeCall c) => Sensors.Remove();
}
