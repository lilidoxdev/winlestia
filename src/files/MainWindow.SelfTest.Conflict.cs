#if WINLESTIA_SELFTEST
// WINLESTIA_FILES_SELFTEST=conflict : copie sur un fichier existant par le VRAI chemin de collage (UI native IFileOperation, FileOps.Quiet
// désactivé), la boîte Windows
// « Remplacer ou ignorer les fichiers » est pilotée avec UI Automation depuis un thread du pool (Uia ci-dessous, aussi utilisé par le
// test xfer). Exige WINLESTIA_FILES_DATA_DIR sous %TEMP% et un dossier de fixtures sous %TEMP%\WinlestiaFilesOps (harnais).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace WinlestiaFiles;

// Une action scriptée sur une boîte native : premier élément (par priorité de jeton, sous-chaîne de son Name sans casse) trouvé
// dans une fenêtre non principale de ce processus. Closes : l'élément cliqué doit disparaître (réessayé, puis un vrai clic souris,
// si le premier Invoke a été avalé)
internal sealed record UiaStep(string Label, string[] Tokens, bool CheckAll = false, int WaitMs = 8000, bool Optional = false,
    bool Closes = false);

internal static class Uia
{
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);

    internal static List<AutomationElement> Windows(int mainHwnd)
    {
        var cond = new OrCondition(new PropertyCondition(AutomationElement.ProcessIdProperty, Environment.ProcessId),
            new PropertyCondition(AutomationElement.ClassNameProperty, "OperationStatusWindow"));
        var list = new List<AutomationElement>();
        try
        {
            foreach (AutomationElement w in AutomationElement.RootElement.FindAll(TreeScope.Children, cond))
            {
                try
                {
                    if (w.Current.NativeWindowHandle != mainHwnd)
                        list.Add(w);
                }
                catch (Exception)
                {
                    // Auto-test : l'échec de cette étape ne doit pas masquer le verdict du test.
                }
            }
        }
        catch (Exception)
        {
            // Auto-test : l'échec de cette étape ne doit pas masquer le verdict du test.
        }
        return list;
    }

    static bool Gone(AutomationElement e, int ms)
    {
        long end = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < end)
        {
            try
            {
                _ = e.Current.Name;
                _ = e.Current.BoundingRectangle;
            }
            catch { return true; }
            Task.Delay(PauseTickMs).Wait();
        }
        try
        {
            _ = e.Current.Name;
            return false;
        }
        catch { return true; }
    }

    internal static string Describe(AutomationElement e)
    {
        try
        {
            return $"{e.Current.ControlType.ProgrammaticName.Replace("ControlType.", "")}|{e.Current.Name}|" +
                $"{e.Current.ClassName}|{e.Current.AutomationId}";
        }
        catch { return "?"; }
    }

    internal static List<string> Dump(AutomationElement window, int max = 70)
    {
        var lines = new List<string> { "WINDOW " + Describe(window) };
        try
        {
            foreach (AutomationElement e in window.FindAll(TreeScope.Descendants, Condition.TrueCondition))
            {
                if (lines.Count >= max)
                    break;
                lines.Add("  " + Describe(e));
            }
        }
        catch (Exception)
        {
            // Auto-test : l'échec de cette étape ne doit pas masquer le verdict du test.
        }
        return lines;
    }

    static bool Act(AutomationElement e, bool check, bool mouse = false)
    {
        try
        {
            if (mouse)
                goto click;
            if (check)
            {
                if (e.TryGetCurrentPattern(TogglePattern.Pattern, out var tp) && tp is TogglePattern t)
                {
                    if (t.Current.ToggleState != ToggleState.On)
                        t.Toggle();
                    return true;
                }
                if (e.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var sp) && sp is SelectionItemPattern s)
                {
                    s.Select();
                    return true;
                }
            }
            if (e.TryGetCurrentPattern(InvokePattern.Pattern, out var ip) && ip is InvokePattern inv)
            {
                Task.Run(() => { try { inv.Invoke(); } catch (Exception) {
                    /* Auto-test : l'échec de cette étape ne doit pas masquer le verdict du test. */ } });
                return true;
            }
            if (e.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var sp2) && sp2 is SelectionItemPattern s2)
            {
                s2.Select();
                return true;
            }
            if (e.TryGetCurrentPattern(TogglePattern.Pattern, out var tp2) && tp2 is TogglePattern t2)
            {
                t2.Toggle();
                return true;
            }
        click:
            var r = e.Current.BoundingRectangle;
            if (r.IsEmpty)
                return false;
            SetCursorPos((int)(r.X + r.Width / 2), (int)(r.Y + r.Height / 2));
            mouse_event(2, 0, 0, 0, UIntPtr.Zero);
            mouse_event(4, 0, 0, 0, UIntPtr.Zero);
            return true;
        }
        catch { return false; }
    }

    // Exécute les étapes dans l'ordre. Renvoie une ligne de journal par étape ("OK libellé -> élément" / "MISS libellé") ; en cas
    // d'échec l'arbre de la boîte est vidé et les boîtes sont fermées (WM_CLOSE).
    internal static Task<List<string>> Run(int mainHwnd, params UiaStep[] steps) => Task.Run(() =>
    {
        var log = new List<string>();
        AutomationElement? lastHit = null;
        foreach (var step in steps)
        {
            long stepStart = Environment.TickCount64;
            long end = Environment.TickCount64 + step.WaitMs;
            bool done = false, dumped = false;
            int seen = 0;
            while (!done && Environment.TickCount64 < end)
            {
                var wins = Windows(mainHwnd);
                if (wins.Count > 0 && !dumped && ++seen == 3)
                {
                    dumped = true;
                    foreach (var w in wins)
                        log.AddRange(Dump(w).Select(l => "    " + l));
                }
                foreach (var w in wins)
                {
                    AutomationElement[] all;
                    try
                    {
                        all = w.FindAll(TreeScope.Descendants, Condition.TrueCondition).Cast<AutomationElement>().ToArray();
                    }
                    catch { continue; }
                    if (step.CheckAll)
                    {
                        var boxes = all.Where(e => { try { return e.Current.ControlType == ControlType.CheckBox
                            && e.Current.AutomationId != "eltSkipCheckbox" && !e.Current.AutomationId.Contains("Global"); } catch {
                            return false; } }).ToList();
                        if (boxes.Count < 2)
                        // les vignettes par fichier se remplissent un instant après l'ouverture de la page ; les deux cases « globales
                        // » servent de repli
                        {
                            if (Environment.TickCount64 - stepStart < 6000)
                                continue;
                            boxes = all.Where(e => { try { return e.Current.ControlType == ControlType.CheckBox
                                && e.Current.AutomationId.Contains("Global"); } catch { return false; } }).ToList();
                            if (boxes.Count == 0)
                                continue;
                        }
                        Task.Delay(PauseDessinMs).Wait();     // la page doit être entièrement dessinée avant la bascule
                        log.AddRange(Dump(w).Select(l => "    " + l));
                        int n = boxes.Count(b => Act(b, true));
                        Task.Delay(PauseStabilisationMs).Wait();
                        foreach (var bx in boxes)
                        {
                            try
                            {
                                if (bx.TryGetCurrentPattern(TogglePattern.Pattern, out var tq)
                                    && ((TogglePattern)tq).Current.ToggleState != ToggleState.On)
                                    Act(bx, true);
                            }
                            catch (Exception)
                            {
                                // Auto-test : l'échec de cette étape ne doit pas masquer le verdict du test.
                            }
                        }
                        log.AddRange(boxes.Select(b => { try { return "    after: " + Describe(b) + " state="
                            + (b.TryGetCurrentPattern(TogglePattern.Pattern, out var tp)
                            ? ((TogglePattern)tp).Current.ToggleState.ToString() : "?"); } catch { return "    after: ?"; } }));
                        if (n == 0)
                            continue;
                        log.Add($"OK {step.Label} -> {n} case(s) cochée(s)");
                        done = true;
                        break;
                    }
                    foreach (var token in step.Tokens)
                    {
                        var hit = all.FirstOrDefault(e => { try { return (token[0] == '=' ? string.Equals(e.Current.Name, token[1..],
                            StringComparison.OrdinalIgnoreCase) : e.Current.Name.Contains(token, StringComparison.OrdinalIgnoreCase))
                            && e.Current.IsEnabled && !e.Current.IsOffscreen; } catch { return false; } });
                        if (hit != null && Act(hit, false))
                        {
                            string how = "invoke";
                            if (step.Closes)
                            {
                                for (int attempt = 0; attempt < 3 && !Gone(hit, 1500); attempt++)
                                {
                                    how = attempt == 0 ? "invoke x2" : "mouse";
                                    Act(hit, false, mouse: attempt > 0);
                                }
                                if (!Gone(hit, 1500))
                                    how += " (element still there)";
                            }
                            lastHit = hit;
                            log.Add($"OK {step.Label} -> {Describe(hit)} [{how}]");
                            done = true;
                            break;
                        }
                    }
                    if (done)
                        break;
                }
                if (!done)
                    Task.Delay(PauseRenduMs).Wait();
            }
            if (!done)
            {
                if (step.Optional)
                {
                    log.Add($"SKIP {step.Label}");
                    continue;
                }
                log.Add($"MISS {step.Label}");
                foreach (var w in Windows(mainHwnd))
                {
                    if (!dumped)
                        log.AddRange(Dump(w).Select(l => "    " + l));
                    try
                    {
                        PostMessage((IntPtr)w.Current.NativeWindowHandle, 0x10, IntPtr.Zero, IntPtr.Zero);
                    }
                    catch (Exception)
                    {
                        // Auto-test : l'échec de cette étape ne doit pas masquer le verdict du test.
                    }
                }
                break;
            }
            Task.Delay(PauseTransitionMs).Wait();     // let the dialog switch page
        }
        return log;
    });
}

