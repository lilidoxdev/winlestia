// Opérations de fichiers via IFileOperation : un thread STA dédié par opération, la fenêtre principale comme propriétaire (le shell
// dessine ses propres dialogues de progression / conflit / accès refusé), un récepteur de progression qui ne collecte que les
// résultats RÉUSSIS pour que la
// liste
// puisse être mise à jour sur place. Chaque objet COM est libéré (FinalReleaseComObject) sur le thread qui l'a créé.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace WinlestiaFiles;

internal enum DoneKind
{
    Rename, Move, Copy, Delete, New
}
// Une étape réussie, quel que soit l'ensemble surveillé : Src (null pour New), Dst (nouveau chemin ; pour Delete le chemin de la
// Corbeille quand le shell l'a fourni).
internal sealed record Done(DoneKind Kind, string? Src, string? Dst);

internal sealed class OpResult
{
    internal readonly List<Done> Log = new();
    internal readonly List<string> Removed = new();      // chemins qui ont quitté leur dossier (déplacés, renommés depuis, supprimés)
    // éléments apparus (copiés, déplacés vers, renommés en, créés), déjà interrogés par stat
    internal readonly List<FileItem> Added = new();
    internal bool Aborted;
    // une destination existait avant et a été écrasée : l'original ne peut pas être restauré par une annulation
    internal bool Replaced;
    internal string? Error;
}

internal static class FileOps
{
    // Test hook: no native progress / confirmation / error UI.
    internal static bool Quiet;
    internal static string OddHr = "";      // diagnostic : HRESULT non nuls vus par le collecteur

    // Opérations en cours. Quand la dernière se termine, Idle est levé sur le thread de travail (l'instance résidente y revérifie son
    // recyclage).
    static int active;
    internal static int Active => Volatile.Read(ref active);
    internal static event Action? Idle;

    const uint FOF_SILENT = 0x4, FOF_RENAMEONCOLLISION = 0x8, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOCONFIRMMKDIR = 0x200,
        FOF_NOERRORUI = 0x400,
        FOFX_RECYCLEONDELETE = 0x00080000, FOFX_ADDUNDORECORD = 0x20000000;
    const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    static readonly Guid ClsidFileOperation = new("3ad05575-8857-4850-9277-11b85bdb8e09");
    static Guid iidShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    internal static Task<OpResult> Copy(IntPtr owner, IReadOnlyList<string> sources, string destFolder, bool move, HashSet<string> watch)
    {
        // Même dossier : « nom - Copie » de l'Explorateur au lieu d'une invite de conflit. Un déplacement sur lui-même est sans effet
        // et filtré.
        bool sameFolder = false;
        var list = new List<string>();
        foreach (var s in sources)
        {
            bool same = string.Equals(Path.GetDirectoryName(s)?.TrimEnd('\\'), destFolder.TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase);
            if (same && move)
                continue;
            sameFolder |= same;
            list.Add(s);
        }
        if (list.Count == 0)
            return Task.FromResult(new OpResult());
        uint flags = FOF_ALLOWUNDO | FOFX_ADDUNDORECORD | FOF_NOCONFIRMMKDIR | (sameFolder ? FOF_RENAMEONCOLLISION : 0);
        return Run(owner, flags, watch, list.Select(p => Path.Combine(destFolder, Path.GetFileName(p))), (op, held) =>
        {
            var dest = Item(destFolder, held);
            foreach (var path in list)
            {
                var item = Item(path, held);
                if (move)
                    op.MoveItem(item, dest, null, null);
                else
                    op.CopyItem(item, dest, null, null);
            }
        });
    }

    // Déplacements / copies explicites (chemin source -> chemin destination) : l'annulation d'un déplacement, le rétablissement d'une
    // copie. La destination garde le nom donné.
    internal static Task<OpResult> Transfer(IntPtr owner, IReadOnlyList<(string From, string To)> pairs, bool move, HashSet<string> watch)
    {
        if (pairs.Count == 0)
            return Task.FromResult(new OpResult());
        return Run(owner, FOF_ALLOWUNDO | FOFX_ADDUNDORECORD | FOF_NOCONFIRMMKDIR, watch, pairs.Select(p => p.To), (op, held) =>
        {
            foreach (var (from, to) in pairs)
            {
                var dest = Item(Path.GetDirectoryName(to)!, held);
                var item = Item(from, held);
                string name = Path.GetFileName(to);
                if (move)
                    op.MoveItem(item, dest, name, null);
                else
                    op.CopyItem(item, dest, name, null);
            }
        });
    }

