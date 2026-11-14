// C# 12 / ProcessUsageSampler.cs / Windows 11. CPU et mémoire par exécutable, à la demande : aucun minuteur, aucun coût au repos.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
namespace PanneauxGlass;

internal sealed class ProcessUsageSampler
{
    // Relevé brut d'un processus : temps processeur cumulé en intervalles de 100 ns, mémoire en octets.
    internal readonly record struct Raw(int Pid, long StartTime, string Path, long CpuTicks, long MemoryBytes);
    // Total par exécutable. Cpu est un pourcentage de la machine entière (0-100).
    internal readonly record struct Usage(string Path, double Cpu, long MemoryBytes, int Count);

    readonly Dictionary<int, (long Start, long Ticks)> previous = new();
    long lastStamp;
    static readonly int SessionId = Process.GetCurrentProcess().SessionId;
    internal const double MaxGapSeconds = 5;

    // Au-delà de ce délai, le relevé précédent est périmé : le prochain relevé compte comme un premier (CPU 0).
    internal static bool IsStale(double elapsedSeconds) => elapsedSeconds > MaxGapSeconds;

    // Pourcentage de la machine entière : temps processeur consommé / (temps écoulé × nombre de cœurs).
    internal static double CpuPercent(long previousTicks, long currentTicks, double elapsedSeconds, int cores)
    {
        if (elapsedSeconds <= 0 || cores <= 0 || currentTicks < previousTicks)
            return 0;
        double percent = (currentTicks - previousTicks) / 1e7 / (elapsedSeconds * cores) * 100;
        return Math.Clamp(percent, 0, 100);
    }

