// Accès Wifi natif (wlanapi.dll) pour la liste réseau des contrôles rapides. Chaque appel s'exécute hors du thread de l'interface, le
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



internal sealed partial class WifiHost : IDisposable
{
    // Lignes brutes (une par groupe BSS signalé par Windows, infrastructure uniquement) ; la mémoire de la liste est libérée avant le
    // retour.
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
            return NetworkInformation.GetInternetConnectionProfile()?.GetNetworkConnectivityLevel() == NetworkConnectivityLevel
                .ConstrainedInternetAccess;
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
            // Un mot de passe saisi va toujours à Windows : un nouveau profil, ou (réseau connu) un remplacement qui garde en mémoire
            // une copie protégée de l'ancien profil à restaurer en cas d'échec.
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
                        // Profil par utilisateur uniquement. « Tous les utilisateurs » n'est utilisé que si Windows rejette lui-même
                        // la portée par utilisateur (paramètre invalide sans aucune raison de profil).
                        code = WlanNative.WlanSetProfile(handle, ref iface, ProfileUser, buffer, IntPtr.Zero, false, IntPtr.Zero,
                            out reason);
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
                var parameters = new WlanNative.ConnectionParameters
                {
                    Mode = 0,
                    Profile = name,
                    Ssid = IntPtr.Zero,
                    Bssids = IntPtr.Zero,
                    BssType = 1,
                    Flags = 0
                };
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
                    // Pas de notification disponible : sondage borné de l'indicateur de connexion, 20 s au plus, uniquement pendant la
                    // connexion.
                    long end = Environment.TickCount64 + ConnectTimeoutMs;
                    while (!finished && Environment.TickCount64 < end)
                    {
                        Thread.Sleep(ConnectPollMs);
                        finished = IsConnected(handle, iface, ssid);
                    }
                }
                key = finished ? WifiRules.MapReason(why, false) : IsConnected(handle, iface, ssid) ? "" : "timeout";
                if (key == "timeout")
                    WlanNative.WlanDisconnect(handle, ref iface, IntPtr.Zero);
            }
            // Ne jamais garder un profil créé pour une tentative échouée (il retiendrait un mauvais mot de passe) ; un profil qui
            // existait avant n'est jamais supprimé.
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