    // Corbeille sauf suppression définitive (l'appelant a déjà demandé confirmation dans ce cas).
    internal static Task<OpResult> Delete(IntPtr owner, IReadOnlyList<string> paths, bool permanent, HashSet<string> watch)
    {
        uint flags = permanent ? FOF_NOCONFIRMATION : FOF_ALLOWUNDO | FOFX_ADDUNDORECORD | FOFX_RECYCLEONDELETE;
        return Run(owner, flags, watch, (op, held) => { foreach (var path in paths) op.DeleteItem(Item(path, held), null); });
    }

    internal static Task<OpResult> Rename(IntPtr owner, string path, string newName, HashSet<string> watch) =>
        Run(owner, FOF_ALLOWUNDO | FOFX_ADDUNDORECORD, watch, (op, held) => op.RenameItem(Item(path, held), newName, null));

    // Nom automatiquement unique ("Nouveau dossier", "Nouveau dossier (2)", ...) choisi sur le thread de travail.
    internal static Task<OpResult> NewFolder(IntPtr owner, string parent, string baseName, HashSet<string> watch) =>
        Run(owner, FOF_ALLOWUNDO | FOFX_ADDUNDORECORD | FOF_NOCONFIRMMKDIR, watch, (op, held) =>
        {
            string name = baseName;
            for (int n = 2; Directory.Exists(Path.Combine(parent, name)) || File.Exists(Path.Combine(parent, name)); n++)
                name = $"{baseName} ({n})";
            op.NewItem(Item(parent, held), FILE_ATTRIBUTE_DIRECTORY, name, null, null);
        });

    static IShellItem Item(string path, List<object> held)
    {
        SHCreateItemFromParsingName(path, IntPtr.Zero, ref iidShellItem, out var item);
        held.Add(item);
        return item;
    }

    static Task<OpResult> Run(IntPtr owner, uint flags, HashSet<string> watch, Action<IFileOperation, List<object>> queue) => Run(owner,
        flags, watch, null, queue);