    // Calcul pur : applique le relevé précédent (avec garde contre la réutilisation d'un PID grâce à l'heure de création),
    // regroupe par chemin (insensible à la casse) et renvoie le nouvel état. Un premier relevé donne 0 %.
    internal static List<Usage> Aggregate(IEnumerable<Raw> raws, Dictionary<int, (long Start, long Ticks)> previous, double elapsedSeconds,
        int cores, out Dictionary<int, (long Start, long Ticks)> next)
    {
        next = new();
        var totals = new Dictionary<string, (double Cpu, long Memory, int Count)>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in raws)
        {
            next[raw.Pid] = (raw.StartTime, raw.CpuTicks);
            double cpu = previous.TryGetValue(raw.Pid, out var old) && old.Start == raw.StartTime
                ? CpuPercent(old.Ticks, raw.CpuTicks, elapsedSeconds, cores) : 0;
            var sum = totals.GetValueOrDefault(raw.Path);
            totals[raw.Path] = (sum.Cpu + cpu, sum.Memory + raw.MemoryBytes, sum.Count + 1);
        }
        return totals.Select(t => new Usage(t.Key, Math.Min(100, t.Value.Cpu), t.Value.Memory, t.Value.Count)).ToList();
    }

    // Le PID racine et tous ses descendants (ex. le lanceur de venv et le vrai interpréteur Python qu'il démarre).
    internal static HashSet<int> Tree(IEnumerable<int> roots, IEnumerable<(int Pid, int Parent)> processes)
    {
        var all = processes.ToArray();
        var result = new HashSet<int>(roots.Where(r => r > 0));
        for (bool grew = true; grew;)
        {
            grew = false;
            foreach (var (pid, parent) in all)
                if (result.Contains(parent) && result.Add(pid))
                    grew = true;
        }
        return result;
    }

    // À appeler depuis un thread de travail. Les PID de `excludedRoots` et leurs descendants sont ignorés.
    internal List<Usage> Sample(IEnumerable<int> excludedRoots)
    {
        var snapshot = Snapshot();
        var excluded = Tree(excludedRoots, snapshot);
        int session = SessionId;
        long now = Stopwatch.GetTimestamp();
        double elapsed = lastStamp == 0 ? 0 : (now - lastStamp) / (double)Stopwatch.Frequency;
        if (IsStale(elapsed))
        {
            // Fenêtre restée fermée : l'ancien relevé est périmé, on repart d'une base neuve (CPU 0).
            previous.Clear();
            elapsed = 0;
        }
        var raws = new List<Raw>();
        foreach (var (pid, _) in snapshot)
        {
            if (excluded.Contains(pid) || pid == 0 || !ProcessIdToSessionId((uint)pid, out uint s) || s != session)
                continue;
            IntPtr handle = OpenProcess(0x1000, false, (uint)pid);
            if (handle == IntPtr.Zero)
                continue;
            try
            {
                var buffer = new StringBuilder(1024);
                uint size = (uint)buffer.Capacity;
                if (!QueryFullProcessImageName(handle, 0, buffer, ref size) || !GetProcessTimes(handle, out long created, out _, out long kernel,
                    out long user))
                    continue;
                long memory = 0;
                var counters = new CountersEx { Size = (uint)Marshal.SizeOf<CountersEx>() };
                if (K32GetProcessMemoryInfo(handle, ref counters, counters.Size))
                    memory = (long)counters.PrivateUsage;
                else
                {
                    var basic = new Counters { Size = (uint)Marshal.SizeOf<Counters>() };
                    if (K32GetProcessMemoryInfo(handle, ref basic, basic.Size))
                        memory = (long)basic.WorkingSetSize;
                }
                raws.Add(new Raw(pid, created, buffer.ToString(0, (int)size), kernel + user, memory));
            }
            catch (Exception)
            {
                // Processus terminé entre-temps : ignoré.
            }
            finally { CloseHandle(handle); }
        }
        var usages = Aggregate(raws, previous, elapsed, Environment.ProcessorCount, out var next);
        previous.Clear();
        foreach (var item in next)
            previous[item.Key] = item.Value;
        lastStamp = now;
        return usages;
    }

    // Tous les PID du protégé (racine + descendants), pour l'arrêt.
    // Y compris la chaîne des ancêtres (terminal, lanceur) : les tuer arrêterait aussi le shell lancé depuis eux.
    internal static HashSet<int> ProtectedPids(IEnumerable<int> roots)
    {
        var snapshot = Snapshot();
        var rootList = roots.ToArray();
        var result = Tree(rootList, snapshot);
        result.UnionWith(Ancestors(rootList, snapshot, StartTimeOf));
        return result;
    }

    // Remonte les liens parent depuis chaque racine. S'arrête sur un cycle, une profondeur excessive, un parent absent ou un parent
    // créé après son enfant (PID réutilisé). startTimeOf renvoie 0 si l'heure de création est inconnue.
    internal static HashSet<int> Ancestors(IEnumerable<int> roots, IEnumerable<(int Pid, int Parent)> processes, Func<int, long> startTimeOf)
    {
        var parents = new Dictionary<int, int>();
        foreach (var (pid, parent) in processes)
            parents[pid] = parent;
        var result = new HashSet<int>();
        foreach (var root in roots.Where(r => r > 0))
        {
            int child = root;
            for (int depth = 0; depth < 64 && parents.TryGetValue(child, out int parent) && parent > 0 && parent != child; depth++)
            {
                long childStart = startTimeOf(child), parentStart = startTimeOf(parent);
                if (childStart != 0 && parentStart != 0 && parentStart > childStart)
                    break;
                if (!result.Add(parent))
                    break;
                child = parent;
            }
        }
        return result;
    }

    static long StartTimeOf(int pid)
    {
        IntPtr handle = OpenProcess(0x1000, false, (uint)pid);
        if (handle == IntPtr.Zero)
            return 0;
        try { return GetProcessTimes(handle, out long created, out _, out _, out _) ? created : 0; }
        finally { CloseHandle(handle); }
    }

    static (int Pid, int Parent)[] Snapshot()
    {
        var list = new List<(int, int)>();
        IntPtr snap = CreateToolhelp32Snapshot(0x2, 0);
        if (snap == new IntPtr(-1))
            return Array.Empty<(int, int)>();
        try
        {
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
            for (bool ok = Process32FirstW(snap, ref entry); ok; ok = Process32NextW(snap, ref entry))
                list.Add(((int)entry.Pid, (int)entry.Parent));
        }
        finally { CloseHandle(snap); }
        return list.ToArray();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcessEntry
    {
        public uint Size, Usage, Pid;
        public UIntPtr Heap;
        public uint Module, Threads, Parent;
        public int Priority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string ExeFile;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct Counters
    {
        public uint Size, PageFaultCount;
        public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage, QuotaPeakNonPagedPoolUsage,
            QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage;
    }
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder buffer, ref uint size);
    [DllImport("kernel32.dll")]
    static extern bool GetProcessTimes(IntPtr process, out long created, out long exited, out long kernel, out long user);
    // PROCESS_MEMORY_COUNTERS_EX : 80 octets en x64.
    [StructLayout(LayoutKind.Sequential)]
    struct CountersEx
    {
        public uint Size, PageFaultCount;
        public UIntPtr PeakWorkingSetSize, WorkingSetSize, QuotaPeakPagedPoolUsage, QuotaPagedPoolUsage, QuotaPeakNonPagedPoolUsage,
            QuotaNonPagedPoolUsage, PagefileUsage, PeakPagefileUsage, PrivateUsage;
    }
    [DllImport("kernel32.dll")] static extern bool K32GetProcessMemoryInfo(IntPtr process, ref Counters counters, uint size);
    [DllImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo")]
    static extern bool K32GetProcessMemoryInfo(IntPtr process, ref CountersEx counters, uint size);
    [DllImport("kernel32.dll")] static extern bool ProcessIdToSessionId(uint pid, out uint session);
    [DllImport("kernel32.dll")] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);
}

// Exécutables que l'arrêt refuse toujours, même hors du dossier Windows.
internal static class StopPolicy
{
    static readonly HashSet<string> Denied = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "csrss.exe", "lsass.exe", "winlogon.exe", "dwm.exe", "services.exe", "smss.exe", "wininit.exe", "MsMpEng.exe",
        "SecurityHealthService.exe", "audiodg.exe", "fontdrvhost.exe", "sihost.exe", "ctfmon.exe", "ShellExperienceHost.exe",
        "StartMenuExperienceHost.exe", "SearchHost.exe", "RuntimeBroker.exe", "Panneaux.Glass.exe", "Winlestia.exe"
    };
    internal static bool IsDenied(string path) => string.IsNullOrWhiteSpace(path) || Denied.Contains(System.IO.Path.GetFileName(path));
}
