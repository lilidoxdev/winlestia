#if WINLESTIA_SELFTEST
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace WinlestiaFiles;

public sealed partial class MainWindow
{
    // Diagnostic : quel type de cycle (menu d'élément, menu de dossier, menu de fond, entrée/sortie de glisser) fait croître le nombre
    // de threads, et revient-il.
    async Task CtxLeak()
    {
        string F = Path.GetFullPath(Active.Location);
        if (!F.StartsWith(Path.Combine(Path.GetTempPath(), "WinlestiaFilesOps"), StringComparison.OrdinalIgnoreCase))
        {
            Close();
            return;
        }
        await WaitLoaded();
        await Task.Delay(PauseMiseEnPageMs);
        string P(string n) => Path.Combine(F, n);
        var target = (IDropTarget)dropTarget!;
        var kinds = new Dictionary<string, Action>
        {
            ["item"] = () =>
            {
                using var s = ShellItems.Of(new[] { P("ctxtest.txt") });
                using var m = ShellMenu.Create(Hwnd, s!, false);
                m?.Enumerate(true);
            },
            ["folder"] = () =>
            {
                using var s = ShellItems.Of(new[] { P("Cible") });
                using var m = ShellMenu.Create(Hwnd, s!, false);
                m?.Enumerate(true);
            },
            ["background"] = () =>
            {
                using var s = ShellItems.OfFolder(F);
                using var m = ShellMenu.Create(Hwnd, s!, false);
                m?.Enumerate(true);
            },
            ["background-nopopulate"] = () =>
            {
                using var s = ShellItems.OfFolder(F);
                using var m = ShellMenu.Create(Hwnd, s!, false);
                m?.Enumerate(false);
            },
            ["dataobject-only"] = () =>
            {
                using var s = ShellItems.Of(new[] { P("file011.txt") });
                var d = s!.ManagedDataObject(Hwnd)!;
                Marshal.FinalReleaseComObject(d);
            },
            ["dragenter"] = () =>
            {
                using var s = ShellItems.Of(new[] { P("file011.txt") });
                var d = s!.ManagedDataObject(Hwnd)!;
                int e = 3;
                target.DragEnter(d, 0, RowPt(Active.Find(P("Cible"))!), ref e);
                target.DragLeave();
                Marshal.FinalReleaseComObject(d);
            },
        };
        int nCycles = int.TryParse(Environment.GetEnvironmentVariable("WINLESTIA_FILES_CYCLES"), out var nc) ? nc : 20;
        var only = Environment.GetEnvironmentVariable("WINLESTIA_FILES_VARIANTS");
        foreach (var (name, cycle) in kinds)
        {
            if (!string.IsNullOrEmpty(only) && !only.Split(',').Contains(name))
                continue;
            for (int i = 0; i < 3; i++)
                cycle();
            await Task.Delay(PauseCopieMs);
            var a = Snap();
            for (int i = 0; i < nCycles; i++)
            {
                cycle();
                await Task.Delay(DndCycleGapMs);
            }
            var b = Snap();
            await Task.Delay(TimeoutLongMs);
            var c = Snap();
            T($"LEAK {name}: threads {a.threads} -> {b.threads} (after 20) -> {c.threads} (+25 s); handles {a.handles} -> {b.handles} -> " +
                $"{c.handles}; gdi {a.gdi}->{c.gdi}; user {a.user}->{c.user}; private MB {a.mb:F1}->{b.mb:F1}->{c.mb:F1}");
        }
        T("DONE leak diag");
        Close();
    }

    async Task DragByMouse(string[] names, string folder, Func<string, string> P)
    {
        Active.Go(Active.Location);
        await WaitLoaded();
        await Task.Delay(PauseAnimationLongueMs);
        SelectNames(names);
        list.Focus();
        var from = RowPt(Active.Find(P(names[0]))!);
        var to = RowPt(Active.Find(P(folder))!);
        Activate();
        var move = Task.Run(() =>
        {
            SetCursorPos(from.X, from.Y);
            Thread.Sleep(PauseAnimationMs);
            mouse_event(0x2, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(PauseAnimationMs);
            for (int i = 1; i <= 24; i++)
            {
                SetCursorPos(from.X + (to.X - from.X) * i / 24, from.Y + (to.Y - from.Y) * i / 24);
                Thread.Sleep(DndCursorStepMs);
            }
            Thread.Sleep(PauseStabilisationMs);
            mouse_event(0x4, 0, 0, 0, UIntPtr.Zero);
        });
        for (int i = 0; i < 150 && !(names.All(n => Directory.Exists(P(folder)) && File.Exists(Path.Combine(P(folder), n)))); i++)
            await Task.Delay(PauseTickMs);
        await move;
        await Task.Delay(PauseDebounceMs);
        try
        {
            await LastDrop;
        }
        catch (Exception)
        {
            // Auto-test : l'échec de cette étape ne doit pas masquer le verdict du test.
        }
        Check("real mouse drag of a 2-file multi-selection onto a folder row (SHDoDragDrop -> OLE -> our IDropTarget) moves both",
            names.All(n =>
            File.Exists(Path.Combine(P(folder), n)) && !File.Exists(P(n)))
            && names.All(n => Active.Find(P(n)) == null)
            && DiskMatches(),
            $"in folder: {names.Count(n => File.Exists(Path.Combine(P(folder), n)))}, "
            + $"left behind: {names.Count(n => File.Exists(P(n)))}, "
            + $"list still shows: {names.Count(n => Active.Find(P(n)) != null)}");
    }
}
#endif
