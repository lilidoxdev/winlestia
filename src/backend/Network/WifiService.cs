// Accès Wifi natif (wlanapi.dll) pour la liste réseau des contrôles rapides. Chaque appel s'exécute hors du thread de l'interface, le
// handle WLAN est ouvert et fermé par opération
// (jamais conservé au repos), le minuteur de scan ne tourne que tant que la liste est ouverte. Les noms de réseau ne sont jamais
// journalisés. Un mot de passe ne voyage que du champ QML vers WlanSetProfile :
// il n'est jamais journalisé, jamais stocké, jamais inclus dans l'état envoyé à l'interface. Les chaînes managées produites par
// l'analyseur JSON et le constructeur XML ne peuvent pas être effacées
// (elles attendent le ramasse-miettes) ; seuls le char[] et la copie non managée remise à Windows sont effacés.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.Networking.Connectivity;

namespace PanneauxGlass;

sealed record WifiSnapshot(string State, bool Connected, bool Limited, List<WifiEntry> Networks);

static class WlanNative
{
    internal delegate void NotifyCallback(IntPtr data, IntPtr context);
    [StructLayout(LayoutKind.Sequential)]
    internal struct ConnectionParameters
    {
        public int Mode; public IntPtr Profile; public IntPtr Ssid; public IntPtr Bssids; public int BssType; public int Flags;
    }
    const string Dll = "wlanapi.dll";
    [DllImport(Dll)] internal static extern int WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiated,
        out IntPtr handle);
    [DllImport(Dll)] internal static extern int WlanCloseHandle(IntPtr handle, IntPtr reserved);
    [DllImport(Dll)] internal static extern int WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
    [DllImport(Dll)] internal static extern int WlanScan(IntPtr handle, ref Guid iface, IntPtr ssid, IntPtr raw, IntPtr reserved);
    [DllImport(Dll)] internal static extern int WlanGetAvailableNetworkList(IntPtr handle, ref Guid iface, uint flags, IntPtr reserved,
        out IntPtr list);
    [DllImport(Dll)] internal static extern int WlanSetProfile(IntPtr handle, ref Guid iface, uint flags, IntPtr xml, IntPtr security,
        [MarshalAs(UnmanagedType.Bool)] bool overwrite, IntPtr reserved, out uint reason);
    [DllImport(Dll)] internal static extern int WlanConnect(IntPtr handle, ref Guid iface, ref ConnectionParameters parameters,
        IntPtr reserved);
    [DllImport(Dll)] internal static extern int WlanDisconnect(IntPtr handle, ref Guid iface, IntPtr reserved);
    [DllImport(Dll, CharSet = CharSet.Unicode)] internal static extern int WlanDeleteProfile(IntPtr handle, ref Guid iface, string profile,
        IntPtr reserved);
    [DllImport(Dll, CharSet = CharSet.Unicode)] internal static extern int WlanGetProfile(IntPtr handle, ref Guid iface, string profile,
        IntPtr reserved, out IntPtr xml, ref uint flags, out uint access);
    [DllImport(Dll)] internal static extern void WlanFreeMemory(IntPtr memory);
    [DllImport(Dll)] internal static extern int WlanRegisterNotification(IntPtr handle, uint source,
        [MarshalAs(UnmanagedType.Bool)] bool ignoreDuplicate, NotifyCallback? callback, IntPtr context, IntPtr reserved,
            out uint previous);
}

internal sealed partial class WifiHost : IDisposable
{
    // Sondage borné : une connexion prend plusieurs secondes, inutile d'interroger plus souvent.
    const int ConnectPollMs = 1000;
    const uint ProfileUser = 2, SourceAcm = 8, SourceNone = 0;
    const int NotifyConnectionComplete = 10, NotifyAttemptFail = 11, ConnectTimeoutMs = 20000, ScanPeriodMs = 15000, ResultDelayMs = 3000;
    const string PortalUrl = "http://www.msftconnecttest.com/redirect";

    readonly object gate = new();
    readonly SemaphoreSlim operation = new(1, 1);
    Timer? timer; bool open, disposed; int tick;
    WifiSnapshot snapshot = new("loading", false, false, new());
    string target = "", resultKind = "", resultKey = "", resultSsid = ""; int resultSeq; bool connecting;
    internal event Action? Changed;

    // Appelé depuis le pont à chaque réponse : scan uniquement tant que la liste est ouverte et que le shell n'est pas au repos.
    internal void SetOpen(bool want)
    {
        lock (gate)
        {
            if (disposed || want == open)
                return;
            open = want;
            if (want)
            {
                tick = 0;
                timer = new Timer(_ => Tick(), null, 0, Timeout.Infinite);
            }
            else
            {
                timer?.Dispose();
                timer = null;
                snapshot = new("loading", false, false, new());
                resultKind = resultKey = resultSsid = "";
            }
        }
    }

    void Tick()
    {
        bool scan;
        lock (gate)
        {
            if (!open || disposed)
                return;
            scan = tick != 1;
            tick++;
        }
        try
        {
            Refresh(scan);
        }
        catch (Exception)
        {
            // Rafraîchissement périodique : un échec est retenté au tick suivant, sans journal répété.
        }
        lock (gate)
        {
            if (open && !disposed)
                timer?.Change(tick == 1 ? ResultDelayMs : ScanPeriodMs, Timeout.Infinite);
        }
    }

