// language: C# 12, file: ProcessSnapshot.cs, target: Windows 11 / .NET 8
// Toolhelp32 snapshot of parent PIDs and executable names. Works even when Process.MainModule is not readable,
// and also hosts the registry lookups used to resolve media sources.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PanneauxGlass.Platform;

internal sealed class ProcessSnapshot
{
    const uint SnapshotProcess = 0x2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    readonly Dictionary<int, (int Parent, string Stem)> entries = new();

    internal static ProcessSnapshot Capture()
    {
        var result = new ProcessSnapshot();
        IntPtr snapshot = CreateToolhelp32Snapshot(SnapshotProcess, 0);
        if (snapshot == IntPtr.Zero || snapshot == new IntPtr(-1))
        {
            return result;
        }

        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            bool more = Process32FirstW(snapshot, ref entry);
            while (more)
            {
                string stem = Path.GetFileNameWithoutExtension(entry.ExeFile);
                result.entries[(int)entry.ProcessId] = ((int)entry.ParentProcessId, stem);
                more = Process32NextW(snapshot, ref entry);
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return result;
    }

    internal int ParentOf(int pid)
    {
        return entries.TryGetValue(pid, out var entry) ? entry.Parent : 0;
    }

    internal string StemOf(int pid)
    {
        return entries.TryGetValue(pid, out var entry) ? entry.Stem : "";
    }

    internal bool IsRunning(string stem)
    {
        foreach (var entry in entries.Values)
        {
            if (entry.Stem.Equals(stem, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>DisplayName registered for an AUMID under HKCU then HKLM, or null.</summary>
    internal static string? RegistryDisplayName(string aumid)
    {
        string keyPath = @"Software\Classes\AppUserModelId\" + aumid;
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using var key = hive.OpenSubKey(keyPath);
                if (key?.GetValue("DisplayName") is string name && name.Length > 0 && !name.StartsWith('@'))
                {
                    return name;
                }
            }
            catch (Exception ex)
                when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                // An unreadable key is the same as a missing one.
            }
        }

        return null;
    }

    /// <summary>Resolver wired to the real registry and process list.</summary>
    internal static MediaSourceIdentity CreateIdentity()
    {
        var processes = Capture();
        return new MediaSourceIdentity(RegistryDisplayName, processes.IsRunning, RegistryDisplayName);
    }
}
