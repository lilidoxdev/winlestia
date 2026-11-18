// language: C# 12, file: LocationService.cs, target: Windows 11 / .NET 8 WPF, position Windows (Geolocator) à la demande, mise en cache 30
// min
using System;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;

namespace PanneauxGlass;

internal sealed record LocationFix(double Lat, double Lon, double AccuracyM, bool Ok);

// Aucune scrutation : une position n'est demandée que par l'action « locate ». Succès gardé 30 min ; échec (accès refusé, service
// désactivé, délai) réessayable après 1 min seulement.
internal sealed class LocationService
{
    internal static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(30), RetryAfterFailure = TimeSpan.FromMinutes(1),
        Timeout = TimeSpan.FromSeconds(5);

    readonly object gate = new();
    readonly Func<Task<LocationFix>> provider;
    readonly Func<DateTimeOffset> clock;
    readonly Action changed;
    LocationFix last = new(0, 0, 0, false);
    DateTimeOffset lastAt = DateTimeOffset.MinValue;
    bool busy, requested;

    internal LocationService(Action changed, Func<Task<LocationFix>>? provider = null, Func<DateTimeOffset>? clock = null)
    {
        this.changed = changed;
        this.provider = provider ?? Locate;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    // Forme publiée dans l'état (`location`) : { lat, lon, accuracyM, ok, requested } (`requested` : une lecture a abouti ou échoué).
    internal object Snapshot()
    {
        lock (gate)
            return new
            {
                lat = last.Lat,
                lon = last.Lon,
                accuracyM = last.AccuracyM,
                ok = last.Ok,
                requested
            };
    }

    // Lance une lecture si le cache est périmé et qu'aucune n'est en cours. Le résultat arrive par `changed`.
    internal Task Request()
    {
        lock (gate)
        {
            var age = clock() - lastAt;
            if (busy || age < (last.Ok ? CacheFor : RetryAfterFailure))
                return Task.CompletedTask;
            busy = true;
        }
        return Task.Run(async () =>
        {
            LocationFix fix;
            try
            {
                fix = await provider();
            }
            catch (Exception ex)
            {
                Store.Log("Position : " + ex.GetType().Name + ": " + ex.Message);
                fix = new LocationFix(0, 0, 0, false);
            }
            lock (gate)
            {
                last = fix;
                requested = true;
                lastAt = clock();
                busy = false;
            }
            try
            {
                changed();
            }
            catch (Exception ex) { Store.Log("Position (notification) : " + ex.GetType().Name + ": " + ex.Message); }
        });
    }

    static async Task<LocationFix> Locate()
    {
        var none = new LocationFix(0, 0, 0, false);
        var access = await Geolocator.RequestAccessAsync().AsTask().WaitAsync(Timeout);
        if (access != GeolocationAccessStatus.Allowed)
            return none;
        var locator = new Geolocator { DesiredAccuracy = PositionAccuracy.Default };
        if (locator.LocationStatus == PositionStatus.Disabled)
            return none;
        var position = await locator.GetGeopositionAsync(CacheFor, Timeout).AsTask().WaitAsync(Timeout + TimeSpan.FromSeconds(1));
        var p = position.Coordinate.Point.Position;
        // Précision ramenée à ~100 m : suffisant pour la météo, inutile de garder davantage.
        return new LocationFix(Math.Round(p.Latitude, 3), Math.Round(p.Longitude, 3), Math.Round(position.Coordinate.Accuracy), true);
    }
}
