// File operations through IFileOperation: one dedicated STA thread per operation, the main window as owner (the shell draws its own
// progress / conflict / access-denied dialogs), an advised progress sink that collects only the SUCCEEDED results so the list
// can be updated in place. Every COM object is released (FinalReleaseComObject) on the thread that created it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace WinlestiaFiles;

internal sealed class OpResult {
    internal readonly List<string> Removed = new();      // paths that left their folder (moved away, renamed from, deleted)
    internal readonly List<FileItem> Added = new();      // items that appeared (copied, moved in, renamed to, created), already stat'ed
    internal bool Aborted;
    internal string? Error;
}

internal static class FileOps {
    // Test hook: no native progress / confirmation / error UI.
    internal static bool Quiet;

    // Operations in flight. When the last one ends, Idle is raised on the worker thread (the resident instance re-checks its recycling there).
    static int active;
    internal static int Active => Volatile.Read(ref active);
    internal static event Action? Idle;

    const uint FOF_SILENT = 0x4, FOF_RENAMEONCOLLISION = 0x8, FOF_NOCONFIRMATION = 0x10, FOF_ALLOWUNDO = 0x40, FOF_NOCONFIRMMKDIR = 0x200, FOF_NOERRORUI = 0x400,
        FOFX_RECYCLEONDELETE = 0x00080000, FOFX_ADDUNDORECORD = 0x20000000;
    const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    static readonly Guid ClsidFileOperation = new("3ad05575-8857-4850-9277-11b85bdb8e09");
    static Guid iidShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

