// Ctrl+Z / Ctrl+Y: an internal journal of what this window did through FileOps (the shell exposes no reliable public undo API).
// Undoable: rename, move, copy (the copies go to the Recycle Bin), new folder, delete to the Recycle Bin (restored from the bin).
// NOT undoable: permanent delete (a barrier entry says so), operations run from the native shell menu, outgoing drags, and files that a
// copy replaced (undoing that copy recycles the replacement, the replaced original is not brought back).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WinlestiaFiles;

internal sealed class UndoEntry
{
    internal string Label = "";
    internal bool Irreversible;
    internal string Why = "suppression définitive";
    internal Func<Task<OpResult>> Undo = null!;
    internal Func<Task<OpResult>> Redo = null!;
}

internal sealed class UndoJournal
{
    const int Max = 50;
    readonly List<UndoEntry> undo = new(), redo = new();
    internal Func<IntPtr> Owner = () => IntPtr.Zero;
    internal Func<HashSet<string>> Watch = () => new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    internal event Action? Changed;

    internal UndoEntry? NextUndo => undo.Count > 0 ? undo[^1] : null;
    internal UndoEntry? NextRedo => redo.Count > 0 ? redo[^1] : null;
    internal int UndoCount => undo.Count;
    internal int RedoCount => redo.Count;

    static Task<OpResult> Refuse(string why) => Task.FromResult(new OpResult { Error = why });
    static bool Exists(string p) => File.Exists(p) || Directory.Exists(p);
    static DateTime Stamp(string p)
    {
        try
        {
            return Directory.Exists(p) ? Directory.GetLastWriteTimeUtc(p) : File.GetLastWriteTimeUtc(p);
        }
        catch { return DateTime.MinValue; }
    }
    // Undo/redo of a move: refuses when something already sits at a target (no conflict dialog).
    static Task<OpResult>? Occupied(IEnumerable<(string From, string To)> pairs) =>
        pairs.FirstOrDefault(p => Exists(p.To)) is { To: { } t } ? Refuse("Impossible d'annuler : un élément nommé « " + Path.GetFileName(t) + " » existe déjà à l'emplacement d'origine") : null;

    void Push(UndoEntry e)
    {
        undo.Add(e);
        if (undo.Count > Max)
            undo.RemoveAt(0);
        redo.Clear();
        Changed?.Invoke();
    }

    internal void RecordPermanentDelete() => Push(new UndoEntry { Label = "Suppression définitive", Irreversible = true });

    // Builds the entry from what the operation really did. Nothing succeeded -> nothing recorded.
    internal void Record(OpResult r)
    {
        if (r.Log.Count == 0)
            return;
        if (r.Replaced)
        {
            Push(new UndoEntry { Label = "Remplacement de fichier", Irreversible = true, Why = "remplacement de fichier" });
            return;
        }
        var kind = r.Log[0].Kind;
        var log = r.Log.Where(d => d.Kind == kind).ToList();
        string n = log.Count > 1 ? $" ({log.Count} éléments)" : "";
        IntPtr Hwnd() => Owner();
        switch (kind)
        {
            case DoneKind.Rename:
                {
                    var d = log[0];
                    string oldPath = d.Src!, newPath = d.Dst!;
                    Push(new UndoEntry
                    {
                        Label = "Renommer",
                        Undo = () => FileOps.Rename(Hwnd(), newPath, Path.GetFileName(oldPath), Watch()),
                        Redo = () => FileOps.Rename(Hwnd(), oldPath, Path.GetFileName(newPath), Watch()),
                    });
                    break;
                }
            case DoneKind.Move:
                {
                    var pairs = log.Where(d => d.Src != null && d.Dst != null).Select(d => (From: d.Src!, To: d.Dst!)).ToList();
                    var back = pairs.Select(p => (From: p.To, To: p.From)).ToList();
                    Push(new UndoEntry
                    {
                        Label = "Déplacer" + n,
                        Undo = () => Occupied(back) ?? FileOps.Transfer(Hwnd(), back, true, Watch()),
                        Redo = () => Occupied(pairs) ?? FileOps.Transfer(Hwnd(), pairs, true, Watch()),
                    });
                    break;
                }
            case DoneKind.Copy:
                {
                    var pairs = log.Where(d => d.Src != null && d.Dst != null).Select(d => (From: d.Src!, To: d.Dst!)).ToList();
                    var copies = pairs.Select(p => p.To).ToList();
                    var stamps = copies.ToDictionary(c => c, Stamp);
                    Push(new UndoEntry
                    {
                        Label = "Copier" + n,
                        Undo = () => copies.FirstOrDefault(c => Exists(c) && Stamp(c) != stamps[c]) is { } changed
                            ? Refuse("Impossible d'annuler : « " + Path.GetFileName(changed) + " » a été modifié depuis la copie")
                            : FileOps.Delete(Hwnd(), copies, false, Watch()),
                        Redo = () => Occupied(pairs.Select(p => (p.From, p.To))) ?? FileOps.Transfer(Hwnd(), pairs, false, Watch()),
                    });
                    break;
                }
            case DoneKind.New:
                {
                    string made = log[0].Dst!;
                    Push(new UndoEntry
                    {
                        Label = "Nouveau dossier",
                        Undo = () => Directory.Exists(made) && Directory.EnumerateFileSystemEntries(made).Any()
                            ? Refuse("Impossible d'annuler : le dossier contient maintenant des éléments")
                            : FileOps.Delete(Hwnd(), new[] { made }, false, Watch()),
                        Redo = async () =>
                        {
                            var res = await FileOps.NewFolder(Hwnd(), Path.GetDirectoryName(made)!, Path.GetFileName(made), Watch());
                            if (res.Log.FirstOrDefault(x => x.Kind == DoneKind.New)?.Dst is { } again)
                                made = again;
                            return res;
                        },
                    });
                    break;
                }
            case DoneKind.Delete:
                {
                    var items = log.Where(d => d.Src != null).Select(d => (Orig: d.Src!, Bin: d.Dst)).ToList();
                    Push(new UndoEntry
                    {
                        Label = "Supprimer" + n,
                        Undo = () => RestoreAsync(items, Watch()),
                        Redo = async () =>
                        {
                            var res = await FileOps.Delete(Hwnd(), items.Select(i => i.Orig).ToList(), false, Watch());
                            var again = res.Log.Where(x => x.Kind == DoneKind.Delete && x.Src != null).Select(x => (Orig: x.Src!, Bin: x.Dst)).ToList();
                            if (again.Count > 0)
                            {
                                items.Clear();
                                items.AddRange(again);
                            }
                            return res;
                        },
                    });
                    break;
                }
        }
    }