public sealed partial class MainWindow
{
    int mainHwndCache;
    int MainHwnd => mainHwndCache != 0 ? mainHwndCache : (mainHwndCache = (int)Hwnd);   // lire d'abord sur le thread UI (Hwnd en a besoin)

    void LogUia(string what, List<string> log)
    {
        foreach (var l in log)
            T($"  uia[{what}] {l}");
    }

    async Task ConflictTest()
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
        T("START conflict fixture " + F0);
        Activate();
        FileOps.Quiet = false;
        await WaitLoaded();
        string F = Path.Combine(F0, "CF");
        string src = Path.Combine(F, "src"), dst = Path.Combine(F, "dst");
        try
        {
            Directory.CreateDirectory(src);
            Directory.CreateDirectory(dst);
            string Read(string p)
            {
                try
                {
                    return File.ReadAllText(p);
                }
                catch { return "<absent>"; }
            }
            void Reset(int n)
            {
                foreach (var d in new[] { src, dst })
                    foreach (var f in Directory.GetFiles(d))
                        File.Delete(f);
                for (int i = 1; i <= n; i++)
                {
                    string name = n == 1 ? "a.txt" : $"a{i}.txt";
                    File.WriteAllText(Path.Combine(src, name), "NEW");
                    File.WriteAllText(Path.Combine(dst, name), "OLD");
                }
            }
            async Task<(OpResult? r, List<string> log)> Paste(int n, params UiaStep[] steps)
            {
                Active.Go(dst);
                await WaitLoaded();
                var paths = Directory.GetFiles(src).OrderBy(p => p).ToList();
                bool ok = await SetClipboardAsync(paths, false);
                Check("clipboard set", ok);
                var drv = Uia.Run(MainHwnd, steps);
                var paste = PasteAsync(dst);
                var both = Task.WhenAll(paste, drv);
                if (await Task.WhenAny(both, Task.Delay(TimeoutConflitMs)) != both)
                {
                    T("TIMEOUT paste");
                    return (null, new List<string> { "timeout" });
                }
                var log = await drv;
                await WaitLoaded();
                return (await paste, log);
            }
            string[] replace = { "Remplacer le fichier", "Replace the file", "Remplacer les fichiers", "Replace the files", "Remplacer",
                "Replace" };
            string[] skip = { "Ignorer ce fichier", "Skip this file", "Ignorer les fichiers", "Skip these files", "Ignorer", "Skip" };
            string[] decide = { "Me laisser décider", "Let me decide", "Comparer les informations", "Compare info" };

            // ---- pass 1: replace ----
            Reset(1);
            int undoBefore = journal.UndoCount;
            var (r1, l1) = await Paste(1, new UiaStep("replace", replace, Closes: true));
            LogUia("replace", l1);
            Check("(1) replace: dialog found and driven", l1.Any(x => x.StartsWith("OK")) && !l1.Any(x => x.StartsWith("MISS")));
            Check("(1) replace: destination now NEW", Read(Path.Combine(dst, "a.txt")) == "NEW", Read(Path.Combine(dst, "a.txt")));
            Check("(1) replace: no extra file", Directory.GetFiles(dst).Length == 1);
            Check("(1) replace: item shown, list matches disk", DiskMatches());
            bool irrev = journal.NextUndo is { Irreversible: true };
            Check("(1) replace: journal does not claim an undo of the overwrite", journal.UndoCount == undoBefore || irrev,
                $"next={journal.NextUndo?.Label ?? "none"} irreversible={irrev}");
            string t1 = await UndoAsync();
            Check("(1) replace: Ctrl+Z leaves NEW in place and says it cannot undo", Read(Path.Combine(dst, "a.txt")) == "NEW"
                && File.Exists(Path.Combine(dst, "a.txt")) && t1.StartsWith("Impossible"), t1);

            // ---- pass 2: skip ----
            Reset(1);
            var (r2, l2) = await Paste(1, new UiaStep("skip", skip, Closes: true));
            LogUia("skip", l2);
            Check("(2) skip: dialog found and driven", l2.Any(x => x.StartsWith("OK")) && !l2.Any(x => x.StartsWith("MISS")));
            Check("(2) skip: destination still OLD", Read(Path.Combine(dst, "a.txt")) == "OLD", Read(Path.Combine(dst, "a.txt")));
            Check("(2) skip: list matches disk", DiskMatches());

            // ---- pass 3: let me decide, keep both ----
            Reset(1);
            var (r3, l3) = await Paste(1, new UiaStep("decide", decide), new UiaStep("keep both", Array.Empty<string>(), CheckAll: true,
                WaitMs: 15000), new UiaStep("continue", new[] { "Continuer", "Continue" }, Closes: true));
            LogUia("decide", l3);
            var names = Directory.GetFiles(dst).Select(Path.GetFileName).OrderBy(x => x).ToList();
            T("  dst after keep-both: " + string.Join(", ", names) + " a.txt=" + Read(Path.Combine(dst, "a.txt")) + " err=" + r3?.Error
                + " aborted=" + r3?.Aborted);
            Check("(3) keep both: dialogs driven", !l3.Any(x => x.StartsWith("MISS")));
            Check("(3) keep both: two files, original OLD kept, copy NEW named 'a (2).txt'", names.Count == 2 && Read(Path.Combine(dst,
                "a.txt")) == "OLD" && names.Any(n => n != "a.txt" && n!.StartsWith("a (") && Read(Path.Combine(dst, n)) == "NEW"),
                string.Join(", ", names));
            Check("(3) keep both: list matches disk", DiskMatches());

            // ---- passe 4 : 3 conflits, « faire cela pour les N conflits suivants » ----
            Reset(3);
            var (r4, l4) = await Paste(3, new UiaStep("apply to all", new[] { "pour les", "for the next", "tous les", "all current",
                "all remaining", "Do this for" }, Optional: true, WaitMs: 6000), new UiaStep("replace", replace, Closes: true));
            LogUia("all3", l4);
            var contents = new[] { 1, 2, 3 }.Select(i => Read(Path.Combine(dst, $"a{i}.txt"))).ToList();
            T("  3-conflict result: " + string.Join(",", contents) + " | uia steps: " + string.Join(" ; ", l4.Where(x
                => !x.StartsWith("    ")).Select(x => x.Length > 90 ? x[..90] : x)));
            Check("(4) 3 conflicts: single choice applied to all three", contents.All(c => c == "NEW")
                && Directory.GetFiles(dst).Length == 3, string.Join(",", contents));
            Check("(4) 3 conflicts: journal not claiming undo", journal.NextUndo is not { Irreversible: false,
                Label: "Copier" or "Copier (3 éléments)" });
        }
        catch (Exception ex) { Check("conflict test exception", false, ex.ToString()); }
        finally
        {
            Active.Go(F0);
            await WaitLoaded();
            Safe.Try(() => Directory.Delete(F, true));
        }
        T($"DONE fails={testFails}");
        Close();
    }
}
#endif
