// language: C# 12, file: BluetoothConnector.cs, target: Windows 11 / .NET 8, connexion / déconnexion d'un périphérique Bluetooth classique
// appairé (bthprops.cpl)
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
namespace PanneauxGlass;

// Technique : BluetoothSetServiceState active/désactive les services installés du périphérique (A2DP, HFP, HSP, HID).
// Désactiver coupe la liaison ; désactiver puis réactiver force la reconnexion. Limites : Bluetooth classique seulement
// (pas de BLE), le périphérique doit être à portée et allumé.
internal static class BluetoothConnector
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct SystemTime { public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct BluetoothDeviceInfo
    {
        public uint Size;
        public ulong Address;
        public uint ClassOfDevice;
        public int Connected, Remembered, Authenticated;
        public SystemTime LastSeen, LastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string Name;
    }

    [DllImport("bthprops.cpl", SetLastError = true)]
    static extern uint BluetoothGetDeviceInfo(IntPtr radio, ref BluetoothDeviceInfo info);
    [DllImport("bthprops.cpl", SetLastError = true)]
    static extern uint BluetoothSetServiceState(IntPtr radio, ref BluetoothDeviceInfo info, ref Guid service, uint flags);

    const uint ServiceDisable = 0, ServiceEnable = 1;
    // Jamais HID : désactiver le service d'un clavier / d'une souris couperait la saisie de l'utilisateur.
    static readonly Guid[] Services =
    {
        new("0000110b-0000-1000-8000-00805f9b34fb"), // récepteur A2DP
        new("0000111e-0000-1000-8000-00805f9b34fb"), // mains libres (HFP)
        new("00001108-0000-1000-8000-00805f9b34fb"), // casque (HSP)
    };

    // Classe majeure (bits 8-12 du CoD) 5 = périphérique d'interface (clavier, souris, manette).
    internal static bool IsInputDevice(uint classOfDevice) => ((classOfDevice >> 8) & 0x1F) == 5;

    // « bt:XXXXXXXXXXXX » -> adresse ; false si l'identifiant n'a pas cette forme.
    internal static bool TryParseId(string? id, out ulong address)
    {
        address = 0;
        return id != null && id.Length == 15 && id.StartsWith("bt:", StringComparison.Ordinal)
            && ulong.TryParse(id.AsSpan(3), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out address);
    }

    static bool Query(ref BluetoothDeviceInfo info, Action<string>? log)
    {
        uint r = BluetoothGetDeviceInfo(IntPtr.Zero, ref info);
        if (r != 0)
            log?.Invoke($"BluetoothGetDeviceInfo erreur {r} (win32 {Marshal.GetLastWin32Error()})");
        return r == 0;
    }

    static bool SetState(ref BluetoothDeviceInfo info, Guid service, uint flags, Action<string>? log)
    {
        uint r = BluetoothSetServiceState(IntPtr.Zero, ref info, ref service, flags);
        log?.Invoke($"service {service.ToString()[..8]} {(flags == ServiceEnable ? "enable" : "disable")} -> {r}"
            + (r != 0 ? $" (win32 {Marshal.GetLastWin32Error()})" : ""));
        return r == 0;
    }

    // Active en réessayant ; ne laisse jamais un service désactivé sur le chemin de connexion.
    static bool EnableWithRetry(ref BluetoothDeviceInfo info, Guid service, Action<string>? log)
    {
        for (int i = 0; i < 3; i++)
        {
            if (SetState(ref info, service, ServiceEnable, log))
                return true;
            Thread.Sleep(200);
        }
        return false;
    }

    // Bloquant (quelques secondes) : à appeler hors du thread de l'interface. Rend false si rien n'a pu être fait.
    internal static bool SetConnected(ulong address, bool connect, Action<string>? log = null)
    {
        var info = new BluetoothDeviceInfo { Size = (uint)Marshal.SizeOf<BluetoothDeviceInfo>(), Address = address, Name = "" };
        if (!Query(ref info, log))
            return false;
        if (IsInputDevice(info.ClassOfDevice))
        {
            log?.Invoke("périphérique d'entrée : connexion non prise en charge");
            return false;
        }
        bool any = false;
        if (!connect)
        {
            foreach (var service in Services)
                any |= SetState(ref info, service, ServiceDisable, log);
            return any;
        }
        foreach (var service in Services)
            any |= EnableWithRetry(ref info, service, log);
        Thread.Sleep(1000);
        if (Query(ref info, log) && info.Connected != 0)
            return any;
        // Toujours déconnecté : désactiver puis réactiver force la liaison ; chaque service désactivé est réactivé.
        foreach (var service in Services)
        {
            SetState(ref info, service, ServiceDisable, log);
            Thread.Sleep(150);
            any |= EnableWithRetry(ref info, service, log);
        }
        return any;
    }
}
