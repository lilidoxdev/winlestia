// C# 12, fichier MonitorDisplays.cs, cible Windows 11 / .NET 8, luminosité DDC/CI de chaque écran (interne et externe)
// Lectures : à l'ouverture du panneau, puis toutes les BrightnessPolicy.RefreshSeconds tant qu'il est ouvert ; mises en cache par écran.
// Écritures : un worker par écran, espacées de 120 ms, la dernière valeur l'emporte.
// Tout appel dxva2 passe par le `gate` partagé de MonitorBrightness : une lecture ne chevauche jamais une écriture.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PanneauxGlass;

internal sealed record DisplayBrightness(int Id, string Name, double Value, bool Available);
internal static partial class MonitorBrightness
{
    delegate bool EnumProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MonitorInfoEx
    {
        public uint Size; public int L1, T1, R1, B1, L2, T2, R2, B2; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr,
            SizeConst = 32)] public string Device;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct ConfigHeader
    {
        public uint Type, Size, LuidLow; public int LuidHigh; public uint Id;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SourceName
    {
        public ConfigHeader Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct TargetName
    {
        public ConfigHeader Header; public uint Flags, Technology; public ushort Manufacturer, Product; public uint Connector;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Name; [MarshalAs(UnmanagedType.ByValTStr,
            SizeConst = 128)] public string Path;
    }
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, EnumProc proc, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);
    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint paths, byte[] pathInfo, ref uint modes,
        byte[] modeInfo, IntPtr topology);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] static extern int GetSourceName(ref SourceName info);
    [DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")] static extern int GetTargetName(ref TargetName info);

    sealed class DisplayState
    {
        public string Name = ""; public double Value; public bool HasValue, Present, Writing; public int Failures; public double? Pending,
            Asked; public long LastWrite = -1_000_000; public List<TaskCompletionSource<bool>> Waiters = new();
    }
    static readonly object sync = new();
    static readonly Dictionary<int, DisplayState> displays = new();
    static long stamp = -1_000_000;

    static List<(int id, IntPtr monitor)> Screens()
    {
        var found = new List<(int, IntPtr)>();
        EnumProc proc = (monitor, _, _, _) => { var info = new MonitorInfoEx { Size = (uint)Marshal.SizeOf<MonitorInfoEx>(), Device = "" };
            if (GetMonitorInfo(monitor, ref info)) { int id = BrightnessPolicy.IdFromDevice(info.Device); if (id > 0 && found.All(f
            => f.Item1 != id)) found.Add((id, monitor)); } return true; };
        try
        {
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            Store.Log("MonitorDisplays.Screens : " + ex.GetType().Name + ": " + ex.Message);
        }
        GC.KeepAlive(proc);
        return found;
    }
    // Model name per display id, from QueryDisplayConfig (active paths). Empty map on any failure: the caller falls back to « Écran N ».
    static Dictionary<int, string> FriendlyNames()
    {
        var map = new Dictionary<int, string>();
        try
        {
            if (GetDisplayConfigBufferSizes(2, out uint np, out uint nm) != 0 || np == 0 || np > 64 || nm > 256)
                return map;
            var paths = new byte[np * 72];
            var modes = new byte[nm * 64];
            if (QueryDisplayConfig(2, ref np, paths, ref nm, modes, IntPtr.Zero) != 0)
                return map;
            for (int i = 0; i < np; i++)
            {
                int o = i * 72;
                var source = new SourceName { Header = new ConfigHeader { Type = 1, Size = (uint)Marshal.SizeOf<SourceName>(),
                    LuidLow = BitConverter.ToUInt32(paths, o), LuidHigh = BitConverter.ToInt32(paths, o + 4),
                    Id = BitConverter.ToUInt32(paths, o + 8) }, Device = "" };
                var target = new TargetName { Header = new ConfigHeader { Type = 2, Size = (uint)Marshal.SizeOf<TargetName>(),
                    LuidLow = BitConverter.ToUInt32(paths, o + 20), LuidHigh = BitConverter.ToInt32(paths, o + 24),
                    Id = BitConverter.ToUInt32(paths, o + 28) }, Name = "", Path = "" };
                if (GetSourceName(ref source) != 0 || GetTargetName(ref target) != 0)
                    continue;
                int id = BrightnessPolicy.IdFromDevice(source.Device);
                string name = (target.Name ?? "").Trim();
                if (id > 0 && name.Length > 0)
                    map[id] = name;
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Store.Log("MonitorDisplays.FriendlyNames : " + ex.GetType().Name + ": " + ex.Message);
        }
        return map;
    }
    static PhysicalMonitor[] OpenHandle(IntPtr monitor)
    {
        if (!GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out uint count) || count is 0 or > 16)
            return Array.Empty<PhysicalMonitor>();
        var monitors = new PhysicalMonitor[count];
        return GetPhysicalMonitorsFromHMONITOR(monitor, count, monitors) ? monitors : Array.Empty<PhysicalMonitor>();
    }
    static (bool ok, uint raw, uint min, uint max) TryRead(IntPtr monitor)
    {
        var monitors = OpenHandle(monitor);
        try
        {
            foreach (var m in monitors)
                if (GetMonitorBrightness(m.Handle, out uint min, out uint value, out uint max) && max > min)
                    return (true, value, min, max);
            return (false, 0, 0, 0);
        }
        finally { if (monitors.Length > 0) DestroyPhysicalMonitors((uint)monitors.Length, monitors); }
    }
    // Écran débranché depuis le dernier recensement : simplement false, sans exception.
    static bool WriteNow(int id, double percent)
    {
        foreach (var (screen, monitor) in Screens())
        {
            if (screen != id)
                continue;
            var monitors = OpenHandle(monitor);
            try
            {
                foreach (var m in monitors)
                    if (GetMonitorBrightness(m.Handle, out uint min, out _, out uint max) && max > min)
                        return SetMonitorBrightness(m.Handle, BrightnessPolicy.ToRaw(percent, min, max));
                return false;
            }
            finally { if (monitors.Length > 0) DestroyPhysicalMonitors((uint)monitors.Length, monitors); }
        }
        return false;
    }

    // Vue en cache des écrans vus par la dernière lecture (nom vide quand le modèle est inconnu).
    internal static IReadOnlyList<DisplayBrightness> Displays()
    {
        lock (sync)
            return displays.Where(d => d.Value.Present).OrderBy(d => d.Key).Select(d => new DisplayBrightness(d.Key, d.Value.Name,
                d.Value.Value, d.Value.HasValue && !BrightnessPolicy.Unavailable(d.Value.Failures))).ToList();
    }
    internal static long CacheAgeMs
    {
        get
        {
            lock (sync)
                return Environment.TickCount64 - stamp;
        }
    }
    // Panneau (ré)ouvert : les écrans marqués indisponibles ont une nouvelle chance.
    internal static void NewSession()
    {
        lock (sync)
            foreach (var d in displays.Values)
                d.Failures = 0;
    }

    internal static async Task<IReadOnlyList<DisplayBrightness>> ReadDisplaysAsync()
    {
        var (screens, names) = await Task.Run(() => (Screens(), FriendlyNames()));
        lock (sync)
        {
            foreach (var d in displays.Values)
                d.Present = false;
            foreach (var (id, _) in screens)
            {
                if (!displays.TryGetValue(id, out var d))
                    displays[id] = d = new DisplayState();
                d.Present = true;
                if (names.TryGetValue(id, out var name))
                    d.Name = name;
            }
        }
        foreach (var (id, monitor) in screens)
        {
            bool go;
            lock (sync)
            {
                var d = displays[id];
                go = BrightnessPolicy.CanRead(d.Writing, d.Pending.HasValue, BrightnessPolicy.Unavailable(d.Failures));
            }
            if (!go)
                continue;
            await gate.WaitAsync();   // held for one display at a time only
            try
            {
                lock (sync)
                {
                    var d = displays[id];
                    go = BrightnessPolicy.CanRead(d.Writing, d.Pending.HasValue, BrightnessPolicy.Unavailable(d.Failures));
                }
                if (!go)
                    continue;
                (bool ok, uint raw, uint min, uint max) = (false, 0, 0, 0);
                try
                {
                    (ok, raw, min, max) = await Task.Run(() => TryRead(monitor));
                }
                catch (Exception)
                {
                    // DDC/CI absent ou moniteur occupé : pas de journal à chaque déplacement du curseur.
                }
                lock (sync)
                {
                    var d = displays[id];
                    if (!ok)
                        d.Failures++;
                    else
                    {
                        d.HasValue = true;
                        d.Failures = 0;
                        if (!d.Writing && !d.Pending.HasValue)
                        {   // a write started during the read: its value wins over the stale hardware value
                            if (d.Asked is double asked && BrightnessPolicy.Matches(asked, raw, min, max))
                                d.Value = Math.Round(asked);
                            else
                            {
                                d.Asked = null;
                                d.Value = BrightnessPolicy.ToPercent(raw, min, max);
                            }
                        }
                    }
                }
            }
            finally { gate.Release(); }
        }
        lock (sync)
            stamp = Environment.TickCount64;
        return Displays();
    }

    // Met en file une écriture pour un écran ; la tâche renvoyée se termine avec le résultat de l'écriture qui a porté cette valeur
    // (ou de la plus récente qui l'a remplacée). Ne lève jamais d'exception.
    internal static Task<bool> SetDisplayAsync(int id, double percent)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        DisplayState state;
        bool known;
        lock (sync)
            known = displays.ContainsKey(id);
        if (!known && Screens().All(s => s.id != id))
            return Task.FromResult(false);
        lock (sync)
        {
            if (!displays.TryGetValue(id, out state!))
                displays[id] = state = new DisplayState();
            state.Pending = Math.Clamp(double.IsNaN(percent) ? 0 : percent, 0, 100);
            state.Waiters.Add(done);
            if (state.HasValue)
                state.Value = Math.Round(state.Pending.Value);
            if (state.Writing)
                return done.Task;
            state.Writing = true;
        }
        _ = Task.Run(() => DrainAsync(id, state));
        return done.Task;
    }
    static async Task DrainAsync(int id, DisplayState state)
    {
        while (true)
        {
            int wait;
            lock (sync)
                wait = BrightnessPolicy.RemainingDelay(state.LastWrite, Environment.TickCount64);
            if (wait > 0)
                await Task.Delay(wait);
            double percent;
            List<TaskCompletionSource<bool>> waiters;
            lock (sync)
            {
                if (!state.Pending.HasValue)
                {
                    state.Writing = false;
                    return;
                }
                percent = state.Pending.Value;
                state.Pending = null;
                waiters = state.Waiters;
                state.Waiters = new();
            }
            bool ok = false;
            try
            {
                await gate.WaitAsync();
                try
                {
                    ok = await Task.Run(() => WriteNow(id, percent));
                }
                finally { gate.Release(); }
            }
            catch (Exception)
            {
                // DDC/CI absent ou moniteur occupé : pas de journal à chaque déplacement du curseur.
            }
            lock (sync)
            {
                state.LastWrite = Environment.TickCount64;
                if (ok)
                {
                    state.Value = Math.Round(percent);
                    state.Asked = percent;
                    state.HasValue = true;
                    state.Failures = 0;
                }
            }
            foreach (var w in waiters)
                w.TrySetResult(ok);
        }
    }
}
