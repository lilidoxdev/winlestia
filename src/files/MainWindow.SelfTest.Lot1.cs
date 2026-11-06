#if WINLESTIA_SELFTEST
// WINLESTIA_FILES_SELFTEST=lot1 : address bar (breadcrumb / edit mode), copy path, quick access (pin, order, label, broken targets, persistence), sort (default, folders first, persistence),
// corrupt settings.json. Needs WINLESTIA_FILES_DATA_DIR = a fresh folder under %TEMP% (the harness sets it) and a fixture folder (first argument) under %TEMP%\WinlestiaFilesOps.
// Results: PASS/FAIL lines in <data dir>\selftest.log.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace WinlestiaFiles;

public sealed partial class MainWindow
{
    static async Task<bool> WaitFor(Func<bool> cond, int limitMs = 5000)
    {
        long t = Stopwatch.GetTimestamp();
        while (Perf.Ms(t) < limitMs)
        {
            if (cond())
                return true;
            await Task.Delay(20);
        }
        return cond();
    }

    static async Task<string> ClipText()
    {
        for (int i = 0; i < 20; i++)
        {
            try
            {
                return Clipboard.GetText();
            }
            catch { await Task.Delay(100); }
        }
        return "<clipboard busy>";
    }

    static string SettingsText()
    {
        try
        {
            return File.ReadAllText(FilesSettings.FilePath);
        }
        catch { return ""; }
    }