    internal static Task<OpResult> Copy(IntPtr owner, IReadOnlyList<string> sources, string destFolder, bool move, HashSet<string> watch) {
        // Same folder: Explorer's "name - Copy" instead of a conflict prompt. A move onto itself is a no-op and is filtered out.
        bool sameFolder = false;
        var list = new List<string>();
        foreach (var s in sources) {
            bool same = string.Equals(Path.GetDirectoryName(s)?.TrimEnd('\\'), destFolder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            if (same && move) continue;
            sameFolder |= same;
            list.Add(s);
        }
        if (list.Count == 0) return Task.FromResult(new OpResult());
        uint flags = FOF_ALLOWUNDO | FOFX_ADDUNDORECORD | FOF_NOCONFIRMMKDIR | (sameFolder ? FOF_RENAMEONCOLLISION : 0);
        return Run(owner, flags, watch, (op, held) => {
            var dest = Item(destFolder, held);
            foreach (var path in list) {
                var item = Item(path, held);
                if (move) op.MoveItem(item, dest, null, null); else op.CopyItem(item, dest, null, null);
            }
        });
    }

    // Recycle Bin unless permanent (the caller has already asked for confirmation in that case).
    internal static Task<OpResult> Delete(IntPtr owner, IReadOnlyList<string> paths, bool permanent, HashSet<string> watch) {
        uint flags = permanent ? FOF_NOCONFIRMATION : FOF_ALLOWUNDO | FOFX_ADDUNDORECORD | FOFX_RECYCLEONDELETE;
        return Run(owner, flags, watch, (op, held) => { foreach (var path in paths) op.DeleteItem(Item(path, held), null); });
    }

    internal static Task<OpResult> Rename(IntPtr owner, string path, string newName, HashSet<string> watch) =>
        Run(owner, FOF_ALLOWUNDO | FOFX_ADDUNDORECORD, watch, (op, held) => op.RenameItem(Item(path, held), newName, null));

    // Auto-unique name ("Nouveau dossier", "Nouveau dossier (2)", ...) picked on the worker thread.
    internal static Task<OpResult> NewFolder(IntPtr owner, string parent, string baseName, HashSet<string> watch) =>
        Run(owner, FOF_ALLOWUNDO | FOFX_ADDUNDORECORD | FOF_NOCONFIRMMKDIR, watch, (op, held) => {
            string name = baseName;
            for (int n = 2; Directory.Exists(Path.Combine(parent, name)) || File.Exists(Path.Combine(parent, name)); n++) name = $"{baseName} ({n})";
            op.NewItem(Item(parent, held), FILE_ATTRIBUTE_DIRECTORY, name, null, null);
        });

    static IShellItem Item(string path, List<object> held) {
        SHCreateItemFromParsingName(path, IntPtr.Zero, ref iidShellItem, out var item);
        held.Add(item);
        return item;
    }

    static Task<OpResult> Run(IntPtr owner, uint flags, HashSet<string> watch, Action<IFileOperation, List<object>> queue) {
        var done = new TaskCompletionSource<OpResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Increment(ref active);
        if (Quiet) flags |= FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI;
        var thread = new Thread(() => {
            var result = new OpResult();
            var held = new List<object>();
            IFileOperation? op = null; Sink? sink = null; uint cookie = 0; bool advised = false;
            try {
                op = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(ClsidFileOperation)!)!;
                op.SetOperationFlags(flags);
                if (owner != IntPtr.Zero) op.SetOwnerWindow(owner);
                sink = new Sink();
                op.Advise(sink, out cookie); advised = true;
                queue(op, held);
                op.PerformOperations();
                op.GetAnyOperationsAborted(out result.Aborted);
            } catch (Exception ex) {
                result.Error = ex.Message;        // user cancel / access denied surface here too; whatever the sink already collected stays valid
            } finally {
                try { if (advised) op!.Unadvise(cookie); } catch { }
                foreach (var o in held) { try { Marshal.FinalReleaseComObject(o); } catch { } }
                if (op != null) { try { Marshal.FinalReleaseComObject(op); } catch { } }
            }
            // Outside the COM call: stat the new items (find data only) for the folders somebody is looking at.
            var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (sink != null) {
                foreach (var r in sink.Results) {
                    if (r.Hr < 0) continue;
                    if (r.Removed != null) result.Removed.Add(r.Removed);
                    if (r.Added != null && watch.Contains(Path.GetDirectoryName(r.Added)?.TrimEnd('\\') ?? "")) {
                        var item = FileItem.Probe(r.Added, types);
                        if (item != null) result.Added.Add(item);
                    }
                }
            }
            done.TrySetResult(result);
            if (Interlocked.Decrement(ref active) == 0) { try { Idle?.Invoke(); } catch { } }
        }) { IsBackground = true, Name = "FileOperation" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return done.Task;
    }

    // ---- sink -------------------------------------------------------------------------------------------------

    sealed record Entry(int Hr, string? Removed, string? Added);

    [ComVisible(true)]
    sealed class Sink : IFileOperationProgressSink {
        internal readonly List<Entry> Results = new();

        // Takes the path out of a shell item and releases the wrapper (the callback owns nothing else).
        static string? PathOf(IShellItem? item) {
            if (item == null) return null;
            try {
                item.GetDisplayName(0x80058000, out var p);          // SIGDN_FILESYSPATH
                if (p == IntPtr.Zero) return null;
                string s = Marshal.PtrToStringUni(p)!; Marshal.FreeCoTaskMem(p); return s;
            } catch { return null; }
            finally { try { Marshal.FinalReleaseComObject(item); } catch { } }
        }
        static string? Combine(string? folder, string? name) => folder != null && name != null ? Path.Combine(folder, name) : null;

        public void StartOperations() { }
        public void FinishOperations(int hr) { }
        public void PreRenameItem(uint f, IShellItem item, string newName) { }
        public void PostRenameItem(uint f, IShellItem item, string newName, int hr, IShellItem? created) {
            string? old = PathOf(item), now = PathOf(created);
            if (hr >= 0 && now != null) Results.Add(new Entry(hr, old, now));
        }
        public void PreMoveItem(uint f, IShellItem item, IShellItem dest, string newName) { }
        public void PostMoveItem(uint f, IShellItem item, IShellItem dest, string newName, int hr, IShellItem? created) {
            string? old = PathOf(item), now = PathOf(created); PathOf(dest);
            if (hr >= 0 && now != null) Results.Add(new Entry(hr, old, now));
        }
        public void PreCopyItem(uint f, IShellItem item, IShellItem dest, string newName) { }
        public void PostCopyItem(uint f, IShellItem item, IShellItem dest, string newName, int hr, IShellItem? created) {
            PathOf(item); PathOf(dest); string? now = PathOf(created);
            if (hr >= 0 && now != null) Results.Add(new Entry(hr, null, now));
        }
        public void PreDeleteItem(uint f, IShellItem item) { }
        public void PostDeleteItem(uint f, IShellItem item, int hr, IShellItem? created) {
            string? old = PathOf(item); PathOf(created);
            if (hr >= 0 && old != null) Results.Add(new Entry(hr, old, null));
        }
        public void PreNewItem(uint f, IShellItem dest, string newName) { }
        public void PostNewItem(uint f, IShellItem dest, string newName, string? template, uint attrs, int hr, IShellItem? created) {
            PathOf(dest); string? now = PathOf(created);
            if (hr >= 0 && now != null) Results.Add(new Entry(hr, null, now));
        }
        public void UpdateProgress(uint total, uint sofar) { }
        public void ResetTimer() { }
        public void PauseTimer() { }
        public void ResumeTimer() { }
    }

    // ---- interop ----------------------------------------------------------------------------------------------

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent([MarshalAs(UnmanagedType.Interface)] out IShellItem item);
        void GetDisplayName(uint sigdn, out IntPtr name);
        void GetAttributes(uint mask, out uint attrs);
        void Compare(IShellItem other, uint hint, out int order);
    }

    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileOperation {
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
        void MoveItem(IShellItem item, IShellItem dest, [MarshalAs(UnmanagedType.LPWStr)] string? newName, IFileOperationProgressSink? sink);
        void MoveItems([MarshalAs(UnmanagedType.Interface)] object items, IShellItem dest);
        void CopyItem(IShellItem item, IShellItem dest, [MarshalAs(UnmanagedType.LPWStr)] string? newName, IFileOperationProgressSink? sink);
        void CopyItems([MarshalAs(UnmanagedType.Interface)] object items, IShellItem dest);
        void DeleteItem(IShellItem item, IFileOperationProgressSink? sink);
        void DeleteItems([MarshalAs(UnmanagedType.Interface)] object items);
        void NewItem(IShellItem dest, uint attrs, [MarshalAs(UnmanagedType.LPWStr)] string name, [MarshalAs(UnmanagedType.LPWStr)] string? template, IFileOperationProgressSink? sink);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }

