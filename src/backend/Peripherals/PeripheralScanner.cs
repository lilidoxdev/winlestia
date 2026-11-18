// language: C# 12, file: PeripheralScanner.cs, target: Windows 11 / .NET 8, batterie des périphériques : couche native (SetupAPI, WinRT,
// XInput), lecture seule
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
namespace PanneauxGlass;

internal static class PeripheralScanner
{
    // ---- XInput ----
    [StructLayout(LayoutKind.Sequential)]
    struct XInputBattery { public byte Type, Level; }

    [DllImport("xinput1_4.dll")]
    static extern uint XInputGetBatteryInformation(uint userIndex, byte devType, out XInputBattery info);

    internal static List<PeripheralCandidate> ScanXInput()
    {
        var list = new List<PeripheralCandidate>();
        for (uint i = 0; i < 4; i++)
        {
            try
            {
                if (XInputGetBatteryInformation(i, 0, out var b) != 0)
                    continue;
                int? percent = PeripheralLogic.MapXInputLevel(b.Type, b.Level);
                if (percent != null)
                    list.Add(new("xinput:" + i, "Xbox controller " + (i + 1), "xinput", "gamepad", true, percent, null, true));
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                break;
            }
        }
        return list;
    }

    // ---- SetupAPI : propriétés PnP Bluetooth ----
    [StructLayout(LayoutKind.Sequential)]
    struct SpDevinfoData { public uint Size; public Guid ClassGuid; public uint DevInst; public IntPtr Reserved; }

