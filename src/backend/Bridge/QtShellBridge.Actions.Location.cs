// language: C# 12, file: QtShellBridge.Actions.Location.cs, target: Windows 11 / .NET 8, actions du pont : position Windows à la demande
// (état `location`)
using System;
using System.Threading;

namespace PanneauxGlass;

internal sealed partial class QtShellBridge
{
    Lazy<LocationService>? locationService;

    // Le résultat réveille la boucle d'état (comme networkChanged) : `location` est publié au prochain échange.
    LocationService Locator => (locationService ?? InitLocation()).Value;

    Lazy<LocationService> InitLocation()
    {
        var created = new Lazy<LocationService>(() => new LocationService(() => networkChanged?.Invoke()));
        return Interlocked.CompareExchange(ref locationService, created, null) ?? created;
    }

    // Forme publiée : { lat, lon, accuracyM, ok, requested }. N'instancie rien tant que personne n'a demandé la position.
    object LocationSnapshot() => locationService?.IsValueCreated == true ? locationService.Value.Snapshot()
        : new
        {
            lat = 0d,
            lon = 0d,
            accuracyM = 0d,
            ok = false,
            requested = false
        };

    [BridgeAction("locate")]
    void OnLocate(BridgeCall c) => _ = Locator.Request();
}
