using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace WinlestiaSetup;

sealed class InstallInfo
{
    public bool Exists; public string Version = ""; public string Dir = ""; public bool FromInno;

    const string OwnKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Winlestia";
    const string InnoKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{8E2B7C41-5F1A-4F7B-9C3D-6A2E0D5B7C18}_is1";
    public static string OwnKeyPath => OwnKey;
    public static string InnoKeyPath => InnoKey;

    public static InstallInfo Detect()
    {
        var i = new InstallInfo();
        using (var k = Registry.CurrentUser.OpenSubKey(OwnKey))
            if (k != null) { i.Version = k.GetValue("DisplayVersion") as string ?? "";
                i.Dir = (k.GetValue("InstallLocation") as string ?? "").TrimEnd('\\'); i.Exists = true; }
        if (!i.Exists)
            using (var k = Registry.CurrentUser.OpenSubKey(InnoKey))
                if (k != null) { i.Version = k.GetValue("DisplayVersion") as string ?? "";
                    i.Dir = (k.GetValue("Inno Setup: App Path") as string ?? "").TrimEnd('\\'); i.Exists = true; i.FromInno = true; }
        var exe = Path.Combine(i.Exists && i.Dir.Length > 0 ? i.Dir : Ctx.DefaultDir, @"Glass-v5\Panneaux.Glass.exe");
        if (File.Exists(exe))
        {
            if (!i.Exists) { i.Exists = true; i.Dir = Ctx.DefaultDir; }
            if (i.Version.Length == 0) i.Version = FileVersionInfo.GetVersionInfo(exe).ProductVersion?.Split('+')[0] ?? "?";
        }
        else if (i.Exists && !Directory.Exists(i.Dir)) i = new InstallInfo();
        return i;
    }
}

static class Sys
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ShFileOp { public IntPtr hwnd; public uint wFunc; public string pFrom; public string? pTo; public ushort fFlags;
        public int fAborted; public IntPtr hMapping; public string? title; }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHFileOperation(ref ShFileOp op);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string cls, string? title);

    // Corbeille uniquement (FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_WANTNUKEWARNING, comme Uninstall-Winlestia.ps1).
    // FOF_WANTNUKEWARNING fait demander le shell au lieu de supprimer définitivement si l'élément ne peut pas être recyclé.
    // Refusé sur tout lecteur qui n'est pas un disque fixe (les lecteurs amovibles et réseau n'ont pas de Corbeille).
    public static bool Recycle(string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path)) return true;
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root) || root.StartsWith("\\\\") || new DriveInfo(root).DriveType != DriveType.Fixed)
            { Log.Write("Recycle Bin refused (not a fixed drive): " + path + ", left in place"); return false; }
        }
        catch (Exception e) { Log.Write("Recycle Bin refused (drive check failed: " + e.Message + "): " + path + ", left in place");
            return false; }
        var op = new ShFileOp { wFunc = 3, pFrom = path + "\0\0", fFlags = 0x0040 | 0x0010 | 0x4000 };
        int r = SHFileOperation(ref op);
        bool ok = r == 0 && op.fAborted == 0 && !Directory.Exists(path) && !File.Exists(path);
        Log.Write("Recycle Bin: " + path + (ok ? " -> ok" : " -> failed (code " + r + "), left in place"));
        return ok;
    }

    public static bool TrayPresent() => FindWindow("Shell_TrayWnd", null) != IntPtr.Zero;
    public static bool ExplorerRunning() => Process.GetProcessesByName("explorer").Length > 0;

    public static (int code, string output) Run(string file, string args, int timeoutMs = 60000)
    {
        var psi = new ProcessStartInfo(file, args) {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEndAsync(); var e = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return (-1, "timeout"); }
        return (p.ExitCode, (o.Result + e.Result).Trim());
    }

    public static bool Under(string path, string root) =>
        Path.GetFullPath(path).TrimEnd('\\').StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    public static void Shortcut(string lnk, string target, string args, string workDir, string icon)
    {
        dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        var s = sh.CreateShortcut(lnk);
        s.TargetPath = target; s.Arguments = args; s.WorkingDirectory = workDir; s.IconLocation = icon; s.Save();
    }

    public static string ShortcutTarget(string lnk)
    {
        try { dynamic sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            return (string)sh.CreateShortcut(lnk).TargetPath; } catch { return ""; }
    }
}

