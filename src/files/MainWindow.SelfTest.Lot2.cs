#if WINLESTIA_SELFTEST
// WINLESTIA_FILES_SELFTEST=lot2 : journal d'annulation, raccourcis (HandleKey), propriétés / ouvrir avec (niveau modèle), recherche
// profonde, vues, sélection (touches + rectangle),
// lecteurs (changement de périphérique), « Ce PC », modèle du menu contextuel, AUMID, crash.log. Exige WINLESTIA_FILES_DATA_DIR sous
// %TEMP% et un dossier de fixtures sous %TEMP%\WinlestiaFilesOps.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace WinlestiaFiles;

public sealed partial class MainWindow
{
    async Task Lot2Test()
    {
        string F0 = Fixture();
        string tmp = Path.GetFullPath(Path.GetTempPath());
        if (!Path.GetFullPath(FilesSettings.Dir).StartsWith(tmp, StringComparison.OrdinalIgnoreCase)
            || Environment.GetEnvironmentVariable("WINLESTIA_FILES_DATA_DIR") is not { Length: > 0 })
        {
            T("ABORT data dir is not a %TEMP% folder");
            Close();
            return;
        }
        T("START lot2 fixture " + F0);
        Activate();
        PropertiesQuiet = true;
        FileOps.Quiet = true;
        await WaitLoaded();
        await sidebar.Ready;
        string F = Path.Combine(F0, "L2");
        Directory.CreateDirectory(F);
        string sub = Path.Combine(F, "sub"), deepDir = Path.Combine(F, "sub", "profond"), dest = Path.Combine(F, "dest");
        Directory.CreateDirectory(deepDir);
        Directory.CreateDirectory(dest);
        File.WriteAllText(Path.Combine(F, "a.txt"), "A");
        File.WriteAllText(Path.Combine(F, "b.txt"), "B");
        File.WriteAllText(Path.Combine(F, "c.log"), "C");
        File.WriteAllText(Path.Combine(sub, "rapport-x.txt"), "1");
        File.WriteAllText(Path.Combine(deepDir, "rapport-y.txt"), "2");
        Active.Go(F);
        await WaitLoaded();
        FileItem? Item(string name) => Active.Items.FirstOrDefault(i => i.Name == name);
        string P(string name) => Path.Combine(F, name);
        void Press(Key k, ModifierKeys m = ModifierKeys.None) => HandleKey(k, m);

        // ---- 1. undo journal ----
        Check("(1) journal starts empty", journal.UndoCount == 0 && journal.NextUndo == null);
        list.Select(new[] { Item("a.txt")! });
        BeginRename(Item("a.txt")!);
        renameBox.Text = "a2.txt";
        await CommitRename();
        Check("(1) rename recorded as 'Annuler ...'",
              journal.NextUndo is { } u1 && u1.Label.Length > 0 && File.Exists(P("a2.txt")),
              journal.NextUndo?.Label ?? "none");
        Check("(1) background menu shows the label and Ctrl+Z",
              ContextItems(CtxKind.Background, new List<FileItem>()).Any(i =>
                  i != null && i.Text.StartsWith("Annuler " + journal.NextUndo!.Label) && i.Text.EndsWith("Ctrl+Z")));
        Press(Key.Z, ModifierKeys.Control);
        Check("(1) Ctrl+Z undoes the rename", await WaitFor(() => File.Exists(P("a.txt")) && !File.Exists(P("a2.txt"))
            && journal.RedoCount == 1));
        Press(Key.Y, ModifierKeys.Control);
        Check("(1) Ctrl+Y redoes it", await WaitFor(() => File.Exists(P("a2.txt")) && !File.Exists(P("a.txt")) && journal.RedoCount == 0));
        Press(Key.Z, ModifierKeys.Control);
        await WaitFor(() => File.Exists(P("a.txt")) && journal.RedoCount == 1);
        Press(Key.Z, ModifierKeys.Control | ModifierKeys.Shift);
        Check("(1) Ctrl+Maj+Z also redoes", await WaitFor(() => File.Exists(P("a2.txt")) && journal.RedoCount == 0));
        Press(Key.Z, ModifierKeys.Control);
        await WaitFor(() => File.Exists(P("a.txt")) && journal.RedoCount == 1);
        await WaitLoaded();

        var nf = await NewFolderAsync();
        string nfPath = nf?.FullPath ?? "";
        Check("(1) new folder created (rename box open)", nf != null && Directory.Exists(nfPath));
        if (renaming != null)
            CancelRename();
        string um = await UndoAsync();
        Check("(1) undo of the new folder removes it", await WaitFor(() => !Directory.Exists(nfPath)), um);
        await RedoAsync();
        Check("(1) redo recreates it", await WaitFor(() => Directory.Exists(nfPath)));
        await UndoAsync();
        await WaitFor(() => !Directory.Exists(nfPath));

        bool cb = await SetClipboardAsync(new[] { P("b.txt") }, true);
        var pr = await PasteAsync(dest);
        Check("(1) move executed",
              await WaitFor(() => File.Exists(Path.Combine(dest, "b.txt")) && !File.Exists(P("b.txt"))),
              $"clip={cb} result={(pr == null ? "null" : pr.Error ?? (pr.Aborted ? "aborted" : "ok " + pr.Log.Count))}");
        await UndoAsync();
        Check("(1) undo of the move brings the file back", await WaitFor(() => File.Exists(P("b.txt")) && !File.Exists(Path.Combine(dest,
            "b.txt"))));
        await RedoAsync();
        Check("(1) redo of the move", await WaitFor(() => File.Exists(Path.Combine(dest, "b.txt"))));
        await UndoAsync();
        await WaitFor(() => File.Exists(P("b.txt")));

        await SetClipboardAsync(new[] { P("c.log") }, false);
        await PasteAsync(dest);
        Check("(1) copy executed", await WaitFor(() => File.Exists(Path.Combine(dest, "c.log"))));
        await UndoAsync();
        Check("(1) undo of the copy removes only the copy", await WaitFor(() => !File.Exists(Path.Combine(dest, "c.log"))
            && File.Exists(P("c.log"))));

        string del = P("corbeille-" + Guid.NewGuid().ToString("N")[..6] + ".txt");
        File.WriteAllText(del, "Z");
        Active.Reload();
        await WaitLoaded();
        var delItem = Active.Find(del);
        var dr = delItem == null ? null : await DeleteAsync(new[] { delItem }, false);
        Check("(1) delete went to the Recycle Bin", dr != null && await WaitFor(() => !File.Exists(del)));
        string back = await UndoAsync();
        bool restored = await WaitFor(() => File.Exists(del), 8000);
        Check("(1) undo of the delete restores from the Recycle Bin", restored && File.ReadAllText(del) == "Z", back);
        if (restored)
        {
            Safe.Try(() => File.Delete(del));
        }

        journal.RecordPermanentDelete();
        string msg = await UndoAsync();
        Check("(1) permanent delete: undo says it is impossible", msg.Contains("Impossible"), msg);

        // ---- 1b. undo refusals ----
        var nf2 = await NewFolderAsync();
        if (renaming != null)
            CancelRename();
        if (nf2 != null)
            File.WriteAllText(Path.Combine(nf2.FullPath, "dedans.txt"), "x");
        string r1 = await UndoAsync();
        Check("(1b) undo of a new folder refuses when it is no longer empty",
              nf2 != null && Directory.Exists(nf2.FullPath) && r1.Contains("contient maintenant des éléments"),
              r1);
        if (nf2 != null)
        {
            Safe.Try(() => Directory.Delete(nf2.FullPath, true));
        }

        await SetClipboardAsync(new[] { P("c.log") }, false);
        await PasteAsync(dest);
        string copied = Path.Combine(dest, "c.log");
        await WaitFor(() => File.Exists(copied));
        File.SetLastWriteTimeUtc(copied, DateTime.UtcNow.AddHours(1));
        string r2 = await UndoAsync();
        Check("(1b) undo of a copy refuses when the copy was modified", File.Exists(copied) && r2.Contains("modifié"), r2);
        Safe.Try(() => File.Delete(copied));

        File.WriteAllText(P("m1.txt"), "M");
        await SetClipboardAsync(new[] { P("m1.txt") }, true);
        await PasteAsync(dest);
        Check("(1b) move executed", await WaitFor(() => File.Exists(Path.Combine(dest, "m1.txt")) && !File.Exists(P("m1.txt"))));
        File.WriteAllText(P("m1.txt"), "autre");
        string r3 = await UndoAsync();
        Check("(1b) undo of a move refuses when the origin name is taken",
              File.Exists(Path.Combine(dest, "m1.txt")) && File.ReadAllText(P("m1.txt")) == "autre" && r3.Contains("existe déjà"),
              r3);
        Safe.Try(() =>
        {
            File.Delete(P("m1.txt"));
            File.Delete(Path.Combine(dest, "m1.txt"));
        
        });

        // ---- 2. propriétés / ouvrir avec ----
        Active.Reload();
        await WaitLoaded();
        await Task.Delay(PauseAnimationLongueMs);
        list.Select(new[] { Item("a.txt")! });
        Press(Key.Return, ModifierKeys.Alt);
        Check("(2) Alt+Enter = properties of the selected item", LastProperties == "single:" + P("a.txt"), LastProperties);
        list.Select(new[] { Item("a.txt")!, Item("b.txt")! });
        Press(Key.Return, ModifierKeys.Alt);
        Check("(2) Alt+Enter on 2 items = multi-file properties", LastProperties == "multi:2", LastProperties);
        list.UnselectAll();
        Press(Key.Return, ModifierKeys.Alt);
        Check("(2) nothing selected = current folder properties", LastProperties == "single:" + F, LastProperties);
        OpenWith(P("a.txt"));
        Check("(2) 'Ouvrir avec...' reaches SHOpenWithDialog (quiet)", LastOpenWith.EndsWith("a.txt"));

        // ---- 3. search ----
        Press(Key.E, ModifierKeys.Control);
        Check("(3) Ctrl+E opens the search box", await WaitFor(() => filterBox.IsVisible));
        ClearFilter(true);
        Press(Key.F, ModifierKeys.Control);
        Check("(3) Ctrl+F opens the search box", await WaitFor(() => filterBox.IsVisible));
        Active.SetFilter("txt");
        await Task.Delay(PauseRenduMs);
        Check("(3) instant filter keeps matching names of this folder",
              Active.Items.Count > 0 && Active.Items.All(i => i.Name.Contains("txt", StringComparison.OrdinalIgnoreCase))
                  && !Active.DeepActive);
        Active.SetDeepOption(true);
        Check("(3) sub-folder search finds rapport-y.txt (2 levels down)",
              await WaitFor(() => Active.DeepActive && Active.Items.Any(i => i.Name == "rapport-y.txt")));
        Check("(3) 'Emplacement' filled",
              Active.Items.FirstOrDefault(i => i.Name == "rapport-y.txt")?.Where.EndsWith("profond") == true,
              Active.Items.FirstOrDefault(i => i.Name == "rapport-y.txt")?.Where ?? "");
        await WaitFor(() => !Active.Searching);
        Press(Key.Escape);
        Check("(3) Esc clears the search", await WaitFor(() => Active.Filter.Length == 0 && !Active.Searching));
        string longDir = F;
        for (int i = 0; i < 6; i++)
            longDir = Path.Combine(longDir, new string('l', 50));
        try
        {
            Directory.CreateDirectory(@"\\?\" + longDir);
            File.WriteAllText(@"\\?\" + Path.Combine(longDir, "tresloin.txt"), "x");
        }
        catch (Exception ex) { T("  long path create failed: " + ex.Message); }
        Active.SetDeepOption(true);
        Active.SetFilter("tresloin");
        Check("(3) long path (>260) result found", await WaitFor(() => Active.Items.Any(i => i.Name == "tresloin.txt"), 8000));
        Active.Go(dest);
        await WaitLoaded();
        Check("(3) folder change stops the search", !Active.Searching && !Active.DeepActive,
            $"searching={Active.Searching} deep={Active.DeepActive}");
        Active.SetDeepOption(false);
        Active.Go(F);
        await WaitLoaded();
        Safe.Try(() => Directory.Delete(@"\\?\" + Path.Combine(F, new string('l', 50)), true));

        // ---- 4. views ----
        Press(Key.D2, ModifierKeys.Control | ModifierKeys.Shift);
        Check("(4) Ctrl+Maj+2 = large icons", CurrentView == ViewMode.Large);
        Press(Key.D3, ModifierKeys.Control | ModifierKeys.Shift);
        Check("(4) Ctrl+Maj+3 = medium icons", CurrentView == ViewMode.Medium);
        Press(Key.D5, ModifierKeys.Control | ModifierKeys.Shift);
        Check("(4) Ctrl+Maj+5 = list", CurrentView == ViewMode.List);
        Check("(4) view saved in settings.json under 'view'", SettingsText().Contains("\"view\": \"list\""));
        Press(Key.D6, ModifierKeys.Control | ModifierKeys.Shift);
        Check("(4) Ctrl+Maj+6 = details", CurrentView == ViewMode.Details);
        FilesSettings.Reload();
        Check("(4) persisted view read back", FilesSettings.InitialView() == "details", FilesSettings.InitialView());
        var fi = Item("a.txt")!;
        var di = Active.Items.FirstOrDefault(i => i.IsDir)!;
        Check("(4) folder size empty, file size in Ko/Mo",
              string.IsNullOrEmpty(di.SizeText) && fi.SizeText.Length > 0,
              $"dir '{di.SizeText}' file '{fi.SizeText}'");
        double w0 = ColumnLayout.Instance.ModW.Value;
        ColumnLayout.Instance.Resize("mod", 30);
        Check("(4) column resize changes the layout width", ColumnLayout.Instance.ModW.Value != w0,
            $"{w0} -> {ColumnLayout.Instance.ModW.Value}");
        ColumnLayout.Instance.Resize("mod", -30);
        ApplySort(Active.Sort with
        {
            Column = SortColumn.Name,
            Ascending = true
        });
        await WaitFor(() => chevrons[0].Visibility == Visibility.Visible, 2000);
        Check("(4) header click sort = persisted sort (name) with a chevron",
              Active.Sort.Column == SortColumn.Name && chevrons[0].Visibility == Visibility.Visible && SettingsText().Contains("\"sort\""),
              Active.Sort.ToString());
        ResetSort();

        // ---- 5. selection ----
        await WaitLoaded();
        await Task.Delay(PauseRenduMs);
        Press(Key.A, ModifierKeys.Control);
        Check("(5) Ctrl+A selects everything", list.SelectedItems.Count == Active.Items.Count && Active.Items.Count > 0);
        Press(Key.Escape);
        Check("(5) Esc deselects", list.SelectedItems.Count == 0);
        list.Select(new[] { Active.Items[0] });
        Press(Key.Space, ModifierKeys.Control);
        Check("(5) Ctrl+Space toggles the focused item", list.SelectedItems.Count == 0);
        Press(Key.Space, ModifierKeys.Control);
        Check("(5) Ctrl+Space toggles it back", list.SelectedItems.Count == 1);
        SetView(ViewMode.Details);
        await Task.Delay(PauseAnimationMs);
        list.UpdateLayout();
        list.UnselectAll();
        int n = Active.Items.Count;
        RubberStart(new Point(300, 32 * 4 + 20), false);
        RubberMove(new Point(120, 32 * 1 + 5));
        RubberEnd();
        Check("(5) rectangle (details) selects rows 1..4",
              list.SelectedItems.Count == Math.Min(4, n - 1) && list.SelectedItems.Contains(Active.Items[1]),
              $"{list.SelectedItems.Count}");
        int before = list.SelectedItems.Count;
        RubberStart(new Point(300, 32 * 6 + 5), true);
        RubberMove(new Point(120, 32 * 6 + 20));
        RubberEnd();
        Check("(5) Ctrl+rectangle adds to the selection",
              list.SelectedItems.Count >= before && (n <= 6 || list.SelectedItems.Contains(Active.Items[6])),
              $"{before} -> {list.SelectedItems.Count}");
        RubberStart(new Point(300, 700), false);
        RubberEnd();
        Check("(5) plain click on empty space deselects", list.SelectedItems.Count == 0);
        foreach (var vm in new[] { ViewMode.Large, ViewMode.Medium, ViewMode.List })
        {
            SetView(vm);
            await Task.Delay(PauseAnimationLongueMs);
            list.UpdateLayout();
            list.UnselectAll();
            var c = Cell;
            RubberStart(new Point(2, 2), false);
            RubberMove(new Point(c.W * 1.5, c.H * 0.5));
            RubberEnd();
            Check($"(5) rectangle in {vm} selects the first 2 cells", list.SelectedItems.Count == Math.Min(2, n) && n > 0,
                $"{list.SelectedItems.Count}");
        }
        SetView(ViewMode.Details);
        list.UnselectAll();

        // ---- 6. drives / Ce PC ----
        string pinDir = Path.Combine(F, "epingle");
        Directory.CreateDirectory(pinDir);
        QuickAccess.Pin(pinDir);
        await sidebar.Recheck();
        Check("(6) pinned folder present", sidebar.QuickView().Any(v => v.Path == pinDir && v.Kind != TargetKind.Missing));
        Directory.Delete(pinDir);
        var fake =
            new List<(string Root, string Label, long Free, long Total)> { (@"Q:\", "Faux", 50L << 30, 100L << 30), (@"R:\", "Plein",
                2L << 30, 100L << 30) };
        Sidebar.DriveSource = () => fake;
        int rebuilt = sidebar.DrivesRebuilt;
        bool handled = false;
        DeviceHook(IntPtr.Zero, 0x0219, (IntPtr)0x8000, IntPtr.Zero, ref handled);
        Check("(6) WM_DEVICECHANGE rebuilds the drive list",
              await WaitFor(() => sidebar.DrivesRebuilt > rebuilt && sidebar.DriveView().Any(d => d.Path.StartsWith("Q:")), 4000));
        Check("(6) not-found state of the pin re-evaluated at the same time",
              await WaitFor(() => sidebar.QuickView().Any(v => v.Path == pinDir && v.Kind == TargetKind.Missing), 4000));
        Directory.CreateDirectory(pinDir);
        DeviceHook(IntPtr.Zero, 0x0219, (IntPtr)0x0007, IntPtr.Zero, ref handled);
        Check("(6) ... back to present when the folder returns",
              await WaitFor(() => sidebar.QuickView().Any(v => v.Path == pinDir && v.Kind == TargetKind.Folder), 4000));
        fake.RemoveAt(0);
        rebuilt = sidebar.DrivesRebuilt;
        DeviceHook(IntPtr.Zero, 0x0219, (IntPtr)0x8004, IntPtr.Zero, ref handled);
        Check("(6) drive removal updates the list",
              await WaitFor(() => sidebar.DrivesRebuilt > rebuilt && !sidebar.DriveView().Any(d => d.Path.StartsWith("Q:")), 4000));
        QuickAccess.Unpin(pinDir);
        Safe.Try(() => Directory.Delete(pinDir));
        fake.Insert(0, (@"Q:\", "Faux", 50L << 30, 100L << 30));
        Active.Go(ThisPc.Path);
        await WaitFor(() => thisPcWrap != null && ThisPcCards.Count > 0, 4000);
        Check("(6) 'Ce PC' cards: Error bar above 90 % only",
              ThisPcCards.Any(c => c.Root == @"R:\" && c.Alert) && ThisPcCards.Any(c => c.Root == @"Q:\" && !c.Alert),
              string.Join(",", ThisPcCards.Select(c => $"{c.Root}{c.Used:P0}")));
        Check("(6) 'Ce PC' title, crumb and page",
              Active.Title == "Ce PC" && CrumbLabels().SequenceEqual(new[] { "Ce PC" }) && ReferenceEquals(content.Child, thisPcPage),
              Active.Title + "|" + string.Join(",", CrumbLabels()));
        Check("(6) address 'Ce PC' resolves", ResolveAddress("Ce PC") is (AddressKind.Folder, var pcv) && pcv == ThisPc.Path);
        Sidebar.DriveSource = null;
        Active.Go(F);
        await WaitLoaded();

        // ---- 7. context menu ----
        var sel1f = new List<FileItem> { Item("a.txt")! };
        var sel1d = new List<FileItem> { di };
        var selN = new List<FileItem> { Item("a.txt")!, Item("c.log")! };
        Check("(7) kinds",
              KindOf(sel1f, false) == CtxKind.File
                  && KindOf(sel1d, false) == CtxKind.Folder
                  && KindOf(selN, false) == CtxKind.Multi
                  && KindOf(sel1f, true) == CtxKind.Background);
        bool Has(List<NativeMenu.Item?> l, string t) => l.Any(i => i != null && i.Text.StartsWith(t));
        var mF = ContextItems(CtxKind.File, sel1f);
        var mD = ContextItems(CtxKind.Folder, sel1d);
        var mM = ContextItems(CtxKind.Multi, selN);
        var mB = ContextItems(CtxKind.Background, new List<FileItem>());
        var mV = ContextItems(CtxKind.Drive, new List<FileItem>(), @"C:\");
        Check("(7) file: 'Ouvrir avec', shortcut on the right, native menu entry",
              Has(mF, "Ouvrir avec") && Has(mF, "Copier\tCtrl+C") && Has(mF, "Afficher plus d'options\tMaj+F10"));
        Check("(7) folder: new tab, no 'Ouvrir avec'", Has(mD, "Ouvrir dans un nouvel onglet") && !Has(mD, "Ouvrir avec"));
        Check("(7) multi-selection: no 'Ouvrir avec', no rename", !Has(mM, "Ouvrir avec") && !Has(mM, "Renommer") && Has(mM, "Supprimer"));
        Check("(7) background: new folder, paste, sort and view submenus",
              Has(mB, "Nouveau dossier")
                  && Has(mB, "Coller")
                  && mB.Any(i => i?.Text == "Trier par" && i.Sub?.Count > 0)
                  && mB.Any(i => i?.Text == "Affichage" && i.Sub?.Count == 4));
        Check("(7) drive: open, properties", Has(mV, "Ouvrir") && Has(mV, "Propriétés"));
        await RunContext(CxViewBase + 2, new List<FileItem>(), true);
        Check("(7) menu id switches the view", CurrentView == ViewMode.Medium);
        SetView(ViewMode.Details);
        await RunContext(CxProps, selN, false);
        Check("(7) menu 'Propriétés' on 2 items = multi-file properties", LastProperties == "multi:2", LastProperties);

        // ---- 8. AUMID / crash.log ----
        Check("(8) explicit AppUserModelID = Winlestia.Files", Program.CurrentAppId() == "Winlestia.Files", Program.CurrentAppId()
            ?? "null");
        App.CrashLog(new InvalidOperationException("lot2 probe"));
        Check("(8) crash.log written through FilesSettings.Dir",
              File.Exists(Path.Combine(FilesSettings.Dir, "crash.log"))
                  && File.ReadAllText(Path.Combine(FilesSettings.Dir, "crash.log")).Contains("lot2 probe"));

        Active.Go(F0);
        await WaitLoaded();
        Safe.Try(() => Directory.Delete(F, true));
        T($"DONE fails={testFails}");
        Close();
    }
}
#endif
