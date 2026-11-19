// C# 12 / VsCodeTheme.cs / Optional, reversible export of the wallpaper palette to VS Code (workbench.colorCustomizations). Event-driven only: no timer, no file watcher. Pure logic lives in VsCodeThemePolicy.cs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PanneauxGlass;

internal sealed class VsCodeTheme
{
    internal sealed record Target(string Id, string Path);
    sealed class Saved
    {
        public int Version { get; set; } = 1; public bool Existed
        {
            get; set;
        }
        public bool HadKey
        {
            get; set;
        }
        public string? RawValue
        {
            get; set;
        }
        public string LastHash { get; set; } = "";
    }

    readonly string appData, backupDir;
    CancellationTokenSource? pending; readonly object gate = new();
    // "" = nothing to report, otherwise: absent | ok | refused | changed | error
    internal string Status { get; private set; } = "";

    internal VsCodeTheme(string appData, string backupDir)
    {
        this.appData = appData;
        this.backupDir = backupDir;
    }
    internal static VsCodeTheme ForCurrentUser() => new(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Panneaux", "vscode"));

    // VS Code counts as installed when its per-user data folder exists.
    internal List<Target> Targets()
    {
        var list = new List<Target>();
        foreach (var (id, folder) in new[] { ("stable", "Code"), ("insiders", "Code - Insiders") })
        {
            string user = Path.Combine(appData, folder, "User");
            if (Directory.Exists(user))
                list.Add(new Target(id, Path.Combine(user, "settings.json")));
        }
        return list;
    }
    internal bool Available() => Targets().Count > 0;

    // Debounced entry point for palette changes: one delayed run, cancelled by the next change.
    internal void Schedule(IReadOnlyDictionary<string, string> palette, int delayMs = 1200)
    {
        CancellationToken token;
        lock (gate)
        {
            pending?.Cancel();
            pending?.Dispose();
            pending = new CancellationTokenSource();
            token = pending.Token;
        }
        _ = Task.Run(async () => { try { await Task.Delay(delayMs, token); Apply(palette, token); } catch (OperationCanceledException) { } catch { Status = "error"; } });
    }
    internal void CancelPending()
    {
        lock (gate)
        {
            pending?.Cancel();
            pending?.Dispose();
            pending = null;
        }
    }

    // ---------- apply ----------
    internal string Apply(IReadOnlyDictionary<string, string> palette, CancellationToken token = default)
    {
        lock (apply)
        {
            if (token.IsCancellationRequested)
                return Status;
            var colors = VsCodeThemePolicy.Map(palette);
            if (colors == null)
                return Status = "refused";
            var targets = Targets();
            if (targets.Count == 0)
                return Status = "absent";
            string worst = "ok";
            foreach (var t in targets)
            {
                string r = ApplyOne(t, colors);
                if (r != "ok")
                    worst = r;
            }
            return Status = worst;
        }
    }
    static readonly object apply = new();

    string ApplyOne(Target t, IReadOnlyDictionary<string, string> colors)
    {
        try
        {
            bool created = false;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                string path = Resolve(t.Path);
                byte[]? before = ReadBytes(path);
                string text = Decode(before, out bool decoded);
                if (!decoded)
                    return "refused";
                if (!VsCodeThemePolicy.TryApplyColors(text, colors, out string result, out _))
                    return "refused";
                string? stateFile = StateFile(t.Id);
                if (!File.Exists(stateFile) || created)
                {
                    created = true;
                    if (!VsCodeThemePolicy.TryGetValue(text, out string? raw, out _))
                        return "refused";
                    Directory.CreateDirectory(backupDir);
                    if (before != null)
                        File.WriteAllBytes(CopyFile(t.Id), before);
                    WriteState(t.Id, new Saved { Existed = before != null, HadKey = raw != null, RawValue = raw, LastHash = Hash(before) });
                }
                byte[] after = Encoding.UTF8.GetBytes(result);
                if (before != null && Same(before, after))
                    return "ok";
                if (!Commit(path, before, after))
                    continue;
                var state = ReadState(t.Id);
                if (state != null)
                {
                    state.LastHash = Hash(after);
                    WriteState(t.Id, state);
                }
                return "ok";
            }
            return "changed";
        }
        catch { return "error"; }
    }