sealed record StepDef(string Key, Func<string?> Run);

static class Runner
{
    public static string? Failure;
    public static readonly List<string> Warnings = new();

    // Exécute les étapes sur un thread de travail ; une étape en échec interrompt si abortOnFail, sinon elle est signalée et l'exécution
    // continue.
    public static async Task Run(List<StepDef> steps, bool abortOnFail, Action<int, string> onStep)
    {
        Failure = null; Warnings.Clear();
        await Task.Run(() =>
        {
            for (int i = 0; i < steps.Count; i++)
            {
                onStep(i, steps[i].Key);
                Log.Write("== step: " + steps[i].Key);
                try
                {
                    var w = steps[i].Run();
                    if (!string.IsNullOrEmpty(w)) { Warnings.Add(w); Log.Write("warning: " + w); }
                    if (Ctx.Dry && !Ctx.Screenshot) Thread.Sleep(380);
                }
                catch (Exception e)
                {
                    Log.Write("ERROR in " + steps[i].Key + ": " + e.Message);
                    Warnings.Add(Loc.T("step." + steps[i].Key) + ": " + e.Message);
                    if (abortOnFail) { Failure = e.Message; break; }
                }
            }
            onStep(steps.Count, "");
        });
    }

    public static void Do(string what, Action a)
    {
        if (Ctx.Dry) { Log.Write("[dry-run] would: " + what); return; }
        Log.Write(what); a();
    }
}

static class Installer
{
    static readonly string[] Payloaded = { "Glass-v5", "Caelestia-Windows", "Winlestia-Terminal", @"Winjets\builtin" };

    public static Stream? OpenPayload()
    {
        var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip");
        if (s != null) return s;
        var f = Path.Combine(AppContext.BaseDirectory, "payload.zip");
        return File.Exists(f) ? File.OpenRead(f) : null;
    }
    public static bool HasPayload() { using var s = OpenPayload(); return s != null; }

    public static List<StepDef> Steps()
    {
        var dir = Ctx.InstallDir; var l = new List<StepDef>();
        l.Add(new("close", () => { ShellCloser.Close(dir); return null; }));
        if (Ctx.Mode is Mode.Install or Mode.Update)
        {
            l.Add(new("backup", () => Backup(dir)));
            l.Add(new("copy", () => Copy(dir)));
        }
        l.Add(new("config", () => Config()));
        if (Ctx.Features.TryGetValue("terminal", out var t)
            && (t || File.Exists(Path.Combine(Ctx.LocalApp, @"Winlestia\terminal-backup\state.json"))))
            l.Add(new("terminal", () => Terminal(dir, t)));
        if (Ctx.Mode is Mode.Install or Mode.Update)
        {
            l.Add(new("task", () => Task(dir)));
            l.Add(new("shortcuts", () => Shortcuts(dir)));
            l.Add(new("registry", () => Registry_(dir)));
        }
        return l;
    }

    static string? Backup(string dir)
    {
        if (!Ctx.Info.Exists) { Log.Write("Fresh install: nothing to back up."); return null; }
        var tag = (Ctx.Info.Version.Length > 0 ? Ctx.Info.Version : "previous") + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var dest = Path.Combine(Ctx.BackupRoot, tag);
        Runner.Do("move previous program folders to " + dest, () =>
        {
            if (Directory.Exists(Ctx.BackupRoot)) foreach (var old in Directory.GetDirectories(Ctx.BackupRoot)) Sys.Recycle(old);
            Directory.CreateDirectory(dest);
            foreach (var p in Payloaded)
            {
                var src = Path.Combine(dir, p);
                if (!Directory.Exists(src)) continue;
                var to = Path.Combine(dest, p); Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                Directory.Move(src, to);
            }
        });
        return null;
    }