    void Refresh(bool scan)
    {
        if (!operation.Wait(0))
            return;
        try
        {
            var read = Read(scan);
            lock (gate)
            {
                if (!open)
                    return;
                snapshot = read;
            }
            Changed?.Invoke();
        }
        finally { operation.Release(); }
    }

    // État envoyé à l'interface : la liste que voit l'utilisateur et le résultat de la dernière action. Jamais un mot de passe.
    internal object Snapshot()
    {
        lock (gate)
        {
            return new
            {
                state = snapshot.State,
                connected = snapshot.Connected,
                limited = snapshot.Limited,
                connecting,
                target,
                result = new
                {
                    seq = resultSeq,
                    kind = resultKind,
                    key = resultKey,
                    ssid = resultSsid
                },
                networks = snapshot.Networks.Select(n => new
                {
                    ssid = n.Ssid,
                    signal = n.Signal,
                    bars = WifiRules.Bars(n.Signal),
                    security = WifiRules.SecurityLabel(n.Security),
                    secure = n.Security != WifiSecurity.Open,
                    connected = n.Connected,
                    saved = n.Saved,
                    supported = WifiRules.CanConnect(n.Security)
                }).ToArray()
            };
        }
    }

    internal async Task ConnectAsync(string ssid, string? password)
    {
        var chars = (password ?? "").ToCharArray();
        password = null;
        if (!await operation.WaitAsync(0))
        {
            Array.Clear(chars);
            return;
        }
        lock (gate)
        {
            connecting = true;
            target = ssid;
            resultKind = "connect";
            resultKey = "";
        }
        string key;
        try
        {
            key = await Task.Run(() => ConnectCore(ssid, chars));
        }
        catch (Exception ex) { key = "failed"; Store.Log("Wi-Fi: connexion interrompue (" + ex.GetType().Name + ")"); }
        finally { Array.Clear(chars); operation.Release(); }
        Finish("connect", key, ssid);
    }

    internal async Task ForgetAsync(string ssid)
    {
        if (!await operation.WaitAsync(0))
            return;
        lock (gate)
        {
            connecting = true;
            target = ssid;
            resultKind = "forget";
            resultKey = "";
        }
        string key;
        try
        {
            key = await Task.Run(() => ForgetCore(ssid));
        }
        catch (Exception ex) { key = "failed"; Store.Log("Wi-Fi: oubli interrompu (" + ex.GetType().Name + ")"); }
        finally { operation.Release(); }
        Finish("forget", key, ssid);
    }

    void Finish(string kind, string key, string ssid)
    {
        lock (gate)
        {
            connecting = false;
            target = "";
            resultKind = kind;
            resultKey = key;
            resultSsid = ssid;
            resultSeq++;
        }
        Changed?.Invoke();
        bool stillOpen;
        lock (gate)
            stillOpen = open;
        if (stillOpen)
            _ = Task.Run(() => { try { Refresh(false); } catch (Exception) {
                /* Rafraîchissement périodique : un échec est retenté au tick suivant, sans journal répété. */ } });
    }

    internal static void OpenPortal()
    {
        try
        {
            Process.Start(new ProcessStartInfo(PortalUrl) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) { Store.Log("Wi-Fi: page de connexion: " + ex.GetType().Name); }
    }

    // Seules deux pages Windows fixes peuvent être ouvertes ; aucun réglage de confidentialité n'est modifié.
    internal static void OpenSettings(bool location)
    {
        try
        {
            Process.Start(new ProcessStartInfo(location ? "ms-settings:privacy-location" : "ms-settings:network-wifi") {
                UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) { Store.Log("Wi-Fi: réglages: " + ex.GetType().Name); }
    }

    // `winlestia wifi` : résumé en lecture seule, sans aucun nom de réseau.
    internal static Task<string> SummaryAsync() => Task.Run(() =>
    {
        var s = Read(false);
        bool available = s.State is not ("noInterface" or "serviceStopped");
        return JsonSerializer.Serialize(new
        {
            available,
            connected = s.Connected,
            networks = s.Networks.Count,
            reason = s.State == "ok" ? "" : s.State
        });
    });

    public void Dispose()
    {
        SetOpen(false);
        lock (gate)
            disposed = true;
    }

    // ---- couche native (toujours sur un thread du pool) ----

    static List<Guid> Interfaces(IntPtr handle)
    {
        var list = new List<Guid>();
        if (WlanNative.WlanEnumInterfaces(handle, IntPtr.Zero, out var p) != 0)
            return list;
        try
        {
            int count = Marshal.ReadInt32(p);
            for (int i = 0; i < count && i < 16; i++)
            {
                var bytes = new byte[16];
                Marshal.Copy(p + 8 + i * 532, bytes, 0, 16);
                list.Add(new Guid(bytes));
            }
        }
        finally { WlanNative.WlanFreeMemory(p); }
        return list;
    }
}
