// SocialService : persistance des compteurs entre deux sessions Windows.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace PanneauxGlass;

internal sealed partial class SocialService
{
    // Regroupe les écritures disque des compteurs.
    const int SaveDebounceMs = 2000;
    // ---- Persistance des compteurs (session Windows) ----------------------------------------------------------
    string CountsFile => Path.Combine(folder, "counts.json");

    sealed class CountsFile_
    {
        public long BootTicks
        {
            get; set;
        }
        public Dictionary<string, int> Counts { get; set; } = new(); public List<string>? TitleNets
        {
            get; set;
        }
        public List<HistoryItem> History { get; set; } = new(); public int NextSeq
        {
            get; set;
        }
    }

    CountsFile_ MakeFile() => new() { NextSeq = nextSeq, BootTicks = bootUtc.Ticks, Counts = nets.ToDictionary(n => n.Id, n => n.Count),
        TitleNets = nets.Where(n => n.U.TitleSeen).Select(n => n.Id).ToList(), History = history.Select(h => new HistoryItem { Seq = h.Seq,
        Net = h.Net, Title = h.Title, Body = h.Body, Kind = h.Kind, At = h.At }).ToList() };

    void LoadCounts()
    {
        try
        {
            if (!File.Exists(CountsFile))
                return;
            var data = JsonSerializer.Deserialize<CountsFile_>(File.ReadAllText(CountsFile));
            if (data == null)
                return;
            // Même session Windows = même heure de démarrage, à quelques secondes d'écart près.
            if (Math.Abs((new DateTime(data.BootTicks, DateTimeKind.Utc) - bootUtc).TotalSeconds) > 90)
                return;
            // Migration : ancien fichier (sans TitleNets) = compteurs cumulés non fiables, plafonnés à 99 ; l'absorption/le premier
            // titre les remplace.
            foreach (var n in nets)
                if (data.Counts.TryGetValue(n.Id, out int c))
                {
                    n.Count = SocialRules.RestoreCount(c, data.TitleNets == null || data.TitleNets.Contains(n.Id));
                    if (data.TitleNets == null)
                        n.U.Legacy = true;
                    else if (data.TitleNets.Contains(n.Id))
                        n.U.TitleSeen = true;
                }
            if (data.History != null)
            {
                history.AddRange(SocialRules.RestoreHistory(data.History, id => nets.Any(x => x.Id == id)));
                nextSeq = SocialRules.NextSeqAfter(data.NextSeq, history.Select(h => h.Seq));
            }
            Store.Log("Social historique chargé : " + history.Count + " entrée(s), prochain seq " + nextSeq + (data.History == null
                ? " (fichier sans historique)" : ""));
        }
        catch (Exception ex) { Store.Log("Social compteurs: " + ex.Message); }
    }

    // Écriture différée (~2 s) hors thread UI.
    void QueueSave()
    {
        if (saveQueued || disposed)
            return;
        saveQueued = true;
        _ = Task.Run(async () => { try { await Task.Delay(SaveDebounceMs); if (disposed) return; dispatcher.Invoke(()
            => saveQueued = false); WriteNow(); } catch (Exception err) { Store.Log("SocialService.QueueSave : " + err.GetType().Name
                + ": "
            + err.Message); } });
    }

    void WriteNow()
    {
        CountsFile_? snapshot = null;
        dispatcher.Invoke(() => { if (countsDirty) { snapshot = MakeFile(); countsDirty = false; } });
        lock (saveLock)
        {
            if (disposed)
                return; // Dispose écrit l'état final sous ce verrou : une écriture différée plus ancienne ne doit pas l'écraser
            try
            {
                if (snapshot != null)
                {
                    Directory.CreateDirectory(folder);
                    Winlestia.Shared.AtomicFile.WriteAllText(CountsFile, JsonSerializer.Serialize(snapshot));
                }
            }
            catch (Exception ex) { Store.Log("Social écriture: " + ex.Message); }
        }
    }
}
