// Quel lien l'icône réseau du dock doit afficher : "ethernet" | "wifi" | "none". Piloté par événements, sans sondage :
// NetworkChange.NetworkAddressChanged / NetworkAvailabilityChanged ne font qu'armer un minuteur ponctuel de 300 ms (réarmer =
// anti-rebond) ; la classification s'exécute ensuite sur un thread du pool.
// Classification : l'interface que Windows utiliserait vraiment (GetBestInterface vers une IPv4 publique, qui respecte les métriques
// d'interface) quand c'est un lien physique, actif, non virtuel, avec une passerelle par défaut ;
// sinon (IPv6 seul, VPN meilleure route, ...) tout lien physique actif avec passerelle, Ethernet préféré au sans-fil (les métriques
// automatiques de Windows classent le filaire d'abord).
// Types filaires -> "ethernet" ; Wireless80211 et haut débit mobile (Wwanpp/Wwanpp2) -> "wifi" (pictogramme sans-fil générique, pas
// d'icône cellulaire dédiée) ; rien d'utilisable -> "none".
// La première valeur est calculée dans Start(), hors du thread de l'appelant. Stop() se désabonne et libère le minuteur (aucune
// fuite). L'état est lu par QtShellBridge.State() à chaque interrogation.
using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PanneauxGlass;

internal static class NetworkKindWatcher
{
    static readonly object gate = new();
    static Timer? debounce;
    static bool started;
    static volatile string current = "wifi";   // affiché jusqu'à la fin de la première classification (l'icône historique du dock)
    internal static string Current => current;
    // Levé (thread du pool, hors de tout verrou) seulement quand le type classé change ; les abonnés ne doivent pas lever d'exception
    // (avalée).
    internal static event Action? Changed;
    static readonly object computeGate = new();

    internal static void Start()
    {
        lock (gate)
        {
            if (started)
                return;
            started = true;
            debounce = new Timer(_ => Recompute(), null, Timeout.Infinite, Timeout.Infinite);
            NetworkChange.NetworkAddressChanged += OnChange;
            NetworkChange.NetworkAvailabilityChanged += OnAvailability;
        }
        Task.Run(Recompute);
    }

    internal static void Stop()
    {
        lock (gate)
        {
            if (!started)
                return;
            started = false;
            NetworkChange.NetworkAddressChanged -= OnChange;
            NetworkChange.NetworkAvailabilityChanged -= OnAvailability;
            debounce?.Dispose();
            debounce = null;
        }
    }

    static void OnChange(object? sender, EventArgs e) => Arm();
    static void OnAvailability(object? sender, NetworkAvailabilityEventArgs e) => Arm();
    static void Arm()
    {
        lock (gate)
        {
            if (started)
                debounce?.Change(300, Timeout.Infinite);
        }
    }

    static void Recompute()
    {
        lock (gate)
        {
            if (!started)
                return;
        }
        bool changed = false;
        lock (computeGate)
        {   // sérialisé : une ancienne classification ne peut jamais écraser une plus récente
            lock (gate)
            {
                if (!started)
                    return;
            }
            try
            {
                changed = SetKind(Classify());
            }
            catch { /* keep the previous value */ }
        }
        if (changed)
        {
            try
            {
                Changed?.Invoke();
            }
            catch (Exception)
            {
                // Un abonné défaillant ne doit pas arrêter la veille réseau.
            }
        }
    }

    // Stocke un nouveau type ; vrai quand il diffère. Sert aussi de point d'entrée de test pour la sonde (sans accès aux adaptateurs).
    internal static bool SetKind(string k)
    {
        if (k == current)
            return false;
        current = k;
        return true;
    }
    internal static void RaiseChangedForTest(string k)
    {
        if (SetKind(k))
        {
            try
            {
                Changed?.Invoke();
            }
            catch (Exception)
            {
                // Un abonné défaillant ne doit pas arrêter la veille réseau.
            }
        }
    }

    // Public pour le test de la sonde.
    internal static string Classify()
    {
        var candidates = NetworkInterface.GetAllNetworkInterfaces().Where(IsCandidate).ToList();
        if (candidates.Count == 0)
            return "none";
        int best = BestInterfaceIndex();
        if (best > 0)
        {
            var hit = candidates.FirstOrDefault(n => IndexOf(n) == best);
            if (hit != null)
                return Kind(hit);
        }
        if (candidates.Any(n => Kind(n) == "ethernet"))
            return "ethernet";
        return "wifi";
    }

    // Mêmes exclusions que PcTelemetry (copiées : virtuel / VPN / Hyper-V / bouclage / tunnel), plus : doit être actif, avoir une vraie
    // passerelle par défaut et être filaire ou sans-fil.
    static bool IsCandidate(NetworkInterface n)
    {
        if (n.OperationalStatus != OperationalStatus.Up)
            return false;
        if (n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
            return false;
        if (IsVirtual(n))
            return false;
        if (!IsWired(n) && !IsWireless(n))
            return false;
        try
        {
            return n.GetIPProperties().GatewayAddresses.Any(g => g.Address != null && !g.Address.Equals(IPAddress.Any)
                && !g.Address.Equals(IPAddress.IPv6Any));
        }
        catch { return false; }
    }

    static bool IsVirtual(NetworkInterface n) => new[] { "virtual", "hyper-v", "vmware", "vethernet", "tap-", "vpn", "wireguard",
        "loopback", "tunnel" }.Any(x => (n.Name + " " + n.Description).Contains(x, StringComparison.OrdinalIgnoreCase));
    static bool IsWired(NetworkInterface n)
        => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType
        .FastEthernetT or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit;
    static bool IsWireless(NetworkInterface n)
        => n.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2;
    static string Kind(NetworkInterface n) => IsWired(n) ? "ethernet" : "wifi";
    static int IndexOf(NetworkInterface n)
    {
        try
        {
            return n.GetIPProperties().GetIPv4Properties()?.Index ?? -1;
        }
        catch { return -1; }
    }

    static int BestInterfaceIndex()
    {
        try
        {
            uint dest = BitConverter.ToUInt32(IPAddress.Parse("8.8.8.8").GetAddressBytes(), 0);
            return GetBestInterface(dest, out uint index) == 0 ? (int)index : -1;
        }
        catch { return -1; }
    }

    [DllImport("iphlpapi.dll")] static extern int GetBestInterface(uint destAddr, out uint bestIfIndex);
}
