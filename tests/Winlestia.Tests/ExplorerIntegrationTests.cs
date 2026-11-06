// C#12 / Explorer077 fixture / only freshly created temporary folders are customized.
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using PanneauxGlass;
namespace Winlestia.Tests;
[Trait("Category", "Desktop")]
public class ExplorerIntegrationTests
{
    [Fact]
    public void AllChecks() => Sta.Run(AllChecksSta);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr h, int attribute, out uint value, int size);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path, uint attributes, ref Info info, uint size, uint flags);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct Info
    {
        public IntPtr Icon; public int Index; public uint Attributes; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Display; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string Type;
    }
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    static void AllChecksSta()
    {
        string root = Path.Combine(Path.GetTempPath(), "WinlestiaExplorer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var t = typeof(Entry).Assembly.GetType("PanneauxGlass.ExplorerAppearance");
        var f = BindingFlags.NonPublic | BindingFlags.Instance;
        var sf = BindingFlags.NonPublic | BindingFlags.Static;
        var service = Activator.CreateInstance(t, f, null, new object[] { Path.Combine(root, "state") }, null);
        ((System.Windows.Threading.DispatcherTimer)t.GetField("timer", f).GetValue(service)).Stop();
        object Call(string name, params object[] args) => t.GetMethod(name, f).Invoke(service, args);
        string docRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WinlestiaTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(docRoot);
        try
        {
            using (var fixture = new System.Windows.Forms.Form { Text = "Winlestia border fixture", Width = 320, Height = 200 })
            {
                fixture.Show();
                Assert.True((bool)Call("ColourFrame", fixture.Handle, false), "applicationBorderAcceptsThemeColour");
                Assert.True((uint)t.GetMethod("Colour", sf).Invoke(null, new object[] { "#afd4f0" }) == 0x00f0d4af, "themeColourUsesNativeRgbFormat");
                Assert.True((bool)Call("RestoreFrame", fixture.Handle), "applicationBorderRestoresWindowsDefault");
                fixture.Close();
            }
            string red = Path.Combine(root, "red.ico"), blue = Path.Combine(root, "blue.ico");
            t.GetMethod("DrawIcon", sf).Invoke(null, new object[] { red, "#fa3c5b", "document" });
            t.GetMethod("DrawIcon", sf).Invoke(null, new object[] { blue, "#60a5fa", "download" });
            using (var icon = new System.Drawing.Icon(red))
            {
                Assert.True(icon.Width > 0 && File.ReadAllBytes(red)[4] == 6, "icoIsValidAndMultiResolution");
            }
            Assert.True(!File.ReadAllBytes(red).SequenceEqual(File.ReadAllBytes(blue)), "paletteChangesIconPixels");
            Store.State.ExplorerTheme = true;
            string visible = Path.Combine(docRoot, "visible");
            Directory.CreateDirectory(visible);
            Directory.CreateDirectory(Path.Combine(visible, "A"));
            Directory.CreateDirectory(Path.Combine(visible, "B"));
            Call("ColourVisibleFolder", visible);
            Assert.True(new[] { "A", "B" }.All(n => File.Exists(Path.Combine(visible, n, "desktop.ini"))), "visibleChildFoldersAreThemed");
            Call("Restore");
            Assert.True(new[] { "A", "B" }.All(n => !File.Exists(Path.Combine(visible, n, "desktop.ini"))), "visibleChildFoldersRestore");
            Store.State.ExplorerTheme = false;
            Store.State.ExplorerTheme = true;
            string deep = Path.Combine(docRoot, "deep");
            string D(params string[] p) => Path.Combine(new[] { deep }.Concat(p).ToArray());
            Directory.CreateDirectory(D("a", "b", "c", "d"));
            Directory.CreateDirectory(D("hid", "x"));
            File.SetAttributes(D("hid"), FileAttributes.Hidden);
            Call("ColourVisibleFolder", deep);
            bool Has(params string[] p) => File.Exists(Path.Combine(D(p), "desktop.ini"));
            Assert.True(Has("a") && Has("a", "b") && Has("a", "b", "c"), "depthThreeIsThemed");
            Assert.True(!Has("a", "b", "c", "d"), "depthFourIsNotThemed");
            Assert.True(!Has("hid") && !Has("hid", "x"), "hiddenFoldersAndTheirChildrenSkipped");
            Call("Restore");
            Assert.True(!Has("a") && !Has("a", "b") && !Has("a", "b", "c"), "depthThreeRestoresEverything");
            string many = Path.Combine(docRoot, "many");
            for (int i = 0; i < 300; i++)
                Directory.CreateDirectory(Path.Combine(many, "f" + i));
            var budget = new[] { 1024 };
            t.GetMethod("ColourVisibleTree", f).Invoke(service, new object[] { many, budget });
            int themed = Directory.GetDirectories(many).Count(d => File.Exists(Path.Combine(d, "desktop.ini")));
            Assert.True(themed + (File.Exists(Path.Combine(many, "desktop.ini")) ? 1 : 0) == 256 && budget[0] == 1024 - 256, "perLocationCapIs256");
            Call("Restore");
            Assert.True(!Directory.GetDirectories(many).Any(d => File.Exists(Path.Combine(d, "desktop.ini"))), "capRestoresEverything");
            string shared = Path.Combine(docRoot, "shared");
            for (int i = 0; i < 10; i++)
                Directory.CreateDirectory(Path.Combine(shared, "s" + i));
            var small = new[] { 3 };
            t.GetMethod("ColourVisibleTree", f).Invoke(service, new object[] { shared, small });
            Assert.True(small[0] == 0 && Directory.GetDirectories(shared).Count(d => File.Exists(Path.Combine(d, "desktop.ini"))) + (File.Exists(Path.Combine(shared, "desktop.ini")) ? 1 : 0) == 3, "passBudgetIsHonoured");
            Call("Restore");
            string outside = Path.Combine(root, "outside");
            Directory.CreateDirectory(Path.Combine(outside, "o1"));
            Call("ColourVisibleFolder", outside);
            Assert.True(!File.Exists(Path.Combine(outside, "desktop.ini")) && !File.Exists(Path.Combine(outside, "o1", "desktop.ini")), "folderOutsidePersonalFoldersIsNotThemed");
            string idem = Path.Combine(docRoot, "idem");
            Directory.CreateDirectory(Path.Combine(idem, "k1"));
            Store.State.ExplorerTheme = true;
            Call("ColourVisibleFolder", idem);
            string idemIni = Path.Combine(idem, "k1", "desktop.ini");
            var idemTime = File.GetLastWriteTimeUtc(idemIni);
            int idemNotes = (int)t.GetField("NotifyCount", f).GetValue(service);
            ((System.Collections.IDictionary)t.GetField("scanned", f).GetValue(service)).Clear();
            System.Threading.Thread.Sleep(30);
            Call("ColourVisibleFolder", idem);
            Assert.True(File.Exists(idemIni) && File.GetLastWriteTimeUtc(idemIni) == idemTime, "noWriteWhenAlreadyThemed");
            Assert.True(idemNotes > 0 && (int)t.GetField("NotifyCount", f).GetValue(service) == idemNotes, "noNotifyWhenNothingWritten");
            var t0 = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            string dn = Path.Combine(root, "debounce");
            Assert.True((bool)Call("Notify", dn, t0) && !(bool)Call("Notify", dn, t0.AddSeconds(9)) && (bool)Call("Notify", dn, t0.AddSeconds(11)) && (bool)Call("Notify", dn + "2", t0.AddSeconds(11)), "notifyDebouncedPerFolder");
            string repo = Path.Combine(docRoot, "repo");
            Directory.CreateDirectory(Path.Combine(repo, ".git"));
            Directory.CreateDirectory(Path.Combine(repo, "src"));
            string plain = Path.Combine(docRoot, "plain");
            Directory.CreateDirectory(Path.Combine(plain, "node_modules"));
            Directory.CreateDirectory(Path.Combine(plain, "sub"));
            Directory.CreateDirectory(Path.Combine(plain, "sub2", ".git"));
            Call("ColourVisibleFolder", repo);
            Call("ColourVisibleFolder", plain);
            Assert.True(!File.Exists(Path.Combine(repo, "desktop.ini")) && !File.Exists(Path.Combine(repo, "src", "desktop.ini")) && !File.Exists(Path.Combine(plain, "sub2", "desktop.ini")) && File.Exists(Path.Combine(plain, "sub", "desktop.ini")), "gitFolderIsSkipped");
            Assert.True(!File.Exists(Path.Combine(plain, "node_modules", "desktop.ini")), "devFoldersAreSkipped");
            Call("Restore");
            string userOpen = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WinlestiaTest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(userOpen, "child"));
            try
            {
                Call("ColourVisibleFolder", userOpen);
                Assert.True(File.Exists(Path.Combine(userOpen, "desktop.ini")) && File.Exists(Path.Combine(userOpen, "child", "desktop.ini")), "openFolderUnderUserFolderIsThemed");
                Call("Restore");
                Assert.True(!File.Exists(Path.Combine(userOpen, "desktop.ini")) && !File.Exists(Path.Combine(userOpen, "child", "desktop.ini")), "openFolderRestores");
            }
            finally { try { File.SetAttributes(userOpen, FileAttributes.Normal); Directory.Delete(userOpen, true); } catch { } }
            Store.State.ExplorerTheme = false;
            string fresh = Path.Combine(root, "fresh");
            Directory.CreateDirectory(fresh);
            var attrs = File.GetAttributes(fresh);
            System.Threading.Tasks.Task.Run(() => Call("ApplyFolder", fresh, red)).GetAwaiter().GetResult();
            string freshIni = Path.Combine(fresh, "desktop.ini");
            Assert.True(File.ReadAllText(freshIni).Contains("red.ico"), "shellFolderCustomSettingsWorksOnWorker");
            var info = new Info();
            SHGetFileInfo(fresh, 0, ref info, (uint)Marshal.SizeOf<Info>(), 0x100);
            Assert.True(info.Icon != IntPtr.Zero, "windowsShellResolvesFolderIcon");
            if (info.Icon != IntPtr.Zero)
            {
                using var nativeIcon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(info.Icon).Clone();
                using var pixels = nativeIcon.ToBitmap();
                Console.Error.WriteLine("Shell icon size: " + pixels.Width + "x" + pixels.Height);
                int tinted = 0;
                for (int y = 0; y < pixels.Height; y++)
                for (int x = 0; x < pixels.Width; x++)
                {
                    var pixel = pixels.GetPixel(x, y);
                    if (pixel.A > 100 && pixel.R > pixel.G * 1.5 && pixel.R > pixel.B * 1.3)
                        tinted++;
                }
                Assert.True(tinted > pixels.Width * pixels.Height * .2, "windowsShellActuallyUsesTheRedIcon");
                DestroyIcon(info.Icon);
            }
            Call("ApplyFolder", fresh, blue);
            Assert.True(File.ReadAllText(freshIni).Contains("blue.ico"), "secondThemeUpdatesResource");
            Call("Restore");
            Assert.True(!File.Exists(freshIni) && File.GetAttributes(fresh) == attrs, "freshFolderRestoresExactly");
            string dated = Path.Combine(root, "dated");
            string datedKid = Path.Combine(dated, "kid");
            Directory.CreateDirectory(datedKid);
            var y2020 = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            Directory.SetLastWriteTimeUtc(datedKid, y2020);
            Directory.SetLastWriteTimeUtc(dated, y2020);
            Call("ApplyFolder", datedKid, red);
            bool Near(string d, DateTime w) => Math.Abs((Directory.GetLastWriteTimeUtc(d) - w).TotalSeconds) < 2;
            Assert.True(File.Exists(Path.Combine(datedKid, "desktop.ini")) && Near(datedKid, y2020), "mtimePreservedAfterTheme");
            Assert.True(Near(dated, y2020), "parentMtimeUnchangedByChildTheme");
            Directory.SetLastWriteTimeUtc(datedKid, new DateTime(2023, 5, 5, 0, 0, 0, DateTimeKind.Utc));
            Call("ApplyFolder", datedKid, blue);
            Assert.True(Near(datedKid, y2020), "originalWriteUtcHonoredOnReTheme");
            Call("Restore");
            Assert.True(!File.Exists(Path.Combine(datedKid, "desktop.ini")) && Near(datedKid, y2020), "mtimePreservedAfterRestore");
            string repair = Path.Combine(root, "repair");
            Directory.CreateDirectory(Path.Combine(repair, "sub"));
            File.WriteAllText(Path.Combine(repair, "old.txt"), "x");
            File.WriteAllText(Path.Combine(repair, "desktop.ini"), "[.ShellClassInfo]");
            var y2021 = new DateTime(2021, 3, 3, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(Path.Combine(repair, "old.txt"), y2021);
            Directory.SetLastWriteTimeUtc(Path.Combine(repair, "sub"), new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            Call("RepairDatesFromChildren", repair);
            Assert.True(Near(repair, y2021), "repairDatesUsesNewestChildExcludingIni");
            string legacy = Path.Combine(root, "legacy");
            Directory.CreateDirectory(legacy);
            File.WriteAllText(Path.Combine(legacy, "c.txt"), "x");
            File.SetLastWriteTimeUtc(Path.Combine(legacy, "c.txt"), y2021);
            Directory.SetLastWriteTimeUtc(legacy, DateTime.UtcNow);
            Directory.CreateDirectory(Path.Combine(root, "state"));
            string manifestPath = Path.Combine(root, "state", "manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(new[] { new { Folder = legacy, Attributes = 16, IniAttributes = 0 } }));
            Call("RepairManifestDates");
            Assert.True(Near(legacy, y2021) && File.ReadAllText(manifestPath).Contains("\"DateRepaired\":true"), "repairRunsForAlreadyThemedUnchangedFolder");
            Call("Restore");
            string existing = Path.Combine(root, "existing");
            Directory.CreateDirectory(existing);
            string ini = Path.Combine(existing, "desktop.ini");
            byte[] original = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("[.ShellClassInfo]\r\nInfoTip=Été personnel\r\nIconResource=C:\\Windows\\system32\\shell32.dll,3\r\n")).ToArray();
            File.WriteAllBytes(ini, original);
            File.SetAttributes(ini, FileAttributes.Hidden | FileAttributes.System);
            var originalAttrs = File.GetAttributes(ini);
            Call("ApplyFolder", existing, red);
            Call("Restore");
            Assert.True(File.ReadAllBytes(ini).SequenceEqual(original) && File.GetAttributes(ini) == originalAttrs, "existingIniBytesAndAttributesRestored");
            Call("ApplyFolder", existing, red);
            File.SetAttributes(ini, FileAttributes.Normal);
            File.AppendAllText(ini, "\r\n;changed elsewhere");
            byte[] edited = File.ReadAllBytes(ini);
            string message = (string)Call("Restore");
            Assert.True(File.ReadAllBytes(ini).SequenceEqual(edited) && message.Contains("1 dossier"), "externalEditsAreNeverOverwritten");
            try
            {
                Call("ApplyFolder", Environment.GetFolderPath(Environment.SpecialFolder.Windows), red);
                Assert.True(false, "systemFoldersRejected");
            }
            catch (TargetInvocationException) { Assert.True(true, "systemFoldersRejected"); }
        }
        finally { try { ((IDisposable)service).Dispose(); } catch { } foreach (string dir in new[] { docRoot, root }) { try { if (Directory.Exists(dir)) { foreach (var e in Directory.EnumerateFileSystemEntries(dir, "*", SearchOption.AllDirectories)) try { File.SetAttributes(e, FileAttributes.Normal); } catch { } File.SetAttributes(dir, FileAttributes.Normal); Directory.Delete(dir, true); } } catch { } } }
    }
}