    static string? Copy(string dir)
    {
        using var ps = OpenPayload();
        if (ps == null) { Log.Write("[demo] no payload.zip: nothing to copy."); return null; }
        Runner.Do("extract payload.zip to " + dir, () =>
        {
            using var zip = new ZipArchive(ps, ZipArchiveMode.Read);
            var root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
            foreach (var e in zip.Entries)
            {
                if (e.FullName.EndsWith("/")) continue;
                var target = Path.GetFullPath(Path.Combine(dir, e.FullName.Replace('/', '\\')));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unsafe entry: "
                    + e.FullName);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                e.ExtractToFile(target, true);
            }
            var setupDir = Path.Combine(dir, "Setup"); Directory.CreateDirectory(setupDir);
            File.Copy(Environment.ProcessPath!, Path.Combine(setupDir, "WinlestiaSetup.exe"), true);
        });
        return null;
    }

    static string? Config()
    {
        var path = Ctx.PrefsPath;
        Runner.Do("write feature choices to " + path, () =>
        {
            JsonObject root = new();
            if (File.Exists(path))
            {
                root = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? new();
                var bak = path + ".setup-backup"; if (!File.Exists(bak)) File.Copy(path, bak);
            }
            foreach (var f in Features.All.Where(f => f.Kind == FeatureKind.Pref))
                foreach (var k in f.Keys) root[k] = Ctx.Features[f.Id];
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(true));
        });
        foreach (var f in Features.All.Where(f => f.Kind == FeatureKind.Pref))
            Log.Write($"  {string.Join("+", f.Keys)} = {Ctx.Features[f.Id]}");
        return null;
    }

    static string? Terminal(string dir, bool on)
    {
        var py = Path.Combine(dir, @"Caelestia-Windows\.venv\Scripts\python.exe");
        var script = Path.Combine(dir, @"Winlestia-Terminal\install.py");
        var args = "\"" + script + "\"" + (on ? "" : " --restore");
        string? warn = null;
        Runner.Do((on ? "apply" : "restore") + " terminal integration: python install.py" + (on ? "" : " --restore"), () =>
        {
            if (!File.Exists(py) || !File.Exists(script)) { warn = Loc.T("warn.terminalMissing"); return; }
            var (c, o) = Sys.Run(py, args, 180000);
            if (c != 0) { Log.Write(o); warn = Loc.T("warn.terminal"); }
        });
        return warn;
    }

    static string? Task(string dir)
    {
        var exe = Path.Combine(dir, @"Glass-v5\Panneaux.Glass.exe");
        Runner.Do("create scheduled task WinlestiaShell (logon, current user, no elevation)", () =>
        {
            var user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            string E(string s) => System.Security.SecurityElement.Escape(s) ?? "";
            var xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n<Task version=\"1.2\" "
            + "xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n"
                + "  <RegistrationInfo><Description>WinlestiaShell</Description></RegistrationInfo>\n"
                + "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + E(user) + "</UserId></LogonTrigger></Triggers>\n"
                + "  <Principals><Principal id=\"Author\"><UserId>" + E(user)
                    + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>\n"
                + "  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false"
                + "</DisallowStartIfOnBatteries>"
                + "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowHardTerminate>true</AllowHardTerminate>"
                + "<StartWhenAvailable>false</StartWhenAvailable>"
                + "<RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable><AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true"
                + "</Enabled><Hidden>false</Hidden>"
                + "<RunOnlyIfIdle>false</RunOnlyIfIdle><WakeToRun>false</WakeToRun><ExecutionTimeLimit>PT0S</ExecutionTimeLimit>"
                + "<Priority>4</Priority></Settings>\n"
                + "  <Actions Context=\"Author\"><Exec><Command>" + E("\"" + exe + "\"")
                    + "</Command><Arguments>--qt-shell</Arguments><WorkingDirectory>" + E(Path.GetDirectoryName(exe)!)
                    + "</WorkingDirectory></Exec></Actions>\n</Task>\n";
            var tmp = Path.Combine(Path.GetTempPath(), "winlestia-task-" + Guid.NewGuid().ToString("N") + ".xml");
            File.WriteAllText(tmp, xml, Encoding.Unicode);
            try
            {
                var (c, o) = Sys.Run("schtasks.exe", "/create /tn WinlestiaShell /xml \"" + tmp + "\" /f");
                if (c != 0) throw new InvalidOperationException("schtasks: " + o);
            }
            finally { try { File.Delete(tmp); } catch { } }
            using var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            if (run?.GetValue("Panneaux") != null) { run.DeleteValue("Panneaux");
                Log.Write("Removed old Run value (replaced by the task)."); }
        });
        return null;
    }

    public static string StartMenu => Environment.GetFolderPath(Environment.SpecialFolder.Programs);

    static string? Shortcuts(string dir)
    {
        var exe = Path.Combine(dir, @"Glass-v5\Panneaux.Glass.exe"); var wd = Path.GetDirectoryName(exe)!;
        Runner.Do("create Start menu shortcuts in " + StartMenu, () =>
        {
            Sys.Shortcut(Path.Combine(StartMenu, "Winlestia.lnk"), exe, "--qt-shell --show", wd, exe + ",0");
            Sys.Shortcut(Path.Combine(StartMenu, Loc.T("sc.restore") + ".lnk"), Path.Combine(wd, "RETABLIR_BARRE_WINDOWS.exe"), "", wd, exe
                + ",0");
            Sys.Shortcut(Path.Combine(StartMenu, Loc.T("sc.uninstall") + ".lnk"), Path.Combine(dir, @"Setup\WinlestiaSetup.exe"),
                "--uninstall", Path.Combine(dir, "Setup"), exe + ",0");
        });
        return null;
    }

    static string? Registry_(string dir)
    {
        var exe = Path.Combine(dir, @"Glass-v5\Panneaux.Glass.exe");
        Runner.Do("write HKCU\\" + InstallInfo.OwnKeyPath
            + " (Installed apps entry, uninstall string -> Setup\\WinlestiaSetup.exe --uninstall)", () =>
        {
            using var k = Registry.CurrentUser.CreateSubKey(InstallInfo.OwnKeyPath);
            k.SetValue("DisplayName", "Winlestia"); k.SetValue("DisplayVersion", Ctx.Version); k.SetValue("Publisher", "dj");
            k.SetValue("InstallLocation", dir); k.SetValue("DisplayIcon", exe);
            k.SetValue("UninstallString", "\"" + Path.Combine(dir, @"Setup\WinlestiaSetup.exe") + "\" --uninstall");
            k.SetValue("NoModify", 1, RegistryValueKind.DWord); k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            if (Registry.CurrentUser.OpenSubKey(InstallInfo.InnoKeyPath) != null)
            {
                Registry.CurrentUser.DeleteSubKeyTree(InstallInfo.InnoKeyPath, false);
                Log.Write("Removed the Inno Setup uninstall entry (replaced by this one).");
            }
        });
        return null;
    }

    public static void Launch(string dir)
    {
        var exe = Path.Combine(dir, @"Glass-v5\Panneaux.Glass.exe");
        if (Ctx.Dry) { Log.Write("[dry-run] would: start " + exe + " --qt-shell --show"); return; }
        Process.Start(new ProcessStartInfo(exe, "--qt-shell --show") {
            WorkingDirectory = Path.GetDirectoryName(exe)!, UseShellExecute = false });
    }
}

