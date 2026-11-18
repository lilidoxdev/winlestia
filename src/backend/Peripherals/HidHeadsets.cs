// language: C# 12, file: HidHeadsets.cs, target: Windows 11 / .NET 8, batterie des casques USB à dongle (SteelSeries Arctis) via HID,
// requête en lecture seule
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace PanneauxGlass;

// État décodé d'un casque : Available = false quand le casque est éteint / hors de portée de sa base.
internal sealed record HeadsetStatus(bool Available, int Percent, bool Charging);

internal static class HidHeadsets
{
    // Une ligne par modèle : seuls les modèles dont le protocole est connu y figurent. Request = octets envoyés après l'identifiant de
    // rapport 0.
    // Target : interface HID à interroger (fragment « mi_xx » du chemin) et sa page d'usage ; seules ces interfaces sont ouvertes.
    internal sealed record Target(string Interface, ushort UsagePage);
    internal sealed record Model(ushort Pid, string Name, Target[] Targets, byte[] Request, Func<byte[], HeadsetStatus?> Parse);

    internal const ushort SteelSeries = 0x1038;

    internal static readonly Model[] Models =
    {
        new(0x2212, "Arctis 7P+", new Target[] { new("&mi_03", 0xFFC0) }, new byte[] { 0xB0 }, ParseArctis7PlusStatus),
    };

    internal static string Id(ushort vid, ushort pid)
        => "hid:" + vid.ToString("x4", CultureInfo.InvariantCulture) + ":" + pid.ToString("x4", CultureInfo.InvariantCulture);

    // Pure. Réponse à la requête 0xB0, octets bruts avec l'identifiant de rapport : [0]=0x00, [1]=0xB0 (écho), [2]=état (1 = casque
    // éteint), [3]=niveau 0..4 (paliers de 25 %), [4]=1 si en charge. Observé : 00 B0 03 03 03 00… (casque allumé).
    internal static HeadsetStatus? ParseArctis7PlusStatus(byte[]? report)
    {
        if (report == null || report.Length < 5 || report[1] != 0xB0)
            return null;
        if (report[2] == 0x01)
            return new HeadsetStatus(false, 0, false);
        if (report[3] > 4)
            return null;
        return new HeadsetStatus(true, report[3] * 25, report[4] == 0x01);
    }

    // ---- HID natif ----
    [StructLayout(LayoutKind.Sequential)]
    struct SpDeviceInterfaceData { public uint Size; public Guid InterfaceClassGuid; public uint Flags; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HidpCaps
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort LinkCollectionNodes, InputButtonCaps, InputValueCaps, InputDataIndices, OutputButtonCaps, OutputValueCaps,
            OutputDataIndices, FeatureButtonCaps, FeatureValueCaps, FeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Overlapped { public IntPtr Internal, InternalHigh; public uint Offset, OffsetHigh; public IntPtr Event; }