    // Returns null when there was nothing to do, else the entry that ran plus its result (Error set when it failed).
    internal async Task<(UndoEntry Entry, OpResult? Result)?> UndoAsync()
    {
        if (undo.Count == 0)
            return null;
        var e = undo[^1];
        undo.RemoveAt(undo.Count - 1);
        if (e.Irreversible)
        {
            Changed?.Invoke();
            return (e, null);
        }
        var r = await e.Undo();
        if (r.Error == null && !r.Aborted)
            redo.Add(e);
        Changed?.Invoke();
        return (e, r);
    }

    internal async Task<(UndoEntry Entry, OpResult? Result)?> RedoAsync()
    {
        if (redo.Count == 0)
            return null;
        var e = redo[^1];
        redo.RemoveAt(redo.Count - 1);
        var r = await e.Redo();
        if (r.Error == null && !r.Aborted)
            undo.Add(e);
        Changed?.Invoke();
        return (e, r);
    }

    // ---- Recycle Bin restore ------------------------------------------------------------------------------------

    // Shell.Application on the bin folder (CSIDL_BITBUCKET = 10): match by bin path, else by name + original folder, then the canonical
    // "undelete" verb (locale independent). On a failure the verb captions are tried (Restaurer / Restore).
    internal static Task<OpResult> RestoreAsync(IReadOnlyList<(string Orig, string? Bin)> items, HashSet<string> watch)
    {
        var done = new TaskCompletionSource<OpResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var result = new OpResult();
            try
            {
                var type = Type.GetTypeFromProgID("Shell.Application")!;
                dynamic shell = Activator.CreateInstance(type)!;
                foreach (var (orig, bin) in items)
                {
                    dynamic folder = shell.NameSpace(10);
                    dynamic? found = null;
                    string name = Path.GetFileName(orig), bare = Path.GetFileNameWithoutExtension(orig), from = Path.GetDirectoryName(orig)!.TrimEnd('\\');
                    foreach (dynamic it in folder.Items())
                    {
                        string p = "", nm = "", where = "";
                        try
                        {
                            p = (string)it.Path;
                            nm = (string)it.Name;
                            where = ((string)folder.GetDetailsOf(it, 1)).TrimEnd('\\');
                        }
                        catch { }
                        // GetDetailsOf may prefix invisible direction marks
                        where = where.Trim('‎', '‏', '‪', '‬');
                        bool byBin = bin != null && string.Equals(p, bin, StringComparison.OrdinalIgnoreCase);
                        bool byName = (string.Equals(nm, name, StringComparison.OrdinalIgnoreCase) || string.Equals(nm, bare, StringComparison.OrdinalIgnoreCase)) && string.Equals(where, from, StringComparison.OrdinalIgnoreCase);
                        if (byBin || byName)
                        {
                            found = it;
                            if (byBin)
                                break;
                        }
                    }
                    if (found == null)
                    {
                        result.Error = "Introuvable dans la Corbeille : " + name;
                        continue;
                    }
                    try
                    {
                        found.InvokeVerb("undelete");
                    }
                    catch { }
                    if (!WaitExists(orig))
                    {
                        try
                        {
                            foreach (dynamic verb in found.Verbs())
                            {
                                string caption = ((string)verb.Name).Replace("&", "");
                                if (caption.StartsWith("Restaur", StringComparison.OrdinalIgnoreCase) || caption.StartsWith("Restore", StringComparison.OrdinalIgnoreCase))
                                {
                                    verb.DoIt();
                                    break;
                                }
                            }
                        }
                        catch { }
                    }
                    if (WaitExists(orig))
                    {
                        result.Log.Add(new Done(DoneKind.Move, bin, orig));
                        var item = FileItem.Probe(orig, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                        if (item != null && watch.Contains(Path.GetDirectoryName(orig)?.TrimEnd('\\') ?? ""))
                            result.Added.Add(item);
                    }
                    else
                        result.Error = "Restauration impossible : " + name;
                }
            }
            catch (Exception ex) { result.Error = ex.Message; }
            done.TrySetResult(result);
        })
        {
            IsBackground = true,
            Name = "BinRestore"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    static bool WaitExists(string path)
    {
        for (int i = 0; i < 50; i++)
        {
            if (File.Exists(path) || Directory.Exists(path))
                return true;
            Thread.Sleep(100);
        }
        return false;
    }
}