    // targets : où les copies / déplacements doivent atterrir. Ceux qui existent déjà au début de l'opération sont mémorisés ;
    // l'un
    // d'eux
    // apparaissant dans le journal signifie qu'il a été écrasé.
    static Task<OpResult> Run(IntPtr owner, uint flags, HashSet<string> watch, IEnumerable<string>? targets, Action<IFileOperation,
        List<object>> queue)
    {
        var done = new TaskCompletionSource<OpResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Increment(ref active);
        if (Quiet)
            flags |= FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI;
        var thread = new Thread(() =>
        {
            var result = new OpResult();
            var held = new List<object>();
            var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (targets != null)
                foreach (var t in targets)
                {
                    try
                    {
                        if (File.Exists(t) || Directory.Exists(t))
                            existing.Add(t);
                    }
                    catch (Exception)
                    {
                        // Chemin inaccessible : traité comme inexistant.
                    }
                }
            IFileOperation? op = null;
            Sink? sink = null;
            uint cookie = 0;
            bool advised = false;
            try
            {
                op = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidFileOperation)!)!;
                op.SetOperationFlags(flags);
                if (owner != IntPtr.Zero)
                    op.SetOwnerWindow(owner);
                sink = new Sink();
                op.Advise(sink, out cookie);
                advised = true;
                queue(op, held);
                op.PerformOperations();
                op.GetAnyOperationsAborted(out result.Aborted);
            }
            catch (Exception ex)
            {
                // l'annulation par l'utilisateur et l'accès refusé remontent aussi ici ; ce que le collecteur a déjà reçu reste
                // valable
                result.Error = ex.Message;
            }
            finally
            {
                Safe.Try(() =>
                {
                    if (advised)
                        op!.Unadvise(cookie);

                });
                foreach (var o in held)
                {
                    Safe.Try(() => Marshal.FinalReleaseComObject(o));
                }
                if (op != null)
                {
                    Safe.Try(() => Marshal.FinalReleaseComObject(op));
                }
            }
            // Hors de l'appel COM : lit les nouveaux éléments (find-data seulement) pour les dossiers que quelqu'un regarde.
            var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (sink != null)
            {
                result.Log.AddRange(sink.Log);
                if (existing.Count > 0)
                    result.Replaced = sink.Log.Any(d => d.Kind is DoneKind.Copy or DoneKind.Move && d.Dst != null
                        && existing.Contains(d.Dst));
                foreach (var r in sink.Results)
                {
                    if (r.Hr < 0)
                        continue;
                    if (r.Removed != null)
                        result.Removed.Add(r.Removed);
                    if (r.Added != null && watch.Contains(Path.GetDirectoryName(r.Added)?.TrimEnd('\\') ?? ""))
                    {
                        var item = FileItem.Probe(r.Added, types);
                        if (item != null)
                            result.Added.Add(item);
                    }
                }
            }
            done.TrySetResult(result);
            if (Interlocked.Decrement(ref active) == 0)
            {
                try
                {
                    Idle?.Invoke();
                }
                catch (Exception err)
                {
                    App.LogLine("FileOps.Run : " + err.GetType().Name + ": " + err.Message);
                }
            }
        })
        {
            IsBackground = true,
            Name = "FileOperation"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    // ---- sink -------------------------------------------------------------------------------------------------

    sealed record Entry(int Hr, string? Removed, string? Added);

    [ComVisible(true)]
    sealed class Sink : IFileOperationProgressSink
    {
        internal readonly List<Entry> Results = new();
        internal readonly List<Done> Log = new();

        // Extrait le chemin d'un élément du shell et libère l'enveloppe (le callback ne possède rien d'autre).
        static string? PathOf(IShellItem? item)
        {
            if (item == null)
                return null;
            try
            {
                item.GetDisplayName(0x80058000, out var p);          // SIGDN_FILESYSPATH
                if (p == IntPtr.Zero)
                    return null;
                string s = Marshal.PtrToStringUni(p)!;
                Marshal.FreeCoTaskMem(p);
                return s;
            }
            catch { return null; }
            finally { Safe.Try(() => Marshal.FinalReleaseComObject(item)); }
        }
        // Un HRESULT positif n'est pas toujours un succès : COPYENGINE_S_PENDING (0x27000B, observé après une invite « fichier en
        // cours d'utilisation » annulée : l'élément est intact), S_USER_IGNORED (0x270005), S_ALREADY_DONE (0x27000A).
        // S_DONT_PROCESS_CHILDREN (0x270008, dossiers) et les autres codes S_ sont de vrais succès.
        static bool Succeeded(int hr) => hr >= 0 && hr != 0x0027000B && hr != 0x00270005 && hr != 0x0027000A;
        static string? Combine(string? folder, string? name) => folder != null && name != null ? Path.Combine(folder, name) : null;

        public void StartOperations()
        {
        }
        public void FinishOperations(int hr)
        {
        }
        public void PreRenameItem(uint f, IShellItem item, string newName)
        {
        }
        public void PostRenameItem(uint f, IShellItem item, string newName, int hr, IShellItem? created)
        {
            string? old = PathOf(item), now = PathOf(created);
            if (hr >= 0 && now != null)
            {
                Results.Add(new Entry(hr, old, now));
                Log.Add(new Done(DoneKind.Rename, old, now));
            }
        }
        public void PreMoveItem(uint f, IShellItem item, IShellItem dest, string newName)
        {
        }
        public void PostMoveItem(uint f, IShellItem item, IShellItem dest, string newName, int hr, IShellItem? created)
        {
            string? old = PathOf(item), now = PathOf(created);
            PathOf(dest);
            if (hr != 0)
            {
                Perf.Log($"fileop: move hr=0x{hr:X8} for {old}");
                OddHr += $"move 0x{hr:X8}; ";
            }
            if (Succeeded(hr) && now != null)
            {
                Results.Add(new Entry(hr, old, now));
                Log.Add(new Done(DoneKind.Move, old, now));
            }
        }
        public void PreCopyItem(uint f, IShellItem item, IShellItem dest, string newName)
        {
        }
        public void PostCopyItem(uint f, IShellItem item, IShellItem dest, string newName, int hr, IShellItem? created)
        {
            string? src = PathOf(item);
            PathOf(dest);
            string? now = PathOf(created);
            if (hr != 0)
            {
                Perf.Log($"fileop: copy hr=0x{hr:X8} for {src}");
                OddHr += $"copy 0x{hr:X8}; ";
            }
            if (Succeeded(hr) && now != null)
            {
                Results.Add(new Entry(hr, null, now));
                Log.Add(new Done(DoneKind.Copy, src, now));
            }
        }
        public void PreDeleteItem(uint f, IShellItem item)
        {
        }
        public void PostDeleteItem(uint f, IShellItem item, int hr, IShellItem? created)
        {
            string? old = PathOf(item), bin = PathOf(created);
            if (hr != 0)
            {
                Perf.Log($"fileop: delete hr=0x{hr:X8} for {old}");
                OddHr += $"delete 0x{hr:X8}; ";
            }
            if (Succeeded(hr) && old != null)
            {
                Results.Add(new Entry(hr, old, null));
                Log.Add(new Done(DoneKind.Delete, old, bin));
            }
        }
        public void PreNewItem(uint f, IShellItem dest, string newName)
        {
        }
        public void PostNewItem(uint f, IShellItem dest, string newName, string? template, uint attrs, int hr, IShellItem? created)
        {
            PathOf(dest);
            string? now = PathOf(created);
            if (hr >= 0 && now != null)
            {
                Results.Add(new Entry(hr, null, now));
                Log.Add(new Done(DoneKind.New, null, now));
            }
        }
        public void UpdateProgress(uint total, uint sofar)
        {
        }
        public void ResetTimer()
        {
        }
        public void PauseTimer()
        {
        }
        public void ResumeTimer()
        {
        }
    }

    // ---- interop ----------------------------------------------------------------------------------------------

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem item);
        void GetDisplayName(uint sigdn, out IntPtr name);
        void GetAttributes(uint mask, out uint attrs);
        void Compare(IShellItem other, uint hint, out int order);
    }

    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileOperation
    {
        void Advise(IFileOperationProgressSink sink, out uint cookie);
        void Unadvise(uint cookie);
        void SetOperationFlags(uint flags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        void SetProgressDialog([MarshalAs(UnmanagedType.Interface)] object dialog);
        void SetProperties([MarshalAs(UnmanagedType.Interface)] object properties);
        void SetOwnerWindow(IntPtr owner);
        void ApplyPropertiesToItem(IShellItem item);
        void ApplyPropertiesToItems([MarshalAs(UnmanagedType.Interface)] object items);
        void RenameItem(IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName, IFileOperationProgressSink? sink);
        void RenameItems([MarshalAs(UnmanagedType.Interface)] object items, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        void MoveItem(IShellItem item, IShellItem dest, [MarshalAs(UnmanagedType.LPWStr)] string? newName,
            IFileOperationProgressSink? sink);
        void MoveItems([MarshalAs(UnmanagedType.Interface)] object items, IShellItem dest);
        void CopyItem(IShellItem item, IShellItem dest, [MarshalAs(UnmanagedType.LPWStr)] string? newName,
            IFileOperationProgressSink? sink);
        void CopyItems([MarshalAs(UnmanagedType.Interface)] object items, IShellItem dest);
        void DeleteItem(IShellItem item, IFileOperationProgressSink? sink);
        void DeleteItems([MarshalAs(UnmanagedType.Interface)] object items);
        void NewItem(IShellItem dest, uint attrs, [MarshalAs(UnmanagedType.LPWStr)] string name,
            [MarshalAs(UnmanagedType.LPWStr)] string? template, IFileOperationProgressSink? sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }

    [ComImport, Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileOperationProgressSink
    {
        void StartOperations();
        void FinishOperations(int hr);
        void PreRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        void PostRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName, int hr, IShellItem? created);
        void PreMoveItem(uint flags, IShellItem item, IShellItem dest, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        void PostMoveItem(uint flags, IShellItem item, IShellItem dest, [MarshalAs(UnmanagedType.LPWStr)] string newName, int hr,
            IShellItem? created);
        void PreCopyItem(uint flags, IShellItem item, IShellItem dest, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        void PostCopyItem(uint flags, IShellItem item, IShellItem dest, [MarshalAs(UnmanagedType.LPWStr)] string newName, int hr,
            IShellItem? created);
        void PreDeleteItem(uint flags, IShellItem item);
        void PostDeleteItem(uint flags, IShellItem item, int hr, IShellItem? created);
        void PreNewItem(uint flags, IShellItem dest, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        void PostNewItem(uint flags, IShellItem dest, [MarshalAs(UnmanagedType.LPWStr)] string newName,
            [MarshalAs(UnmanagedType.LPWStr)] string? template, uint attrs, int hr, IShellItem? created);
        void UpdateProgress(uint total, uint sofar);
        void ResetTimer();
        void PauseTimer();
        void ResumeTimer();
    }
}
