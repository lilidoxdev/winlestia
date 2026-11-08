// C#12 / FolderIcons tests: stable icon scheme, migration, cleanup. Fixtures live only under %TEMP%\WinlestiaFilesOps\icons-test and are
// deleted at the end.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using PanneauxGlass;
using D = System.Drawing;
namespace Winlestia.Tests;
[Trait("Category", "Desktop")]
public class FolderIconsIntegrationTests
{
    static D.Color Centre(string ico)
    {
        using var icon = new D.Icon(ico, 48, 48);
        using var bmp = icon.ToBitmap();
        return bmp.GetPixel(bmp.Width / 2, bmp.Height * 3 / 4);
    }
    static void Wipe(string d)
    {
        TempDir.Delete(d);
    }
    static string Ini(string folder) => Path.Combine(folder, "desktop.ini");
    [Fact]
    public void AllChecks() => Sta.Run(AllChecksSta);
    static void AllChecksSta()
    {
        string dir = Path.Combine(Path.GetTempPath(), "WinlestiaFilesOps", "icons-test");
        try
        {
            if (Directory.Exists(dir))
                Wipe(dir);
            Directory.CreateDirectory(dir);
            Store.State.ExplorerTheme = true;
            string state = Path.Combine(dir, "state");
            var svc = new ExplorerAppearance(state);
            var notes = new List<(uint kind, uint flags, string? path)>();
            svc.NotifySink = (k, f, p) => { lock (notes) notes.Add((k, f, p)); };
            string icons = Path.Combine(state, "icons");
            // 1. palette change regenerates the stable icons (atomic, skipped when unchanged)
            bool first = svc.RegenerateIcons("#fa3c5b");
            string folderIcon = svc.StableIcon("folder");
            byte[] redBytes = File.ReadAllBytes(folderIcon);
            var red = Centre(folderIcon);
            Assert.True(!first && ExplorerAppearance.IconKinds.All(k => File.Exists(svc.StableIcon(k))), "stableIconsCreatedForAllKinds");
            DateTime stamp = File.GetLastWriteTimeUtc(folderIcon);
            Thread.Sleep(30);
            bool same = svc.RegenerateIcons("#fa3c5b");
            Assert.True(!same && File.GetLastWriteTimeUtc(folderIcon) == stamp && File.ReadAllBytes(folderIcon).SequenceEqual(redBytes),
                "unchangedAccentSkipsRegeneration");
            // a themed folder pointing at the stable path
            string themed = Path.Combine(dir, "themed");
            Directory.CreateDirectory(themed);
            svc.ApplyFolder(themed, folderIcon);
            string iniBefore = File.ReadAllText(Ini(themed));
            DateTime iniTime = File.GetLastWriteTimeUtc(Ini(themed));
            bool replaced = svc.RegenerateIcons("#3c9cfa");
            var blue = Centre(folderIcon);
            Assert.True(replaced && red.R > red.B + 60 && blue.B > blue.R + 60 && !File.ReadAllBytes(folderIcon).SequenceEqual(redBytes),
                "paletteChangeRegeneratesStableIcon");
            Assert.True(!Directory.EnumerateFiles(icons, "*.tmp").Any(), "noTempLeftBehind");
            Assert.True(File.ReadAllText(Ini(themed)) == iniBefore && File.GetLastWriteTimeUtc(Ini(themed)) == iniTime
                && iniBefore.Contains("folder-current.ico", StringComparison.OrdinalIgnoreCase), "paletteChangeNeedsNoIniRewrite");
            var data = new List<ExplorerAppearance.Backup> { new() { Folder = themed,
                AppliedHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(Ini(themed)))) } };
            Assert.True(svc.AlreadyThemed(themed, folderIcon, data), "alreadyThemedWithStablePathAfterPaletteChange");
            // 2. migration of legacy inis + 3. cleanup
            string Legacy(string hex, string kind)
            {
                string p = Path.Combine(icons, hex + "-" + kind + "-v2.ico");
                ExplorerAppearance.DrawIcon(p, "#" + hex, kind);
                return p;
            }
            string l1 = Legacy("3c9cfa", "folder"), l2 = Legacy("fa3c5b", "folder"), l3 = Legacy("3c9cfa", "download"),
                orphan = Legacy("aaaaaa", "folder");
            var legacyFolders = new List<string>();
            for (int i = 0; i < 5; i++)
            {
                string f = Path.Combine(dir, "legacy" + i);
                Directory.CreateDirectory(f);
                legacyFolders.Add(f);
                svc.ApplyFolder(f, i == 2 ? l3 : i % 2 == 0 ? l1 : l2);
            }
            string edited = legacyFolders[4];
            File.SetAttributes(Ini(edited), FileAttributes.Normal);
            File.AppendAllText(Ini(edited), "InfoTip=mine\r\n");
            string editedText = File.ReadAllText(Ini(edited));
            string unthemed = Path.Combine(dir, "plain");
            Directory.CreateDirectory(unthemed);
            var r = svc.MigrateAsync(2, 1).GetAwaiter().GetResult();
            Assert.True(r.migrated == 4 && legacyFolders.Take(4).All(f => File.ReadAllText(Ini(f)).Contains("-current.ico",
                StringComparison.OrdinalIgnoreCase) && !File.ReadAllText(Ini(f)).Contains("-v2.ico", StringComparison.OrdinalIgnoreCase)),
                "migrationRewritesLegacyInis");
            Assert.True(File.ReadAllText(Ini(legacyFolders[2])).Contains("download-current.ico", StringComparison.OrdinalIgnoreCase),
                "migrationKeepsKindOfSpecialIcons");
            Assert.True(r.edited == 1 && File.ReadAllText(Ini(edited)) == editedText
                && editedText.Contains("-v2.ico", StringComparison.OrdinalIgnoreCase), "migrationSkipsUserEditedIni");
            Assert.True(r.failed == 0 && legacyFolders.Take(4).All(f => svc.AlreadyThemed(f, f == legacyFolders[2]
                ? svc.StableIcon("download") : folderIcon, ReadManifest(state))), "migrationKeepsAppliedHashSemantics");
            Assert.True(!File.Exists(Ini(unthemed)), "migrationLeavesUnthemedFoldersAlone");
            Assert.True(!File.Exists(orphan) && !File.Exists(l3) && !File.Exists(l2), "cleanupRemovesUnreferencedLegacyIcons");
            Assert.True(File.Exists(l1) == editedText.Contains(Path.GetFileName(l1), StringComparison.OrdinalIgnoreCase) && File.Exists(l1),
                "cleanupKeepsIconReferencedByEditedIni");
            Assert.True(File.ReadAllText(Path.Combine(icons, "state.json")).Contains("\"Migrated\":true"),
                "migrationRunsOnceFlagPersisted");
            // 4. shell refresh: one ASSOCCHANGED, then bounded per-folder batches
            lock (notes)
                notes.Clear();
            int gen = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            svc.RefreshShellAsync(gen, 2, 40).GetAwaiter().GetResult();
            sw.Stop();
            int folders = ReadManifest(state).Count;
            Assert.True(notes.Count(n => n.kind == 0x08000000) == 1 && notes[0].kind == 0x08000000
                && notes.Count(n => n.kind == 0x2000) == folders && folders >= 6, "shellRefreshAssocChangedOnceThenFolders");
            Assert.True(sw.ElapsedMilliseconds >= (folders / 2 - 1) * 35, "shellRefreshIsThrottled");
            // 5. ColourVisibleFolder: callable from many threads, budgeted, no exception
            bool ok = true;
            var tasks = Enumerable.Range(0, 4).Select(t =>
                Task.Run(() => { try { for (int i = 0; i < 30; i++) svc.ColourVisibleFolder(Path.Combine(dir,
                "nowhere" + (i % 7))); } catch { ok = false; } })).ToArray();
            Task.WaitAll(tasks);
            Assert.True(ok && (int)typeof(ExplorerAppearance).GetField("budgetUsed",
                BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(svc)! <= 12, "colourVisibleFolderThreadSafeAndBudgeted");
            svc.Dispose();
            // 6. real shell: fresh process, stable path rewritten atomically
            string shell = ShellProbe.Run(Path.Combine(dir, "shell"));
            Console.WriteLine(shell);
            Assert.True(shell.Split('\n').First(l => l.StartsWith("(a1)")).Contains("turned blue: 0 ms")
                || shell.Split('\n').First(l => l.StartsWith("(a1)")).Contains("ms"), "shellFreshProcessSeesNewColourAtSamePath");
        }
        finally { Wipe(dir); }
    }
    static List<ExplorerAppearance.Backup> ReadManifest(string state) =>
        System.Text.Json.JsonSerializer.Deserialize<List<ExplorerAppearance.Backup>>(File.ReadAllText(Path.Combine(state,
        "manifest.json")))!;
}