static class ShellCloser
{
    // Même logique que installer\Close-Winlestia.ps1 : utilise le script installé s'il est présent, sinon un repli limité au chemin.
    public static void Close(string root)
    {
        var ps = Path.Combine(root, "Close-Winlestia.ps1");
        if (File.Exists(ps))
        {
            var args = "-NoLogo -NoProfile -ExecutionPolicy Bypass -File \"" + ps + "\" -AppRoot \"" + root + "\"";
            if (Ctx.Dry) { var (c, o) = Sys.Run("powershell.exe", args + " -ListOnly", 30000);
                Log.Write("[dry-run] processes that would be closed:" + Environment.NewLine + (o.Length > 0 ? o : "(none)")); }
            else { var (c, o) = Sys.Run("powershell.exe", args, 60000); Log.Write("Close-Winlestia.ps1 exit " + c + " " + o); }
            return;
        }
        if (!Directory.Exists(root)) { Log.Write("Nothing to close (no installation folder)."); return; }
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                var path = p.MainModule?.FileName; if (path == null || p.Id == Environment.ProcessId || !Sys.Under(path, root)) continue;
                Runner.Do("stop " + p.ProcessName + " (" + p.Id + ")", () => { p.Kill(); p.WaitForExit(3000); });
            }
            catch { }
        }
    }
}

