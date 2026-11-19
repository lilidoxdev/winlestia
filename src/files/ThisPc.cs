// "Ce PC" : un emplacement virtuel (aucune énumération d'éléments) dont la page liste les lecteurs avec une barre d'espace libre.
using System;
using System.Collections.Generic;
using System.IO;

namespace WinlestiaFiles;

internal static class ThisPc
{
    internal const string Path = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
    internal const string Label = "Ce PC";
    internal static bool Is(string? p) => p != null && string.Equals(p.TrimEnd('\\'), Path, StringComparison.OrdinalIgnoreCase);

    internal sealed record DriveCard(string Root, string Label, string Kind, long Free, long Total, bool Ready);

    // Thread de travail uniquement (IsReady peut bloquer sur un lecteur en veille / réseau). Sidebar.DriveSource (tests) remplace la
    // vraie liste.
    internal static List<DriveCard> Read()
    {
        var list = new List<DriveCard>();
        if (Sidebar.DriveSource != null)
        {
            foreach (var d in Sidebar.DriveSource())
                list.Add(new DriveCard(d.Root, d.Label, "Disque local", d.Free, d.Total, d.Total > 0));
            return list;
        }
        foreach (var d in DriveInfo.GetDrives())
        {
            string kind = d.DriveType switch
            {
                DriveType.Removable => "Périphérique amovible",
                DriveType.Network => "Lecteur réseau",
                DriveType.CDRom => "Lecteur optique",
                _ => "Disque local"
            };
            try
            {
                if (d.IsReady)
                    list.Add(new DriveCard(d.Name, d.VolumeLabel, kind, d.AvailableFreeSpace, d.TotalSize, true));
                else
                    list.Add(new DriveCard(d.Name, "", kind, 0, 0, false));
            }
            catch { list.Add(new DriveCard(d.Name, "", kind, 0, 0, false)); }
        }
        return list;
    }
}
