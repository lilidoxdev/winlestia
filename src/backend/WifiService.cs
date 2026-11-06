// Native Wifi (wlanapi.dll) access for the quick-controls network list. Every call runs off the interface thread, the WLAN handle is opened and closed per operation
// (never kept at rest), the scan timer only runs while the list is open. Network names are never logged. A password only travels from the QML field to WlanSetProfile:
// it is never logged, never stored, never part of the state sent to the interface. The managed strings made by the JSON parser and the XML builder cannot be wiped
// (they wait for the garbage collector); only the char[] and the unmanaged copy handed to Windows are cleared.
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
    [DllImport(Dll)] internal static extern int WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiated, out IntPtr handle);
    [DllImport(Dll)] internal static extern int WlanCloseHandle(IntPtr handle, IntPtr reserved);
    [DllImport(Dll)] internal static extern int WlanEnumInterfaces(IntPtr handle, IntPtr reserved, out IntPtr list);
    [DllImport(Dll)] internal static extern int WlanScan(IntPtr handle, ref Guid iface, IntPtr ssid, IntPtr raw, IntPtr reserved);
    [DllImport(Dll)] internal static extern int WlanGetAvailableNetworkList(IntPtr handle, ref Guid iface, uint flags, IntPtr reserved, out IntPtr list);
    [DllImport(Dll)] internal static extern int WlanSetProfile(IntPtr handle, ref Guid iface, uint flags, IntPtr xml, IntPtr security, [MarshalAs(UnmanagedType.Bool)] bool overwrite, IntPtr reserved, out uint reason);
    [DllImport(Dll)] internal static extern int WlanConnect(IntPtr handle, ref Guid iface, ref ConnectionParameters parameters, IntPtr reserved);
    [DllImport(Dll)] internal static extern int WlanDisconnect(IntPtr handle, ref Guid iface, IntPtr reserved);
    [DllImport(Dll, CharSet = CharSet.Unicode)] internal static extern int WlanDeleteProfile(IntPtr handle, ref Guid iface, string profile, IntPtr reserved);
    [DllImport(Dll, CharSet = CharSet.Unicode)] internal static extern int WlanGetProfile(IntPtr handle, ref Guid iface, string profile, IntPtr reserved, out IntPtr xml, ref uint flags, out uint access);
    [DllImport(Dll)] internal static extern void WlanFreeMemory(IntPtr memory);
    [DllImport(Dll)] internal static extern int WlanRegisterNotification(IntPtr handle, uint source, [MarshalAs(UnmanagedType.Bool)] bool ignoreDuplicate, NotifyCallback? callback, IntPtr context, IntPtr reserved, out uint previous);
}

internal sealed class WifiHost : IDisposable
{
    const uint ProfileUser = 2, SourceAcm = 8, SourceNone = 0;
    const int NotifyConnectionComplete = 10, NotifyAttemptFail = 11, ConnectTimeoutMs = 20000, ScanPeriodMs = 15000, ResultDelayMs = 3000;
    const string PortalUrl = "http://www.msftconnecttest.com/redirect";

    readonly object gate = new();
    readonly SemaphoreSlim operation = new(1, 1);
    Timer? timer; bool open, disposed; int tick;
    WifiSnapshot snapshot = new("loading", false, false, new());
    string target = "", resultKind = "", resultKey = "", resultSsid = ""; int resultSeq; bool connecting;
    internal event Action? Changed;

    // Called from the bridge on every reply: scanning only while the list is open and the shell is not quiet.
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