static class Uninstaller
{
    public static List<StepDef> Steps()
    {
        var dir = Ctx.InstallDir;
        return new()
        {
            new("restore", () => RestoreShell(dir)),
            new("utask", () => { Runner.Do("schtasks /delete /tn WinlestiaShell /f",
                () => Sys.Run("schtasks.exe", "/delete /tn WinlestiaShell /f")); return null; }),
            new("stop", () => { ShellCloser.Close(dir); return null; }),
            new("undo", () => Undo(dir)),
            new("uregistry", Registry_),
            new("ushortcuts", () => Shortcuts(dir)),
            new("files", () => Files(dir)),
            new("verify", Verify),
        };
    }

    static string? RestoreShell(string dir)
    {
        var backend = Path.Combine(dir, @"Glass-v5\Panneaux.Glass.exe");
        if (File.Exists(backend))
            Runner.Do("run backend --restore-taskbar", () =>
            {
                var (c, o) = Sys.Run(backend, "--restore-taskbar", 15000); Log.Write("--restore-taskbar exit " + c);
            });
        else Log.Write("Backend not found: falling back to the explorer check below.");
        return EnsureExplorer();
    }

    // Shell_TrayWnd est la fenêtre de la barre des tâches : si elle manque, le shell n'est pas vraiment revenu, donc explorer.exe est
    // relancé.
    public static string? EnsureExplorer()
    {
        bool Wait(int ms) { var end = DateTime.UtcNow.AddMilliseconds(ms); while (DateTime.UtcNow < end) {
            if (Sys.TrayPresent()) return true; Thread.Sleep(250); } return Sys.TrayPresent(); }
        if (Wait(Ctx.Dry ? 500 : 6000)) { Log.Write("Windows taskbar present (Shell_TrayWnd)."); return null; }
        if (Ctx.Dry) { Log.Write("[dry-run] taskbar absent: would restart explorer.exe"); return null; }
        Log.Write("Taskbar absent: restarting explorer.exe");
        foreach (var p in Process.GetProcessesByName("explorer")) { try { p.Kill(); p.WaitForExit(3000); } catch { } }
        Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        return Wait(15000) ? null : Loc.T("warn.noTaskbar");
    }

