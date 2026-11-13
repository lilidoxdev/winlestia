// language: C# 12, file: BatteryStatus.cs, target: Windows 11 / .NET 8, niveau de batterie via GetSystemPowerStatus (lu sur le tick de performance)
using System;
using System.Runtime.InteropServices;
namespace PanneauxGlass;

internal static class BatteryStatus
{
    [StructLayout(LayoutKind.Sequential)]
    struct SystemPowerStatus
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    // Pure : pourcentage 0-100, ou -1 sans batterie (drapeau 128 ou pourcentage inconnu = 255).
    internal static (int percent, bool charging) Interpret(byte batteryFlag, byte percent, byte acLine = 0)
    {
        if ((batteryFlag & 128) != 0 || batteryFlag == 255 || percent > 100)
            return (-1, false);
        return (percent, (batteryFlag & 8) != 0);
    }

    static readonly object gate = new();
    static (int, bool) cached = (-1, false);
    static long readAt = long.MinValue;

    // Lecture mise en cache 5 s : le tick de performance peut être plus rapide.
    internal static (int percent, bool charging) Read()
    {
        lock (gate)
        {
            long now = Environment.TickCount64;
            if (readAt != long.MinValue && now - readAt < 5000)
                return cached;
            readAt = now;
            try
            {
                cached = GetSystemPowerStatus(out var s) ? Interpret(s.BatteryFlag, s.BatteryLifePercent, s.ACLineStatus) : (-1, false);
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
            {
                cached = (-1, false);
            }
            return cached;
        }
    }
}