    [StructLayout(LayoutKind.Sequential)]
    struct DevPropKey { public Guid Fmtid; public uint Pid; }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr hwnd, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInfo(IntPtr set, uint index, ref SpDevinfoData data);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SpDevinfoData data, ref DevPropKey key, out uint type, byte[] buffer,
        uint size, out uint required, uint flags);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceInstanceIdW(IntPtr set, ref SpDevinfoData data, StringBuilder id, uint size, out uint required);
    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    const uint DigcfPresent = 2, DigcfAllClasses = 4;
    static readonly Guid BatteryFmt = new("104EA319-6EE2-4701-BD47-8DDBF425BBE5");
    static readonly Guid BluetoothFmt = new("2BD67D8B-8BEB-48D5-87E0-6CDA3428040A");
    static readonly Guid DeviceFmt = new("A45C254E-DF1C-4EFD-8020-67D146A850E0");
    static readonly Regex AddressInId = new(@"(?:DEV_|&)([0-9A-F]{12})(?:_C|\\|$)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    internal sealed class PnpInfo
    {
        public string Name = "";
        public uint? Cod;
        public int? Battery;
    }

    static byte[]? Prop(IntPtr set, ref SpDevinfoData data, Guid fmt, uint pid, uint expectedType)
    {
        var key = new DevPropKey { Fmtid = fmt, Pid = pid };
        var buf = new byte[512];
        if (!SetupDiGetDevicePropertyW(set, ref data, ref key, out uint type, buf, (uint)buf.Length, out uint need, 0) || type
            != expectedType)
            return null;
        return buf.AsSpan(0, (int)Math.Min(need, (uint)buf.Length)).ToArray();
    }

    // Une entrée par adresse : batterie, CoD et nom des nœuds BTHENUM / BTHLE.
    internal static Dictionary<ulong, PnpInfo> ScanPnp()
    {
        var map = new Dictionary<ulong, PnpInfo>();
        foreach (string enumerator in new[] { "BTHENUM", "BTHLE" })
        {
            IntPtr set = SetupDiGetClassDevsW(IntPtr.Zero, enumerator, IntPtr.Zero, DigcfPresent | DigcfAllClasses);
            if (set == new IntPtr(-1))
                continue;
            try
            {
                var data = new SpDevinfoData { Size = (uint)Marshal.SizeOf<SpDevinfoData>() };
                for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
                {
                    ulong address = 0;
                    var addrBytes = Prop(set, ref data, BluetoothFmt, 1, 0x12);
                    if (addrBytes != null)
                        TryHex(Encoding.Unicode.GetString(addrBytes).TrimEnd('\0'), out address);
                    if (address == 0)
                    {
                        var id = new StringBuilder(256);
                        if (SetupDiGetDeviceInstanceIdW(set, ref data, id, 256, out _))
                        {
                            var m = AddressInId.Match(id.ToString());
                            if (m.Success)
                                TryHex(m.Groups[1].Value, out address);
                        }
                    }
                    if (address == 0)
                        continue;
                    if (!map.TryGetValue(address, out var info))
                        map[address] = info = new PnpInfo();
                    var battery = Prop(set, ref data, BatteryFmt, 2, 3);
                    if (battery is { Length: >= 1 } && battery[0] <= 100)
                        info.Battery ??= battery[0];
                    var cod = Prop(set, ref data, BluetoothFmt, 10, 7);
                    if (cod is { Length: >= 4 })
                        info.Cod ??= BitConverter.ToUInt32(cod, 0);
                    var name = Prop(set, ref data, DeviceFmt, 14, 0x12);
                    if (name != null && info.Name.Length == 0)
                        info.Name = Encoding.Unicode.GetString(name).TrimEnd('\0');
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
        }
        return map;
    }

    internal static bool TryHex(string text, out ulong value)
        => ulong.TryParse(text.Replace(":", "").Replace("-", ""), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

    // ---- WinRT : appairés + connectés ----
    sealed record Aep(ulong Address, string Name, bool Connected);

    // Rend null si la tâche dépasse ms ; « late » reçoit (et peut libérer) un résultat arrivé après l'échéance.
    static async Task<T?> Timeout<T>(Task<T> task, int ms, Action<T>? late = null) where T : class
    {
        if (await Task.WhenAny(task, Task.Delay(ms)) == task)
            return await task;
        if (late != null)
            _ = task.ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion && t.Result != null)
                    late(t.Result);
            }, TaskScheduler.Default);
        else
            _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        return null;
    }

    // Absence de service Batterie : pas de nouvelle lecture directe (qui réveille le périphérique) avant ~10 min.
    static readonly System.Collections.Concurrent.ConcurrentDictionary<ulong, long> noBatteryUntil = new();
    const long NoBatteryRetryMs = 10 * 60 * 1000;

    static async Task<List<Aep>> PairedAsync(string selector)
    {
        var props = new[] { "System.Devices.Aep.IsConnected", "System.Devices.Aep.DeviceAddress" };
        var found = await Timeout(DeviceInformation.FindAllAsync(selector, props).AsTask(), 8000);
        if (found == null)
            throw new TimeoutException();
        var list = new List<Aep>();
        foreach (var d in found)
        {
            string text = d.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var a) ? a as string ?? "" : "";
            if (!TryHex(text, out ulong address) && !TryHex(d.Id[(d.Id.LastIndexOf('-') + 1)..], out address))
                continue;
            bool connected = d.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var c) && c is true;
            list.Add(new(address, d.Name ?? "", connected));
        }
        return list;
    }

    static async Task<(int? percent, ushort? appearance)> ReadLeAsync(ulong address, bool needBattery)
    {
        BluetoothLEDevice? device = null;
        try
        {
            device = await Timeout(BluetoothLEDevice.FromBluetoothAddressAsync(address).AsTask(), 4000, late => late.Dispose());
            if (device == null)
                return (null, null);
            ushort? appearance = null;
            try { appearance = device.Appearance?.RawValue; } catch (Exception) { }
            int? percent = null;
            if (needBattery)
            {
                // Cache d'abord (ne réveille pas le périphérique) ; lecture directe seulement si le cache est vide.
                percent = await ReadBatteryAsync(device, BluetoothCacheMode.Cached);
                bool retry = !noBatteryUntil.TryGetValue(address, out long until) || Environment.TickCount64 >= until;
                if (percent == null && retry)
                {
                    percent = await ReadBatteryAsync(device, BluetoothCacheMode.Uncached);
                    if (percent == null)
                        noBatteryUntil[address] = Environment.TickCount64 + NoBatteryRetryMs;
                    else
                        noBatteryUntil.TryRemove(address, out _);
                }
            }
            return (percent, appearance);
        }
        catch (Exception)
        {
            return (null, null);
        }
        finally
        {
            device?.Dispose();
        }
    }

    static async Task<int?> ReadBatteryAsync(BluetoothLEDevice device, BluetoothCacheMode mode)
    {
        try
        {
            var services = await Timeout(device.GetGattServicesForUuidAsync(GattServiceUuids.Battery, mode).AsTask(), 3000);
            var service = services?.Services.FirstOrDefault();
            if (service == null)
                return null;
            using (service)
            {
                var chars = await Timeout(service.GetCharacteristicsForUuidAsync(GattCharacteristicUuids.BatteryLevel, mode).AsTask(),
                    3000);
                var characteristic = chars?.Characteristics.FirstOrDefault();
                if (characteristic == null)
                    return null;
                var read = await Timeout(characteristic.ReadValueAsync(mode).AsTask(), 3000);
                if (read == null || read.Status != GattCommunicationStatus.Success || read.Value == null || read.Value.Length < 1)
                    return null;
                var reader = DataReader.FromBuffer(read.Value);
                byte value = reader.ReadByte();
                return value <= 100 ? value : null;
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Scan complet : chaque source est isolée, une panne n'empêche pas les autres.
    internal static async Task<List<PeripheralCandidate>> ScanAsync()
    {
        var pnp = new Dictionary<ulong, PnpInfo>();
        var classic = new List<Aep>();
        var le = new List<Aep>();
        try { pnp = await Timeout(Task.Run(ScanPnp), 8000) ?? pnp; } catch (Exception) { }
        try { classic = await PairedAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true)); } catch (Exception) { }
        try { le = await PairedAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)); } catch (Exception) { }

        var bluetooth = new List<PeripheralCandidate>();
        foreach (ulong address in classic.Select(a => a.Address).Union(le.Select(a => a.Address)))
        {
            try
            {
                var c = classic.FirstOrDefault(a => a.Address == address);
                var l = le.FirstOrDefault(a => a.Address == address);
                pnp.TryGetValue(address, out var info);
                string name = FirstText(c?.Name, l?.Name, info?.Name) ?? address.ToString("X12", CultureInfo.InvariantCulture);
                bool connected = c?.Connected == true || l?.Connected == true;
                int? percent = info?.Battery;
                ushort? appearance = null;
                if (l?.Connected == true && (percent == null || PeripheralLogic.DetectKind(info?.Cod, null, name) == "other"))
                {
                    var gatt = await ReadLeAsync(address, percent == null);
                    percent ??= gatt.percent;
                    appearance = gatt.appearance;
                }
                bluetooth.Add(new("bt:" + address.ToString("X12", CultureInfo.InvariantCulture), name, c != null ? "bluetooth" : "ble",
                    PeripheralLogic.DetectKind(info?.Cod, appearance, name), connected, connected ? percent : null));
            }
            catch (Exception)
            {
            }
        }
        var pads = new List<PeripheralCandidate>();
        try { pads = ScanXInput(); } catch (Exception) { }
        var result = PeripheralLogic.Merge(bluetooth, pads);
        try { result.AddRange(await Task.Run(() => HidHeadsets.Scan())); } catch (Exception) { }
        try
        {
            var (percent, charging) = BatteryStatus.Read();
            var system = PeripheralLogic.SystemBattery(percent, charging);
            if (system != null)
                result.Add(system);
        }
        catch (Exception) { }
        return result;
    }

    static string? FirstText(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