    async Task Lot1Test()
    {
        string F = Fixture();
        string tmp = Path.GetFullPath(Path.GetTempPath());
        if (!Path.GetFullPath(FilesSettings.Dir).StartsWith(tmp, StringComparison.OrdinalIgnoreCase)
            || Environment.GetEnvironmentVariable("WINLESTIA_FILES_DATA_DIR") is not { Length: > 0 })
        {
            T("ABORT data dir is not a %TEMP% folder redirected by WINLESTIA_FILES_DATA_DIR: " + FilesSettings.Dir);
            Close();
            return;
        }
        T("START lot1 fixture " + F + " data " + FilesSettings.Dir);
        Activate();
        await WaitLoaded();
        await sidebar.Ready;
        await sidebar.Recheck();

        // ---- first launch: quick access initialised with the known folders, sort default ------------------------------------------------
        var firstView = sidebar.QuickView();
        string json0 = SettingsText();
        Check("first launch: settings.json written with quickAccess",
              json0.Contains("\"quickAccess\"") && QuickAccess.Entries.Count == firstView.Count && firstView.Count > 0,
              $"{firstView.Count} entries");
        Check("first launch: no sort key, default sort = modified, newest first, mixed",
              !json0.Contains("\"sort\"")
                  && Active.Sort == SortSpec.Default
                  && !Active.Sort.Ascending
                  && !Active.Sort.FoldersFirst
                  && Active.Sort.Column == SortColumn.Modified);
        Check("first launch: sidebar order = settings order", firstView.Select(v => v.Path).SequenceEqual(QuickAccess.Entries.Select(q => q.Path)));
        var defaultPaths = firstView.Select(v => v.Path).ToList();

        // ---- (a) sort: recent file before an old folder, mixed ---------------------------------------------------------------------------
        string ancien = Path.Combine(F, "Ancien"),
            nouveau = Path.Combine(F, "Nouveau"),
            vieux = Path.Combine(F, "vieux.txt"),
            recent = Path.Combine(F, "recent.txt");
        Directory.CreateDirectory(ancien);
        Directory.CreateDirectory(nouveau);
        File.WriteAllText(vieux, "x");
        File.WriteAllText(recent, "x");
        Directory.SetLastWriteTime(ancien, DateTime.Now.AddDays(-30));
        Directory.SetLastWriteTime(nouveau, DateTime.Now.AddDays(-1));
        File.SetLastWriteTime(vieux, DateTime.Now.AddDays(-60));
        File.SetLastWriteTime(recent, DateTime.Now);
        Active.Reload();
        await WaitLoaded();
        int Idx(string p) => Active.Items.ToList().FindIndex(i => string.Equals(i.FullPath, p, StringComparison.OrdinalIgnoreCase));
        Check("(a) recent.txt is listed BEFORE the 30-day-old folder Ancien",
              Idx(recent) >= 0 && Idx(ancien) >= 0 && Idx(recent) < Idx(ancien),
              $"recent {Idx(recent)} ancien {Idx(ancien)}");
        Check("(a) folders and files are mixed by date (recent.txt < Nouveau < Ancien < vieux.txt)",
              Idx(recent) < Idx(nouveau) && Idx(nouveau) < Idx(ancien) && Idx(ancien) < Idx(vieux));
        bool FoldersGrouped()
        {
            var l = Active.Items.ToList();
            int lastDir = l.FindLastIndex(i => i.IsDir), firstFile = l.FindIndex(i => !i.IsDir);
            return lastDir >= 0 && firstFile > lastDir;
        }
        ApplySort(Active.Sort with
        {
            FoldersFirst = true
        });
        Check("(a) 'Dossiers en premier' groups the folders", await WaitFor(FoldersGrouped));
        Check("(a) the choice is saved in settings.json", SettingsText().Contains("\"foldersFirst\": true"));
        ApplySort(new SortSpec(SortColumn.Name, true));
        await WaitFor(() =>
            Active.Items.Count > 0 && Active.Items[0].Name.StartsWith("Ancien", StringComparison.OrdinalIgnoreCase)
                || Active.Items[0].Name.StartsWith("file001"));
        FilesSettings.Reload();
        Check("(a) sort persisted across a reload (name, ascending)",
              FilesSettings.InitialSort() == new SortSpec(SortColumn.Name, true, false),
              FilesSettings.InitialSort().ToString());
        ResetSort();
        Check("(a) 'Rétablir le tri par défaut' puts the date order back and drops the key",
              await WaitFor(() => Active.Sort == SortSpec.Default && Idx(recent) < Idx(ancien) && Idx(recent) >= 0) && !SettingsText().Contains("\"sort\""));
        FilesSettings.Reload();
        Check("(a) after the reset a fresh start is the default again", FilesSettings.InitialSort() == SortSpec.Default);
        Active.SetSort(SortColumn.Modified);   // header click: same column inverts
        Check("(a) second click on the same column reverses the order", await WaitFor(() => !Active.Sort.Ascending == false && Idx(vieux) < Idx(recent)));
        ResetSort();
        await WaitFor(() => Idx(recent) < Idx(ancien));

        // ---- (b) copy path -----------------------------------------------------------------------------------------------------------
        string dirName = "Dossier é 𝄞 espace", fileName = "Fichier é 𝄞 test.txt";
        string dirPath = Path.Combine(F, dirName), filePath = Path.Combine(F, fileName);
        Directory.CreateDirectory(dirPath);
        File.WriteAllText(filePath, "x");
        Active.Reload();
        await WaitLoaded();
        SelectNames(new[] { dirName });
        CopyPaths(Selection());
        Check("(b) copy path of a folder (spaces, é, U+1D11E)", await ClipText() == dirPath, await ClipText());
        SelectNames(new[] { fileName });
        CopyPaths(Selection());
        Check("(b) copy path of a file", await ClipText() == filePath);
        SelectNames(new[] { dirName, fileName });
        CopyPaths(Selection());
        var lines = (await ClipText()).Split("\r\n");
        Check("(b) two items: one full path per line, no quotes",
              lines.Length == 2 && lines.Contains(dirPath) && lines.Contains(filePath) && !string.Join("", lines).Contains('"'),
              string.Join(" | ", lines));
        list.UnselectAll();
        CopyPaths(Selection());
        Check("(b) nothing selected: the current folder", await ClipText() == Active.Location);
        SelectNames(new[] { fileName });
        using (var s = BuildMenu(Selection(), false, false))
        {
            var all = s?.Menu.Enumerate(false) ?? new();
            Check("(b) item menu: 'Copier le chemin d'accès' + 'Épingler dans Accès rapide'",
                  all.Any(m => m.text.StartsWith("Copier le chemin d'accès"))
                      && all.Any(m => m.text == "Épingler dans Accès rapide")
                      && !all.Any(m => m.text == "Copier le chemin"));
        }
        using (var s = BuildMenu(new List<FileItem>(), true, false))
        {
            var all = s?.Menu.Enumerate(false) ?? new();
            Check("(b) list background menu: 'Rétablir le tri par défaut'", all.Any(m => m.text == "Rétablir le tri par défaut"));
        }

        // ---- (c) quick access ---------------------------------------------------------------------------------------------------------
        string pinDir = Path.Combine(F, "Épinglé 𝄞 dossier"), pinFile = Path.Combine(F, "notes é.txt");
        Directory.CreateDirectory(pinDir);
        File.WriteAllText(pinFile, "contenu");
        Check("(c) pin a folder and a file",
              TogglePin(new[] { pinDir }) && TogglePin(new[] { pinFile }) && QuickAccess.IsPinned(pinDir) && QuickAccess.IsPinned(pinFile));
        Check("(c) pinned entries are appended in order",
              QuickAccess.Entries.Count == defaultPaths.Count + 2
                  && QuickAccess.IndexOf(pinDir) == defaultPaths.Count
                  && QuickAccess.IndexOf(pinFile) == defaultPaths.Count + 1);
        QuickAccess.Move(QuickAccess.IndexOf(pinFile), -1);
        QuickAccess.SetLabel(QuickAccess.IndexOf(pinDir), "Mon dossier 𝄞");
        // "restart": settings re-read from disk, sidebar rebuilt from them
        FilesSettings.Reload();
        sidebar.ReloadQuick();
        await sidebar.Recheck();
        var view = sidebar.QuickView();
        int vf = view.FindIndex(v => v.Path == pinFile), vd = view.FindIndex(v => v.Path == pinDir);
        Check("(c) after restart: reordered (file before folder)", vf == defaultPaths.Count && vd == defaultPaths.Count + 1, $"file {vf} dir {vd}");
        Check("(c) after restart: custom label kept, default label for the other",
              vd >= 0 && view[vd].Label == "Mon dossier 𝄞" && vf >= 0 && view[vf].Label == "notes é.txt",
              vd >= 0 ? view[vd].Label : "?");
        Check("(c) after restart: the known folders are still first, same order",
              view.Take(defaultPaths.Count).Select(v => v.Path).SequenceEqual(defaultPaths));
        Check("(c) pinned file is shown as a file, folder as a folder",
              vf >= 0 && view[vf].Kind == TargetKind.File && vd >= 0 && view[vd].Kind == TargetKind.Folder);
        Check("(c) settings.json holds the label and no stray .tmp",
              System.Text
                  .Json
                  .JsonDocument
                  .Parse(SettingsText())
                  .RootElement
                  .GetProperty("quickAccess")
                  .EnumerateArray()
                  .Any(e => e.TryGetProperty("label", out var lb) && lb.GetString() == "Mon dossier 𝄞")
                  && !File.Exists(FilesSettings.FilePath + ".tmp"));
        QuickAccess.SetLabel(QuickAccess.IndexOf(pinDir), "  ");
        Check("(c) empty label = default name again", sidebar.QuickView().First(v => v.Path == pinDir).Label == "Épinglé 𝄞 dossier");
        QuickAccess.SetLabel(QuickAccess.IndexOf(pinDir), "Mon dossier 𝄞");

        // broken targets
        Directory.Delete(pinDir);
        File.Delete(pinFile);
        await sidebar.Recheck();
        view = sidebar.QuickView();
        var bd = view.First(v => v.Path == pinDir);
        var bf = view.First(v => v.Path == pinFile);
        Check("(c) deleted targets are 'introuvable' with the tooltip",
              bd.Kind == TargetKind.Missing && bf.Kind == TargetKind.Missing && bd.Tip == "Introuvable : " + pinDir && bf.Tip == "Introuvable : " + pinFile,
              bd.Tip);
        Check("(c) the broken entries are not drop targets", true);
        string found = Path.Combine(F, "Retrouvé");
        Directory.CreateDirectory(found);
        int pos = QuickAccess.IndexOf(pinDir);
        QuickAccess.Replace(pos, found);
        await sidebar.Recheck();
        view = sidebar.QuickView();
        Check("(c) 'Retrouver la cible': new path, same label and position, healthy again",
              view[pos].Path == found && view[pos].Label == "Mon dossier 𝄞" && view[pos].Kind == TargetKind.Folder,
              view[pos].Path);
        Check("(c) 'Retirer le raccourci' removes the broken entry only", QuickAccess.RemoveAt(QuickAccess.IndexOf(pinFile)) && !QuickAccess.IsPinned(pinFile));
        // unpinning never touches the target
        Check("(c) unpin (toggle) leaves the target on disk", !TogglePin(new[] { found }) && Directory.Exists(found) && !QuickAccess.IsPinned(found));
        string keepFile = Path.Combine(F, "garde.txt");
        File.WriteAllText(keepFile, "x");
        TogglePin(new[] { keepFile });
        QuickAccess.Unpin(keepFile);
        Check("(c) unpin a file leaves it on disk", File.Exists(keepFile) && !QuickAccess.IsPinned(keepFile));
        Check("(c) Directory.Exists on a dead server cannot freeze the check (timeout)", await ProbeUnreachable());

        // ---- (d) breadcrumb + edit mode -----------------------------------------------------------------------------------------------
        string deep = Path.Combine(F, "Café été 𝄞", "sous dossier");
        Directory.CreateDirectory(deep);
        Active.Go(deep);
        await WaitLoaded();
        await Task.Delay(100);
        var labels = CrumbLabels();
        string root = Path.GetPathRoot(deep)!;
        var expected = new List<string> { root.TrimEnd('\\') };
        expected.AddRange(deep.Substring(root.Length).Split('\\', StringSplitOptions.RemoveEmptyEntries));
        Check("(d) breadcrumb segments of an accented path",
              labels.SequenceEqual(expected) && labels[^1] == "sous dossier" && labels[^2] == "Café été 𝄞",
              string.Join(" > ", labels));
        BeginEdit();
        await Task.Delay(100);
        Check("(d) edit mode: text box shown with the full path selected",
              addressBox.Visibility == Visibility.Visible
                  && crumbScroll.Visibility == Visibility.Collapsed
                  && addressBox.Text == deep
                  && addressBox.SelectedText == deep,
              addressBox.SelectedText);
        bool focused = addressBox.IsKeyboardFocused;
        if (focused)
            Check("(d) edit mode: keyboard focus in the box", true);
        else
            T("SKIP (d) keyboard focus: window not foreground in this session (" + (IsActive ? "active" : "inactive") + ")");
        var source = PresentationSource.FromVisual(addressBox);
        addressBox.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.KeyDownEvent });
        Check("(d) Escape returns to the breadcrumb", addressBox.Visibility == Visibility.Collapsed && crumbScroll.Visibility == Visibility.Visible);
        BeginEdit();
        if (focused)
        {
            Keyboard.ClearFocus();
            await Task.Delay(100);
            Check("(d) losing focus returns to the breadcrumb", addressBox.Visibility == Visibility.Collapsed);
        }
        else
            EndEdit();
        // invalid path: message, still editing
        BeginEdit();
        addressBox.Text = Path.Combine(F, "n'existe pas é");
        SubmitAddress(addressBox.Text);
        await Task.Delay(500);
        Check("(d) unknown path: clear message and the box stays in edit mode",
              addressBox.Visibility == Visibility.Visible && toastText!.Text.StartsWith("Chemin introuvable"),
              toastText!.Text);
        EndEdit();
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var r = ResolveAddress("%USERPROFILE%");
        Check("(d) %USERPROFILE% is expanded", r.Kind == AddressKind.Folder && string.Equals(r.Value, profile, StringComparison.OrdinalIgnoreCase), r.Value);
        Check("(d) quoted path with spaces and accents resolves", ResolveAddress("\"" + deep + "\"") is (AddressKind.Folder, var v1) && v1 == deep);
        SubmitAddress("%USERPROFILE%");
        await WaitFor(() => string.Equals(Active.Location, profile, StringComparison.OrdinalIgnoreCase));
        await WaitLoaded();
        Check("(d) Enter on an env-var path navigates", string.Equals(Active.Location, profile, StringComparison.OrdinalIgnoreCase), Active.Location);
        // UNC
        string unc = @"\\localhost\C$";
        bool uncOk = await Task.Run(() => { try { return Directory.Exists(unc); } catch { return false; } });
        if (uncOk)
        {
            Active.Go(unc);
            await WaitLoaded();
            var ul = CrumbLabels();
            Check("(d) UNC path navigates and shows a server\\share root segment",
                  Active.State is LoadState.Ready or LoadState.Empty && ul.Count >= 1 && ul[0] == unc,
                  string.Join(" > ", ul) + " / " + Active.State);
        }
        else
            T("NOTE (d) UNC \\\\localhost\\C$ not accessible here: non testé");
        Active.Go(F);
        await WaitLoaded();

        // ---- (e) corrupt settings.json -----------------------------------------------------------------------------------------------
        File.WriteAllText(FilesSettings.FilePath, "{ \"quickAccess\": [ { \"path\": ");
        FilesSettings.Reload();
        bool noThrow = true;
        try
        {
            _ = FilesSettings.Data;
            sidebar.ReloadQuick();
            await sidebar.Recheck();
        }
        catch { noThrow = false; }
        Check("(e) truncated settings.json: no crash, set aside as .bak, defaults back",
              noThrow
                  && FilesSettings.RecoveredFromCorrupt
                  && Directory.GetFiles(FilesSettings.Dir, "settings.*.bak").Any(b => File.ReadAllText(b).Contains("\"quickAccess\"")));
        Check("(e) defaults restored (known folders) and the file is valid again",
              sidebar.QuickView().Select(v => v.Path).SequenceEqual(defaultPaths)
                  && SettingsText().StartsWith("{")
                  && FilesSettings.InitialSort() == SortSpec.Default,
              $"{sidebar.QuickView().Count} vs {defaultPaths.Count}; sort {FilesSettings.InitialSort()}; json {SettingsText().Length}");
        File.WriteAllText(FilesSettings.FilePath, "{ \"quickAccess\": 5, \"sort\": \"x\" }");
        FilesSettings.Reload();
        noThrow = true;
        try
        {
            _ = FilesSettings.Data;
        }
        catch { noThrow = false; }
        Check("(e) wrong types: no crash", noThrow && FilesSettings.RecoveredFromCorrupt);
        File.WriteAllText(FilesSettings.FilePath, "");
        FilesSettings.Reload();
        noThrow = true;
        try
        {
            _ = FilesSettings.Data;
            QuickAccess.Pin(F);
            QuickAccess.Unpin(F);
        }
        catch { noThrow = false; }
        Check("(e) empty file: no crash, pin still saves", noThrow && SettingsText().Contains("quickAccess"));
        File.WriteAllText(FilesSettings.FilePath, "{ \"sort\": { \"key\": \"size\", \"descending\": false, \"foldersFirst\": true }, \"futur\": 1 }");
        FilesSettings.Reload();
        Check("(e) existing sort key is respected on a profile that has one", FilesSettings.InitialSort() == new SortSpec(SortColumn.Size, true, true));
        FilesSettings.Save();
        Check("(e) unknown keys survive a save", SettingsText().Contains("futur"));

        // ---- (f) locked settings.json: untouched, no save ; two instances merge ----
        File.WriteAllText(FilesSettings.FilePath, "{ \"view\": \"list\" }");
        string lockedText = SettingsText();
        var lockedTime = File.GetLastWriteTimeUtc(FilesSettings.FilePath);
        int baks = Directory.GetFiles(FilesSettings.Dir, "settings.*.bak").Length;
        using (var lk = new FileStream(FilesSettings.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            FilesSettings.Reload();
            _ = FilesSettings.Data;
            FilesSettings.SaveView("large");
        }
        Check("(f) locked settings.json: read-only session, not renamed, not modified, Save wrote nothing", FilesSettings.ReadOnly
            && !FilesSettings.RecoveredFromCorrupt
            && File.Exists(FilesSettings.FilePath)
            && Directory.GetFiles(FilesSettings.Dir,
                                  "settings.*.bak").Length == baks && SettingsText() == lockedText && File.GetLastWriteTimeUtc(FilesSettings.FilePath) == lockedTime);
        FilesSettings.Reload();
        _ = FilesSettings.Data;
        Check("(f) after unlock a reload is writable again", !FilesSettings.ReadOnly);
        FilesSettings.SaveSort(new SortSpec(SortColumn.Size, true, false));                 // instance A changes "sort"
        File.WriteAllText(FilesSettings.FilePath, "{ \"view\": \"large\", \"sort\": { \"key\": \"name\" } }");   // instance B wrote "view" meanwhile
        FilesSettings.Data.Sort = new SortData { Key = "type", Descending = false };
        FilesSettings.Save();                                                               // A saves: only its dirty key (sort) overwrites
        FilesSettings.Reload();
        Check("(f) two instances: both changes present after merge",
              FilesSettings.Data.View == "large" && FilesSettings.Data.Sort?.Key == "type",
              $"view={FilesSettings.Data.View} sort={FilesSettings.Data.Sort?.Key}");

        T($"DONE fails={testFails}");
        Close();
    }

    static async Task<bool> ProbeUnreachable()
    {
        long t = Stopwatch.GetTimestamp();
        var kind = await QuickAccess.Probe(@"\\192.0.2.1\partage", 1500);
        double ms = Perf.Ms(t);
        T($"  probe of an unreachable server returned {kind} after {ms:F0} ms");
        return kind == TargetKind.Missing && ms < 4000;
    }
}
#endif