    [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr data, out HidpCaps caps);
    const int HidpStatusSuccess = 0x00110000;
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevsW(ref Guid classGuid, IntPtr enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, uint index, ref SpDeviceInterfaceData data);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref SpDeviceInterfaceData data, IntPtr detail, uint size,
        out uint required, IntPtr info);
    [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags,
        IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool WriteFile(SafeFileHandle handle, IntPtr buffer, uint count, IntPtr written, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool ReadFile(SafeFileHandle handle, IntPtr buffer, uint count, IntPtr read, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetOverlappedResult(SafeFileHandle handle, IntPtr overlapped, out uint transferred, bool wait);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CancelIoEx(SafeFileHandle handle, IntPtr overlapped);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateEventW(IntPtr attributes, bool manual, bool initial, IntPtr name);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);

    const uint DigcfPresent = 2, DigcfDeviceInterface = 0x10;
    const uint GenericRead = 0x80000000, GenericWrite = 0x40000000, ShareReadWrite = 3, OpenExisting = 3, FlagOverlapped = 0x40000000;
    const int ErrorIoPending = 997;

    // Chemins d'interface HID d'un fournisseur / produit, triés par numéro d'interface (MI_xx).
    internal static List<string> InterfacePaths(ushort vid, ushort pid)
    {
        var paths = new List<string>();
        HidD_GetHidGuid(out var guid);
        IntPtr set = SetupDiGetClassDevsW(ref guid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (set == new IntPtr(-1))
            return paths;
        string key = "vid_" + vid.ToString("x4", CultureInfo.InvariantCulture) + "&pid_" + pid.ToString("x4", CultureInfo.InvariantCulture);
        try
        {
            var data = new SpDeviceInterfaceData { Size = (uint)Marshal.SizeOf<SpDeviceInterfaceData>() };
            for (uint i = 0; SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data); i++)
            {
                SetupDiGetDeviceInterfaceDetailW(set, ref data, IntPtr.Zero, 0, out uint need, IntPtr.Zero);
                if (need < 8 || need > 4096)
                    continue;
                IntPtr detail = Marshal.AllocHGlobal((int)need);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetailW(set, ref data, detail, need, out _, IntPtr.Zero))
                        continue;
                    string path = Marshal.PtrToStringUni(detail + 4) ?? "";
                    if (path.Contains(key, StringComparison.OrdinalIgnoreCase))
                        paths.Add(path);
                }
                finally
                {
                    Marshal.FreeHGlobal(detail);
                }
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
        return paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    // Envoie la requête puis lit les rapports d'entrée pendant timeoutMs ; rend chaque rapport brut reçu (octet 0 = identifiant de
    // rapport).
    // Les interfaces dont la page d'usage diffère de usagePage ne sont pas interrogées.
    internal static List<byte[]> Exchange(string path, byte[] request, ushort usagePage, int timeoutMs, out HidpCaps caps,
        Action<string>? log = null)
    {
        var reports = new List<byte[]>();
        caps = default;
        using var handle = CreateFileW(path, GenericRead | GenericWrite, ShareReadWrite, IntPtr.Zero, OpenExisting, FlagOverlapped,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastWin32Error();
            log?.Invoke(error == 32 ? "CreateFile: violation de partage (casque tenu en exclusivité, p. ex. SteelSeries GG)"
                : "CreateFile: erreur " + error);
            return reports;
        }
        if (!HidD_GetPreparsedData(handle, out IntPtr pre))
            return reports;
        try
        {
            int status = HidP_GetCaps(pre, out caps);
            if (status != HidpStatusSuccess || caps.UsagePage != usagePage || caps.InputReportByteLength == 0
                || caps.OutputReportByteLength == 0)
                return reports;
        }
        finally
        {
            HidD_FreePreparsedData(pre);
        }
        int inLen = caps.InputReportByteLength, outLen = caps.OutputReportByteLength, ovSize = Marshal.SizeOf<Overlapped>();
        IntPtr readBuf = IntPtr.Zero, writeBuf = IntPtr.Zero, readOv = IntPtr.Zero, writeOv = IntPtr.Zero;
        IntPtr readEvent = IntPtr.Zero, writeEvent = IntPtr.Zero;
        bool readPending = false, leakRead = false;
        try
        {
            readBuf = Marshal.AllocHGlobal(inLen);
            writeBuf = Marshal.AllocHGlobal(outLen);
            readOv = Marshal.AllocHGlobal(ovSize);
            writeOv = Marshal.AllocHGlobal(ovSize);
            readEvent = CreateEventW(IntPtr.Zero, true, false, IntPtr.Zero);
            writeEvent = CreateEventW(IntPtr.Zero, true, false, IntPtr.Zero);
            if (readEvent == IntPtr.Zero || writeEvent == IntPtr.Zero)
                return reports;
            for (int i = 0; i < outLen; i++)
                Marshal.WriteByte(writeBuf, i, i == 0 ? (byte)0 : i - 1 < request.Length ? request[i - 1] : (byte)0);
            Marshal.StructureToPtr(new Overlapped { Event = writeEvent }, writeOv, false);
            // Lecture armée avant l'écriture pour ne pas manquer la réponse.
            long deadline = Environment.TickCount64 + timeoutMs;
            bool wrote = false;
            while (Environment.TickCount64 < deadline && reports.Count < 8)
            {
                if (!readPending)
                {
                    Marshal.StructureToPtr(new Overlapped { Event = readEvent }, readOv, false);
                    if (!ReadFile(handle, readBuf, (uint)inLen, IntPtr.Zero, readOv) && Marshal.GetLastWin32Error() != ErrorIoPending)
                        break;
                    readPending = true;
                }
                if (!wrote)
                {
                    wrote = true;
                    if (!WriteFile(handle, writeBuf, (uint)outLen, IntPtr.Zero, writeOv) && Marshal.GetLastWin32Error() != ErrorIoPending)
                        break;
                    if (WaitForSingleObject(writeEvent, 500) != 0)
                    {
                        CancelIoEx(handle, writeOv);
                        if (WaitForSingleObject(writeEvent, 1000) != 0)
                        {
                            // Écriture jamais terminée : le noyau peut encore lire writeBuf / écrire writeOv, on ne les libère pas.
                            log?.Invoke("WriteFile: annulation sans effet, mémoire conservée");
                            writeBuf = writeOv = IntPtr.Zero;
                            writeEvent = IntPtr.Zero;
                        }
                        break;
                    }
                }
                uint wait = (uint)Math.Max(1, deadline - Environment.TickCount64);
                if (WaitForSingleObject(readEvent, wait) != 0)
                    break;
                readPending = false;
                if (!GetOverlappedResult(handle, readOv, out uint got, false))
                    break;   // lecture avortée (périphérique retiré) : ne pas réarmer
                if (got > 0)
                {
                    var report = new byte[got];
                    Marshal.Copy(readBuf, report, 0, (int)got);
                    reports.Add(report);
                }
            }
        }
        finally
        {
            if (readPending)
            {
                CancelIoEx(handle, readOv);
                if (WaitForSingleObject(readEvent, 1000) != 0)
                {
                    // Lecture non terminée : le noyau peut encore écrire dans readBuf / readOv, on les laisse (fuite volontaire).
                    leakRead = true;
                    log?.Invoke("ReadFile: annulation sans effet, mémoire conservée");
                }
            }
            if (!leakRead)
            {
                if (readEvent != IntPtr.Zero)
                    CloseHandle(readEvent);
                if (readBuf != IntPtr.Zero)
                    Marshal.FreeHGlobal(readBuf);
                if (readOv != IntPtr.Zero)
                    Marshal.FreeHGlobal(readOv);
            }
            if (writeEvent != IntPtr.Zero)
                CloseHandle(writeEvent);
            if (writeBuf != IntPtr.Zero)
                Marshal.FreeHGlobal(writeBuf);
            if (writeOv != IntPtr.Zero)
                Marshal.FreeHGlobal(writeOv);
        }
        return reports;
    }

    // Une requête par modèle présent. Un casque dont la base répond « éteint » est rendu déconnecté ; sans réponse, il est ignoré.
    internal static List<PeripheralCandidate> Scan(Action<string>? trace = null)
    {
        var list = new List<PeripheralCandidate>();
        foreach (var model in Models)
        {
            try
            {
                var paths = InterfacePaths(SteelSeries, model.Pid);
                foreach (var target in model.Targets)
                {
                    string? path = paths.FirstOrDefault(p => p.Contains(target.Interface, StringComparison.OrdinalIgnoreCase));
                    if (path == null)
                        continue;
                    var reports = Exchange(path, model.Request, target.UsagePage, 500, out _,
                        m => trace?.Invoke(model.Name + " " + target.Interface + ": " + m));
                    var status = reports.Select(model.Parse).FirstOrDefault(r => r != null);
                    if (status == null)
                        continue;
                    list.Add(new(Id(SteelSeries, model.Pid), model.Name, "hid", "headset", status.Available,
                        status.Available ? status.Percent : null, status.Available ? status.Charging : null, true));
                    break;   // une interface a répondu : les suivantes ne sont pas ouvertes
                }
            }
            catch (Exception)
            {
            }
        }
        return list;
    }
}