    // ---------- restore ----------
    // Used when the option is switched off and by `--vscode-restore` (uninstall). Returns ok | refused | error | none.
    internal string Restore()
    {
        lock (apply)
        {
            CancelPending();
            string worst = "none";
            foreach (var id in new[] { "stable", "insiders" })
            {
                var target = Targets().Find(x => x.Id == id) ?? FromState(id);
                if (target == null)
                    continue;
                string r = RestoreOne(target);
                if (r != "none" && (worst == "none" || r != "ok"))
                    worst = r;
            }
            return Status = worst == "none" ? "" : worst == "ok" ? "" : worst;
        }
    }
    Target? FromState(string id) => null;   // a state without its VS Code folder has nothing left to restore

    string RestoreOne(Target t)
    {
        try
        {
            string path = Resolve(t.Path);
            var state = ReadState(t.Id);
            byte[]? now = ReadBytes(path);
            if (now == null)
            {
                Forget(t.Id);
                return state == null ? "none" : "ok";
            }
            if (state != null && Hash(now) == state.LastHash && state.Existed && File.Exists(CopyFile(t.Id)))
            {
                // Nothing else touched the file since our last write: put the original bytes back.
                if (!Commit(path, now, File.ReadAllBytes(CopyFile(t.Id))))
                    return "error";
                Forget(t.Id);
                return "ok";
            }
            if (state != null && Hash(now) == state.LastHash && !state.Existed)
            {
                File.Delete(path);
                Forget(t.Id);
                return "ok";
            }
            string text = Decode(now, out bool decoded);
            if (!decoded)
                return "refused";
            string result;
            string error;
            if (state != null)
            {
                if (!(state.HadKey && state.RawValue != null ? VsCodeThemePolicy.TrySetRaw(text, state.RawValue, out result, out error) : VsCodeThemePolicy.TryRemove(text, out result, out error)))
                    return "refused";
            }
            else
            {
                // No backup: remove only the key, and only when its value is recognisably ours.
                if (!VsCodeThemePolicy.LooksOwned(text))
                    return "none";
                if (!VsCodeThemePolicy.TryRemove(text, out result, out error))
                    return "refused";
            }
            if (result != text && !Commit(path, now, Encoding.UTF8.GetBytes(result)))
                return "error";
            Forget(t.Id);
            return "ok";
        }
        catch { return "error"; }
    }

    // ---------- files ----------
    static string Resolve(string path)
    {
        try
        {
            var target = new FileInfo(path).ResolveLinkTarget(true);
            return target?.FullName ?? path;
        }
        catch { return path; }
    }
    static byte[]? ReadBytes(string path)
    {
        if (!File.Exists(path))
            return null;
        using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var m = new MemoryStream();
        s.CopyTo(m);
        return m.ToArray();
    }
    static string Decode(byte[]? bytes, out bool ok)
    {
        ok = true;
        if (bytes == null)
            return "";
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch { ok = false; return ""; }
    }
    static bool Same(byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b);
    static string Hash(byte[]? bytes) => bytes == null ? "-" : Convert.ToHexString(SHA256.HashData(bytes));

    // Temp file in the same folder, then File.Replace. The file is read again just before the swap: if it changed since `before`, nothing is written and the caller starts over.
    static bool Commit(string path, byte[]? before, byte[] after)
    {
        string tmp = path + ".winlestia.tmp";
        try
        {
            File.WriteAllBytes(tmp, after);
            if (!Same(ReadBytes(path) ?? Array.Empty<byte>(), before ?? Array.Empty<byte>()) || (before == null) != !File.Exists(path))
                return false;
            if (before == null)
                File.Move(tmp, path);
            else
                File.Replace(tmp, path, null, true);
            return true;
        }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
    }

    string StateFile(string id) => Path.Combine(backupDir, id + ".state.json");
    string CopyFile(string id) => Path.Combine(backupDir, id + ".settings.json.bak");
    Saved? ReadState(string id)
    {
        try
        {
            return File.Exists(StateFile(id)) ? JsonSerializer.Deserialize<Saved>(File.ReadAllText(StateFile(id))) : null;
        }
        catch { return null; }
    }
    void WriteState(string id, Saved s)
    {
        string tmp = StateFile(id) + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s));
        File.Move(tmp, StateFile(id), true);
    }
    // Completed restore: the state goes, the full copy is kept as the last safety net.
    void Forget(string id)
    {
        try
        {
            if (File.Exists(StateFile(id)))
                File.Delete(StateFile(id));
            if (File.Exists(CopyFile(id)))
                File.Move(CopyFile(id), Path.Combine(backupDir, id + ".settings.json.last"), true);
        }
        catch { }
    }
    internal bool HasState() => File.Exists(StateFile("stable")) || File.Exists(StateFile("insiders"));
}