    static string? Undo(string dir)
    {
        string? warn = null;
        var files = Path.Combine(dir, @"Glass-v5\Winlestia.Files.exe");
        if (File.Exists(files)) Runner.Do("Winlestia.Files.exe --restore (folder handler)", () => Sys.Run(files, "--restore", 30000));
        var py = Path.Combine(dir, @"Caelestia-Windows\.venv\Scripts\python.exe");
        var inst = Path.Combine(dir, @"Winlestia-Terminal\install.py");
        if (File.Exists(py) && File.Exists(inst))
            Runner.Do("terminal integration: install.py --restore", () => {
                var (c, _) = Sys.Run(py, "\"" + inst + "\" --restore", 120000); if (c != 0) warn = Loc.T("warn.terminal"); });
        return warn;
    }

    static string? Registry_()
    {
        Runner.Do("delete HKCU\\" + InstallInfo.OwnKeyPath, () => Registry.CurrentUser.DeleteSubKeyTree(InstallInfo.OwnKeyPath, false));
        if (Registry.CurrentUser.OpenSubKey(InstallInfo.InnoKeyPath) != null)
            Runner.Do("delete HKCU\\" + InstallInfo.InnoKeyPath, () => Registry.CurrentUser.DeleteSubKeyTree(InstallInfo.InnoKeyPath,
                false));
        const string UnKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        using (var un = Registry.CurrentUser.OpenSubKey(UnKey))
            foreach (var name in un?.GetSubKeyNames() ?? Array.Empty<string>())
            {
                using var sk = un!.OpenSubKey(name);
                if (sk?.GetValue("DisplayName") as string == "Winlestia")
                    Runner.Do("delete HKCU\\" + UnKey + "\\" + name
                        + " (DisplayName Winlestia)", () => Registry.CurrentUser.DeleteSubKeyTree(UnKey + "\\" + name, false));
            }
        using (var run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            if ((run?.GetValue("Panneaux") as string)?.Contains("Glass-v5", StringComparison.OrdinalIgnoreCase) == true)
                Runner.Do("delete HKCU Run value Panneaux", () => {
                    using var w = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
                w?.DeleteValue("Panneaux", false); });
        var glass = Path.Combine(Ctx.DefaultDir, "Glass-v5");
        using (var env = Registry.CurrentUser.OpenSubKey("Environment"))
            if (env?.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames) is string path
                && path.Split(';').Any(s => s.TrimEnd('\\').Equals(glass, StringComparison.OrdinalIgnoreCase)))
                Runner.Do("remove " + glass + " from the user PATH", () =>
                {
                    using var w = Registry.CurrentUser.OpenSubKey("Environment", true);
                    var kept = path.Split(';').Where(s => s.Length > 0
                        && !s.TrimEnd('\\').Equals(glass, StringComparison.OrdinalIgnoreCase));
                    w!.SetValue("Path", string.Join(";", kept), RegistryValueKind.ExpandString);
                });
        if (Ctx.O.RemoveData && Registry.CurrentUser.OpenSubKey(@"Software\Winlestia") != null)
            Runner.Do("delete HKCU\\Software\\Winlestia", () => Registry.CurrentUser.DeleteSubKeyTree(@"Software\Winlestia", false));
        return null;
    }

    static string? Shortcuts(string dir)
    {
        var names = new List<string> { "Winlestia" };
        names.AddRange(Loc.AllValues("sc.restore")); names.AddRange(Loc.AllValues("sc.uninstall"));
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        foreach (var folder in new[] { Installer.StartMenu, desktop })
            foreach (var n in names.Distinct())
            {
                var f = Path.Combine(folder, n + ".lnk");
                if (!File.Exists(f)) continue;
                var tgt = Sys.ShortcutTarget(f);
                if (tgt.Length > 0 && !tgt.Contains("Winlestia", StringComparison.OrdinalIgnoreCase)
                    && !tgt.Contains("Panneaux", StringComparison.OrdinalIgnoreCase)
                    && !tgt.Contains("RETABLIR", StringComparison.OrdinalIgnoreCase)) continue;
                Runner.Do("delete shortcut " + f, () => File.Delete(f));
            }
        return null;
    }

