// C#12 / FilesRouter fixture / only freshly created temporary files and folders are touched.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using WinlestiaFiles;
namespace Winlestia.Tests;
public class FilesRouterTests
{
    [Fact]
    public void AllChecks()
    {
        string root = Path.Combine(Path.GetTempPath(), "WinlestiaFiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string dir = Path.Combine(root, "dossier");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(root, "a.txt");
            File.WriteAllText(file, "x");
            string zip = Path.Combine(root, "b.zip");
            File.WriteAllText(zip, "x");
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            bool IsExplorer(string a, string? expected = null)
            {
                var r = PathRouter.Route(a);
                return !r.IsOwn && r.Value == (expected ?? a);
            }
            bool IsOwn(string? a, string expected)
            {
                var r = PathRouter.Route(a);
                return r.IsOwn && r.Value == expected;
            }
            Assert.True(IsExplorer("shell:Downloads"), "shellPrefixGoesToExplorer");
            Assert.True(IsExplorer("SHELL:Downloads"), "shellPrefixIsCaseInsensitive");
            Assert.True(IsExplorer("::{645FF040-5081-101B-9F08-00AA002F954E}"), "shellGuidGoesToExplorer");
            Assert.True(IsExplorer(@"\\server\share"), "uncGoesToExplorer");
            Assert.True(IsExplorer(@"\\?\C:\"), "extendedPrefixGoesToExplorer");
            Assert.True(IsExplorer(file), "existingFileGoesToExplorer");
            Assert.True(IsExplorer(zip), "zipGoesToExplorer");
            Assert.True(IsExplorer(Path.Combine(root, "absent.zip")), "missingZipGoesToExplorer");
            Assert.True(IsExplorer(Path.Combine(root, "absent")), "nonexistentFolderGoesToExplorer");
            Assert.True(IsOwn(dir, dir), "existingFolderIsOwn");
            Assert.True(IsOwn("C:", @"C:\"), "driveLetterGetsRootSlash");
            Assert.True(IsOwn(@"C:\", @"C:\"), "driveRootStaysRoot");
            Assert.True(IsOwn("\"" + dir + "\"", dir), "quotedPathIsOwn");
            Assert.True(IsOwn(dir + @"\", dir), "trailingSlashIsRemoved");
            Assert.True(IsOwn(dir.Replace('\\', '/') + "/", dir), "forwardSlashIsNormalized");
            Assert.True(IsOwn("", profile), "emptyIsUserProfile");
            Assert.True(IsOwn(null, profile), "nullIsUserProfile");
            Assert.True(IsExplorer("\"shell:Downloads\""), "originalArgIsKeptForExplorer");
            // Drive / share roots arrive with a dangling quote ("<exe>" "%1" with %1 = C:\)
            Assert.True(IsOwn("C:\"", @"C:\"), "rootCQuote");
            Assert.True(PathRouter.Clean("D:\"") == @"D:\", "rootDQuoteCleaned");
            Assert.True(Directory.Exists(@"Q:\") || IsExplorer("Q:\"", @"Q:\"), "rootUnusedDriveGoesToExplorerNormalised");
            Assert.True(IsExplorer("\\\\srv\\share\"", @"\\srv\share\"), "uncRootQuoteGetsBackslash");
            Assert.True(PathRouter.Clean("\\\\srv\\share\"") == @"\\srv\share\" && PathRouter.Clean(@"\\srv\share\sub\") == @"\\srv\share\sub\", "uncRootCleanDirect");
            Assert.True(Directory.Exists(@"C:\Program Files") && IsOwn("C:\\Program Files\"", @"C:\Program Files"), "programFilesDanglingQuote");
            Assert.True(PathRouter.Clean(@"C:\Users\x") == @"C:\Users\x" && PathRouter.Clean("\"C:\\a b\"") == "\"C:\\a b\"" && PathRouter.Clean(null) == "", "normalPathUnchanged");
            Assert.True(PathRouter.Clean("a\"\"") == "a\"", "onlyOneQuoteStripped");
            Assert.True(IsOwn(dir, dir), "normalFolderStillOwn");
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }
}
