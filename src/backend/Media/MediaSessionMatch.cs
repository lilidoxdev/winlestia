// language: C# 12, file: MediaSessionMatch.cs, target: Windows 11 / .NET 8
// Pure matching between WASAPI audio sessions and the media source selected in the dashboard.
// Per-tab volume is impossible: Windows audio sessions are per process (a browser tab is not a session), so the
// volume and the visualizer always act on every audio session of the selected browser or application.
using System;
using System.Collections.Generic;
using System.Linq;

namespace PanneauxGlass.Media;

/// <summary>An audio session as seen by the mixer. ExeStem may be empty when the image path is unreadable.</summary>
internal sealed record AudioSessionInfo(
    int Pid,
    string ExeStem,
    IReadOnlyList<string> AncestorExeStems,
    string SessionIdentifier
);

/// <summary>The resolved media source: executable stem (may be empty) and the raw AUMID.</summary>
internal sealed record MediaTarget(string ExeStem, string Aumid);

internal static class MediaSessionMatch
{
    const int MaxAncestorDepth = 12;

    // Generic shell/host processes parent many unrelated apps: they never identify a media source via ancestors.
    static readonly HashSet<string> GenericAncestorStems = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer",
        "cmd",
        "powershell",
        "pwsh",
        "conhost",
        "svchost",
        "services",
        "wininit",
        "winlogon",
        "sihost",
        "runtimebroker",
        "dllhost",
        "applicationframehost",
        "windowsterminal",
        "wt",
        "taskhostw",
        "userinit",
    };

    internal static bool Matches(AudioSessionInfo session, MediaTarget target)
    {
        if (target.ExeStem.Length > 0)
        {
            if (session.ExeStem.Equals(target.ExeStem, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Firefox and Chromium play audio from a child process: walk the parent chain.
            bool ancestorMatches =
                !GenericAncestorStems.Contains(target.ExeStem)
                && session.AncestorExeStems.Any(ancestor =>
                    ancestor.Equals(target.ExeStem, StringComparison.OrdinalIgnoreCase)
                );
            if (ancestorMatches)
            {
                return true;
            }

            if (session.SessionIdentifier.Contains(target.ExeStem + ".exe", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        string family = PackageFamily(target.Aumid);
        return family.Length >= 6 && session.SessionIdentifier.Contains(family, StringComparison.OrdinalIgnoreCase);
    }

    // Hash-style AUMIDs never appear in session identifiers: only package families are useful here.
    static string PackageFamily(string aumid)
    {
        bool unusable =
            aumid.Length == 0
            || MediaSourceIdentity.IsHashStyle(aumid)
            || aumid.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        return unusable ? "" : aumid.Split('!')[0];
    }

    /// <summary>Executable stems of the parent chain of a process, nearest parent first.</summary>
    internal static IReadOnlyList<string> AncestorStems(int pid, Func<int, int> parentOf, Func<int, string> stemOf)
    {
        var stems = new List<string>();
        var seen = new HashSet<int> { pid };
        int current = parentOf(pid);
        while (current > 0 && stems.Count < MaxAncestorDepth && seen.Add(current))
        {
            stems.Add(stemOf(current));
            current = parentOf(current);
        }

        return stems;
    }

    /// <summary>
    /// Root of the matched process tree: climbs while the parent has the same executable stem as the target.
    /// Capturing this PID with INCLUDE_TARGET_PROCESS_TREE covers all the browser child processes.
    /// </summary>
    internal static int RootPid(int pid, string exeStem, Func<int, int> parentOf, Func<int, string> stemOf)
    {
        if (exeStem.Length == 0)
        {
            return pid;
        }

        int root = pid;
        var seen = new HashSet<int> { pid };
        for (int depth = 0; depth < MaxAncestorDepth; depth++)
        {
            int parent = parentOf(root);
            bool sameApp = parent > 0 && stemOf(parent).Equals(exeStem, StringComparison.OrdinalIgnoreCase);
            if (!sameApp || !seen.Add(parent))
            {
                break;
            }

            root = parent;
        }

        return root;
    }
}
