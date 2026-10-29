// Which link the dock's network icon should show: "ethernet" | "wifi" | "none". Event-driven, no polling:
// NetworkChange.NetworkAddressChanged / NetworkAvailabilityChanged only arm a one-shot 300 ms timer (re-arming = debounce); the classification then runs on a thread-pool thread.
// Classification: the interface Windows would really use (GetBestInterface towards a public IPv4, which honours interface metrics) when it is a physical, Up, non-virtual link with a default gateway;
// otherwise (IPv6-only, VPN is the best route, ...) any physical Up link with a gateway, Ethernet preferred over wireless (Windows' automatic metrics rank wired first).
// Wired types -> "ethernet"; Wireless80211 and mobile broadband (Wwanpp/Wwanpp2) -> "wifi" (generic wireless glyph, no dedicated cellular icon); nothing usable -> "none".
// The first value is computed in Start() off the caller's thread. Stop() unsubscribes and disposes the timer (no leaks). State is read by QtShellBridge.State() on each poll.
using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PanneauxGlass;

internal static class NetworkKindWatcher {
    static readonly object gate = new();
    static Timer? debounce;
    static bool started;
    static volatile string current = "wifi";   // shown until the first classification finishes (the dock's historical icon)
    internal static string Current => current;
    // Raised (thread-pool thread, outside any lock) only when the classified kind changes; subscribers must not throw (exceptions are swallowed).
    internal static event Action? Changed;
    static readonly object computeGate = new();

    internal static void Start() {
        lock (gate) {
            if (started) return;
            started = true;
            debounce = new Timer(_ => Recompute(), null, Timeout.Infinite, Timeout.Infinite);
            NetworkChange.NetworkAddressChanged += OnChange;
            NetworkChange.NetworkAvailabilityChanged += OnAvailability;
        }
        Task.Run(Recompute);
    }

    internal static void Stop() {
        lock (gate) {
            if (!started) return;
            started = false;
            NetworkChange.NetworkAddressChanged -= OnChange;
            NetworkChange.NetworkAvailabilityChanged -= OnAvailability;
            debounce?.Dispose(); debounce = null;
        }
    }

    static void OnChange(object? sender, EventArgs e) => Arm();
    static void OnAvailability(object? sender, NetworkAvailabilityEventArgs e) => Arm();
    static void Arm() { lock (gate) { if (started) debounce?.Change(300, Timeout.Infinite); } }

    static void Recompute() {
        lock (gate) { if (!started) return; }
        bool changed = false;
        lock (computeGate) {   // serialized: an older classification can never overwrite a newer one
            lock (gate) { if (!started) return; }
            try { changed = SetKind(Classify()); } catch { /* keep the previous value */ }
        }
        if (changed) { try { Changed?.Invoke(); } catch { } }
    }

    // Stores a new kind; true when it differs. Also the test hook for the probe (no adapter access).
    internal static bool SetKind(string k) { if (k == current) return false; current = k; return true; }
    internal static void RaiseChangedForTest(string k) { if (SetKind(k)) { try { Changed?.Invoke(); } catch { } } }

    // Public for the probe test.
    internal static string Classify() {
        var candidates = NetworkInterface.GetAllNetworkInterfaces().Where(IsCandidate).ToList();
        if (candidates.Count == 0) return "none";
        int best = BestInterfaceIndex();
        if (best > 0) {
            var hit = candidates.FirstOrDefault(n => IndexOf(n) == best);
            if (hit != null) return Kind(hit);
        }
        if (candidates.Any(n => Kind(n) == "ethernet")) return "ethernet";
        return "wifi";
    }

    // Same exclusions as PcTelemetry (copied: virtual / VPN / Hyper-V / loopback / tunnel), plus: must be Up, have a real default gateway and be wired or wireless.
    static bool IsCandidate(NetworkInterface n) {
        if (n.OperationalStatus != OperationalStatus.Up) return false;
        if (n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) return false;
        if (IsVirtual(n)) return false;
        if (!IsWired(n) && !IsWireless(n)) return false;
        try { return n.GetIPProperties().GatewayAddresses.Any(g => g.Address != null && !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any)); } catch { return false; }
    }

    static bool IsVirtual(NetworkInterface n) => new[] { "virtual", "hyper-v", "vmware", "vethernet", "tap-", "vpn", "wireguard", "loopback", "tunnel" }.Any(x => (n.Name + " " + n.Description).Contains(x, StringComparison.OrdinalIgnoreCase));
    static bool IsWired(NetworkInterface n) => n.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit;
    static bool IsWireless(NetworkInterface n) => n.NetworkInterfaceType is NetworkInterfaceType.Wireless80211 or NetworkInterfaceType.Wwanpp or NetworkInterfaceType.Wwanpp2;
    static string Kind(NetworkInterface n) => IsWired(n) ? "ethernet" : "wifi";
    static int IndexOf(NetworkInterface n) { try { return n.GetIPProperties().GetIPv4Properties()?.Index ?? -1; } catch { return -1; } }

    static int BestInterfaceIndex() {
        try { uint dest = BitConverter.ToUInt32(IPAddress.Parse("8.8.8.8").GetAddressBytes(), 0); return GetBestInterface(dest, out uint index) == 0 ? (int)index : -1; } catch { return -1; }
    }

    [DllImport("iphlpapi.dll")] static extern int GetBestInterface(uint destAddr, out uint bestIfIndex);
}
