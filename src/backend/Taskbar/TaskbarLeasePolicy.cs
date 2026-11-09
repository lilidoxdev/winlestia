// Logique pure du bail de barre des taches (docs/RESTORE.md) : aucun appel shell, testable sans bureau.
using System;
using System.IO;
using System.Text.Json;

namespace PanneauxGlass;

internal static class TaskbarLeasePolicy
{
    /// <summary>Etat de repli quand le bail est absent ou illisible : 2 = ABS_ALWAYSONTOP (toujours visible).</summary>
    internal const int FallbackState = 2;

    internal enum GuardAction
    {
        /// <summary>Bail illisible : le gardien de repli retourne 2 sans rien restaurer.</summary>
        NoLease,
        /// <summary>Le parent a ete relance (heure de demarrage differente) : restaurer tout de suite.</summary>
        RestoreNow,
        /// <summary>Parent vivant et identique : attendre sa fin ou l'evenement release.</summary>
        Wait,
        /// <summary>Parent introuvable : restaurer, code de sortie 1.</summary>
        RestoreAfterError
    }

    internal static TaskbarNative.Lease? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<TaskbarNative.Lease>(json, Store.Json);
        }
        catch { return null; }
    }

    internal static TaskbarNative.Lease? Read(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path));
        }
        catch { return null; }
    }

    internal static void Write(string path, TaskbarNative.Lease lease) =>
        Winlestia.Shared.AtomicFile.WriteAllText(path, Serialize(lease));

    internal static string Serialize(TaskbarNative.Lease lease) => JsonSerializer.Serialize(lease, Store.Json);

    /// <summary>Bail a appliquer a la restauration : le bail lu, sinon le repli « toujours visible ».</summary>
    internal static TaskbarNative.Lease ForRestore(TaskbarNative.Lease? lease) =>
        lease ?? new TaskbarNative.Lease { State = FallbackState };

    /// <param name="parentStartTicks">Heure de demarrage UTC (ticks) du parent, ou null s'il est introuvable.</param>
    internal static GuardAction Decide(TaskbarNative.Lease? lease, long? parentStartTicks)
    {
        if (lease == null)
            return GuardAction.NoLease;
        if (parentStartTicks == null)
            return GuardAction.RestoreAfterError;
        return parentStartTicks.Value != lease.ParentStart ? GuardAction.RestoreNow : GuardAction.Wait;
    }
}