    [ComImport, Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileOperationProgressSink {
        void StartOperations();
        void FinishOperations(int hr);
        void PreRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        void PostRenameItem(uint flags, IShellItem item, [MarshalAs(UnmanagedType.LPWStr)] string newName, int hr, IShellItem? created);
        void PreMoveItem(uint flags, IShellItem item, IShellItem dest, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        void PostMoveItem(uint flags, IShellItem item, IShellItem dest, [MarshalAs(UnmanagedType.LPWStr)] string newName, int hr, IShellItem? created);
        void PreCopyItem(uint flags, IShellItem item, IShellItem dest, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        void PostCopyItem(uint flags, IShellItem item, IShellItem dest, [MarshalAs(UnmanagedType.LPWStr)] string newName, int hr, IShellItem? created);
        void PreDeleteItem(uint flags, IShellItem item);
        void PostDeleteItem(uint flags, IShellItem item, int hr, IShellItem? created);
        void PreNewItem(uint flags, IShellItem dest, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        void PostNewItem(uint flags, IShellItem dest, [MarshalAs(UnmanagedType.LPWStr)] string newName, [MarshalAs(UnmanagedType.LPWStr)] string? template, uint attrs, int hr, IShellItem? created);
        void UpdateProgress(uint total, uint sofar);
        void ResetTimer();
        void PauseTimer();
        void ResumeTimer();
    }
}