    // State sent to the interface: the list the user sees and the outcome of the last action. Never a password.
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
            _ = Task.Run(() => { try { Refresh(false); } catch (Exception) { /* Rafraîchissement périodique : un échec est retenté au tick suivant, sans journal répété. */ } });
    }

    internal static void OpenPortal()
    {
        try
        {
            Process.Start(new ProcessStartInfo(PortalUrl) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) { Store.Log("Wi-Fi: page de connexion: " + ex.GetType().Name); }
    }

    // Only two fixed Windows pages can be opened; no privacy setting is changed.
    internal static void OpenSettings(bool location)
    {
        try
        {
            Process.Start(new ProcessStartInfo(location ? "ms-settings:privacy-location" : "ms-settings:network-wifi") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) { Store.Log("Wi-Fi: réglages: " + ex.GetType().Name); }
    }

    // `winlestia wifi`: read-only summary without any network name.
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

    // ---- native layer (always on a pool thread) ----

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

    // Raw rows (one per BSS group reported by Windows, infrastructure only); the list memory is freed before returning.
    static int ReadList(IntPtr handle, Guid iface, List<WifiEntry> rows)
    {
        int code = WlanNative.WlanGetAvailableNetworkList(handle, ref iface, 0, IntPtr.Zero, out var p);
        if (code != 0)
            return code;
        try
        {
            int count = Marshal.ReadInt32(p);
            for (int i = 0; i < count && i < 512; i++)
            {
                IntPtr n = p + 8 + i * 628;
                if (Marshal.ReadInt32(n, 548) != 1)
                    continue;
                int length = Math.Min(Math.Max(Marshal.ReadInt32(n, 512), 0), 32);
                var raw = new byte[length];
                if (length > 0)
                    Marshal.Copy(n + 516, raw, 0, length);
                string ssid = new UTF8Encoding(false, false).GetString(raw);
                int flags = Marshal.ReadInt32(n, 620);
                var security = WifiRules.Classify(Marshal.ReadInt32(n, 612), Marshal.ReadInt32(n, 616));
                if (ssid.Contains('�'))
                    security = WifiSecurity.Unsupported;
                string profile = (flags & 2) != 0 ? Marshal.PtrToStringUni(n, 256)?.Split('\0')[0] ?? "" : "";
                rows.Add(new WifiEntry(ssid, Marshal.ReadInt32(n, 604), security, (flags & 1) != 0, (flags & 2) != 0, profile));
            }
        }
        finally { WlanNative.WlanFreeMemory(p); }
        return 0;
    }

    static bool LocationDenied()
    {
        try
        {
            const string path = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location";
            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var k = root.OpenSubKey(path);
                if (string.Equals(k?.GetValue("Value") as string, "Deny", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or System.IO.IOException or UnauthorizedAccessException)
        {
            Store.Log("WifiService.LocationDenied : " + ex.GetType().Name + ": " + ex.Message);
        }
        return false;
    }

    static bool Restricted()
    {
        try
        {
            return NetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.ConstrainedInternetAccess;
        }
        catch { return false; }
    }

    static WifiSnapshot Read(bool scan)
    {
        int openCode = WlanNative.WlanOpenHandle(2, IntPtr.Zero, out _, out var handle);
        if (openCode != 0)
            return new(WifiRules.ListState(openCode, 0, 0, 0, false), false, false, new());
        try
        {
            var interfaces = Interfaces(handle);
            var rows = new List<WifiEntry>();
            int listCode = interfaces.Count == 0 ? 0 : 1;
            foreach (var iface in interfaces)
            {
                if (scan)
                {
                    var g = iface;
                    WlanNative.WlanScan(handle, ref g, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                }
                int code = ReadList(handle, iface, rows);
                if (code == 0 || listCode == 1)
                    listCode = code;
            }
            var grouped = WifiRules.Group(rows);
            bool connected = grouped.Any(e => e.Connected);
            string state = WifiRules.ListState(0, interfaces.Count, listCode, grouped.Count, grouped.Count == 0 && LocationDenied());
            return new(state, connected, connected && Restricted(), grouped);
        }
        finally { WlanNative.WlanCloseHandle(handle, IntPtr.Zero); }
    }

    static bool Find(IntPtr handle, string ssid, out Guid iface, out WifiEntry? entry)
    {
        iface = default;
        entry = null;
        foreach (var candidate in Interfaces(handle))
        {
            var rows = new List<WifiEntry>();
            if (ReadList(handle, candidate, rows) != 0)
                continue;
            entry = WifiRules.Group(rows.Where(r => r.Ssid == ssid)).FirstOrDefault();
            if (entry != null)
            {
                iface = candidate;
                return true;
            }
        }
        return false;
    }

    static bool IsConnected(IntPtr handle, Guid iface, string ssid)
    {
        var rows = new List<WifiEntry>();
        return ReadList(handle, iface, rows) == 0 && rows.Any(r => r.Ssid == ssid && r.Connected);
    }

    static void Wipe(IntPtr buffer, int bytes)
    {
        if (buffer == IntPtr.Zero)
            return;
        Marshal.Copy(new byte[bytes], 0, buffer, bytes);
        Marshal.FreeHGlobal(buffer);
    }

    static string ConnectCore(string ssid, char[] typed)
    {
        int openCode = WlanNative.WlanOpenHandle(2, IntPtr.Zero, out _, out var handle);
        if (openCode != 0)
            return openCode == 1062 ? "serviceStopped" : "noInterface";
        WlanNative.NotifyCallback? callback = null;
        bool registered = false;
        IntPtr backup = IntPtr.Zero;
        uint backupFlags = 0, scope = ProfileUser;
        try
        {
            if (!Find(handle, ssid, out var iface, out var entry) || entry == null)
                return "notFound";
            if (!WifiRules.CanConnect(entry.Security))
                return "unsupported";
            bool created = false, replaced = false;
            string profile = entry.Saved && entry.Profile.Length > 0 ? entry.Profile : ssid;
            // A typed password always goes to Windows: a new profile, or (known network) a replacement that keeps a protected in-memory copy of the old profile to restore on failure.
            if (typed.Length > 0 || !entry.Saved)
            {
                string text = new string(typed);
                string check = WifiRules.ValidatePassword(entry.Security, text);
                if (check.Length > 0)
                    return check;
                string xml;
                try
                {
                    xml = WifiRules.BuildProfile(ssid, entry.Security, text);
                }
                catch (ArgumentException) { return "invalid"; }
                text = "";
                Array.Clear(typed);
                int bytes = (xml.Length + 1) * 2;
                IntPtr buffer = Marshal.StringToHGlobalUni(xml);
                xml = "";
                bool replace = entry.Saved;
                int code;
                uint reason = 0;
                try
                {
                    if (!replace)
                    {
                        // Per-user profile only. All users is used solely when Windows rejects the per-user scope itself (invalid parameter without any profile reason).
                        code = WlanNative.WlanSetProfile(handle, ref iface, ProfileUser, buffer, IntPtr.Zero, false, IntPtr.Zero, out reason);
                        if (code == 87 && reason == 0)
                        {
                            scope = 0;
                            code = WlanNative.WlanSetProfile(handle, ref iface, 0, buffer, IntPtr.Zero, false, IntPtr.Zero, out reason);
                        }
                        if (code == 183)
                            replace = true;
                        else if (code == 0)
                            created = true;
                    }
                    else
                        code = 183;
                    if (replace)
                    {
                        uint flags = 0;
                        int got = WlanNative.WlanGetProfile(handle, ref iface, profile, IntPtr.Zero, out backup, ref flags, out _);
                        if (got != 0)
                        {
                            backup = IntPtr.Zero;
                            code = got;
                        }
                        else if ((flags & 1) != 0)
                            code = 5;
                        else
                        {
                            backupFlags = flags;
                            scope = flags & ProfileUser;
                            code = WlanNative.WlanSetProfile(handle, ref iface, scope, buffer, IntPtr.Zero, true, IntPtr.Zero, out reason);
                            replaced = code == 0;
                        }
                    }
                }
                finally { Wipe(buffer, bytes); }
                if (code != 0)
                    return reason == WifiRules.ReasonKeyFormat ? "badPassword" : WifiRules.MapApiError(code);
            }
            var done = new TaskCompletionSource<uint>(TaskCreationOptions.RunContinuationsAsynchronously);
            Guid wanted = iface;
            callback = (data, _) =>
            {
                try
                {
                    int source = Marshal.ReadInt32(data, 0), notification = Marshal.ReadInt32(data, 4);
                    if (source != (int)SourceAcm || (notification != NotifyConnectionComplete && notification != NotifyAttemptFail))
                        return;
                    var raw = new byte[16];
                    Marshal.Copy(data + 8, raw, 0, 16);
                    if (new Guid(raw) != wanted)
                        return;
                    IntPtr payload = Marshal.ReadIntPtr(data, IntPtr.Size == 8 ? 32 : 28);
                    uint why = payload == IntPtr.Zero ? 0u : (uint)Marshal.ReadInt32(payload, 560);
                    done.TrySetResult(notification == NotifyAttemptFail && why == 0 ? 0x10001u : why);
                }
                catch (Exception)
                {
                    // Callback natif : une exception ne doit jamais remonter vers Win32.
                }
            };
            registered = WlanNative.WlanRegisterNotification(handle, SourceAcm, false, callback, IntPtr.Zero, IntPtr.Zero, out _) == 0;
            IntPtr name = Marshal.StringToHGlobalUni(profile);
            int connectCode;
            try
            {
                var parameters = new WlanNative.ConnectionParameters { Mode = 0, Profile = name, Ssid = IntPtr.Zero, Bssids = IntPtr.Zero, BssType = 1, Flags = 0 };
                connectCode = WlanNative.WlanConnect(handle, ref iface, ref parameters, IntPtr.Zero);
            }
            finally { Marshal.FreeHGlobal(name); }
            string key;
            if (connectCode != 0)
                key = WifiRules.MapApiError(connectCode);
            else
            {
                uint why = 0;
                bool finished = false;
                if (registered)
                {
                    finished = done.Task.Wait(ConnectTimeoutMs);
                    if (finished)
                        why = done.Task.Result;
                }
                else
                {
                    // No notification available: bounded poll of the connected flag, at most 20 s, only during the connection.
                    long end = Environment.TickCount64 + ConnectTimeoutMs;
                    while (!finished && Environment.TickCount64 < end)
                    {
                        Thread.Sleep(1000);
                        finished = IsConnected(handle, iface, ssid);
                    }
                }
                key = finished ? WifiRules.MapReason(why, false) : IsConnected(handle, iface, ssid) ? "" : "timeout";
                if (key == "timeout")
                    WlanNative.WlanDisconnect(handle, ref iface, IntPtr.Zero);
            }
            // Never keep a profile created for a failed attempt (it would hold a wrong password); a profile that existed before is never deleted.
            if (key != "" && created)
                WlanNative.WlanDeleteProfile(handle, ref iface, profile, IntPtr.Zero);
            else if (key != "" && replaced && backup != IntPtr.Zero)
                WlanNative.WlanSetProfile(handle, ref iface, backupFlags & ProfileUser, backup, IntPtr.Zero, true, IntPtr.Zero, out _);
            return key;
        }
        finally
        {
            if (registered)
                WlanNative.WlanRegisterNotification(handle, SourceNone, false, null, IntPtr.Zero, IntPtr.Zero, out _);
            GC.KeepAlive(callback);
            if (backup != IntPtr.Zero)
            {
                int n = 0;
                while (Marshal.ReadInt16(backup, n * 2) != 0 && n < 65536)
                    n++;
                Marshal.Copy(new byte[n * 2], 0, backup, n * 2);
                WlanNative.WlanFreeMemory(backup);
            }
            WlanNative.WlanCloseHandle(handle, IntPtr.Zero);
            Array.Clear(typed);
        }
    }

    static string ForgetCore(string ssid)
    {
        int openCode = WlanNative.WlanOpenHandle(2, IntPtr.Zero, out _, out var handle);
        if (openCode != 0)
            return openCode == 1062 ? "serviceStopped" : "noInterface";
        try
        {
            if (!Find(handle, ssid, out var iface, out var entry) || entry == null || !entry.Saved || entry.Profile.Length == 0)
                return "notFound";
            if (entry.Connected)
                WlanNative.WlanDisconnect(handle, ref iface, IntPtr.Zero);
            return WifiRules.MapApiError(WlanNative.WlanDeleteProfile(handle, ref iface, entry.Profile, IntPtr.Zero));
        }
        finally { WlanNative.WlanCloseHandle(handle, IntPtr.Zero); }
    }
}