    static string? Files(string dir)
    {
        var left = new List<string>();
        // Noms exacts uniquement, sans joker. Les données utilisateur vont à la Corbeille seulement si l'utilisateur a choisi de les
        // supprimer.
        // Dossier Qt : "Panneaux \u00B7 Caelestia Windows", comme dans Uninstall-Winlestia.ps1.
        var data = new[] { Path.Combine(Ctx.LocalApp, "Panneaux"), Path.Combine(Ctx.LocalApp, "Winlestia"),
            Path.Combine(Ctx.LocalApp, "Panneaux \u00B7 Caelestia Windows") };
        void Bin(string p) { if (!Directory.Exists(p)) return; if (Ctx.Dry) { Log.Write("[dry-run] would send to Recycle Bin: " + p);
            return; } if (!Sys.Recycle(p)) left.Add(p); }
        if (Ctx.O.RemoveData) foreach (var d in data) Bin(d); else Log.Write("User data kept: " + string.Join(", ", data));
        if (Ctx.O.RemoveData) Bin(Ctx.BackupRoot); else Log.Write("Backups kept: " + Ctx.BackupRoot);
        var norm = Path.GetFullPath(dir).TrimEnd('\\');
        if (!norm.Equals(Path.GetFullPath(Ctx.DefaultDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
            || Path.GetPathRoot(norm)!.TrimEnd('\\').Equals(norm, StringComparison.OrdinalIgnoreCase))
        { Log.Write("Program folder refused (not exactly " + Ctx.DefaultDir + "): " + dir); left.Add(dir); }
        else Bin(norm);
        return left.Count == 0 ? null : Loc.F("warn.left", string.Join(", ", left));
    }

    static string? Verify()
    {
        if (!Sys.ExplorerRunning() && !Ctx.Dry) Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
        var w = EnsureExplorer();
        Log.Write("explorer.exe running: " + Sys.ExplorerRunning() + ", taskbar: " + Sys.TrayPresent());
        return w;
    }

    // Nettoyage différé de la copie dans %TEMP% faite par RelocateIfNeeded (uniquement un dossier WinlestiaSetup-xxxxxxxx directement sous
    // %TEMP%).
    public static void CleanTempCopy()
    {
        if (Ctx.O.DryRun || !Ctx.O.Relocated) return;
        var dir = Path.GetDirectoryName(Environment.ProcessPath!)!.TrimEnd('\\');
        if (!Path.GetFileName(dir).StartsWith("WinlestiaSetup-")
            || !Path.GetDirectoryName(dir)!.TrimEnd('\\').Equals(Path.GetTempPath().TrimEnd('\\'),
            StringComparison.OrdinalIgnoreCase)) return;
        try { Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 4 127.0.0.1 >nul & rmdir /s /q \"" + dir + "\"") {
            CreateNoWindow = true, UseShellExecute = false }); } catch { }
    }

    // Le désinstalleur ne doit pas s'exécuter depuis le dossier qu'il supprime : copie dans %TEMP% puis relance.
    public static bool RelocateIfNeeded(string[] args)
    {
        var exe = Environment.ProcessPath!;
        bool inside = Sys.Under(exe, Ctx.InstallDir) || Sys.Under(exe, Ctx.DefaultDir);
        if (!inside || Ctx.O.Relocated) return false;
        if (Ctx.O.DryRun) { Log.Write("[dry-run] would copy the setup to %TEMP% and relaunch (running from the install folder)");
            return false; }
        var tmp = Path.Combine(Path.GetTempPath(), "WinlestiaSetup-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(tmp);
        var copy = Path.Combine(tmp, "WinlestiaSetup.exe"); File.Copy(exe, copy, true);
        Process.Start(new ProcessStartInfo(copy, string.Join(" ", args.Select(a => "\"" + a + "\"")) + " --relocated") {
            UseShellExecute = false });
        return true;
    }
}
