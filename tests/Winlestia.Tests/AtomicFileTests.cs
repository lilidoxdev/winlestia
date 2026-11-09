// C#12 / AtomicFile: crash-safe writes of durable user data (C# helper + frontend/atomicfile.py via an isolated python -I script).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Winlestia.Shared;

namespace Winlestia.Tests;

public class AtomicFileTests
{
    [Fact]
    public void AllChecks()
    {
        var r = new Dictionary<string, bool>();
        string dir = Path.Combine(
            Environment.GetEnvironmentVariable("WINLESTIA_DATA_DIR")
                ?? Path.Combine(Path.GetTempPath(), "winlestia-test"),
            "atomicfile-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(dir);
        string f = Path.Combine(dir, "state.json");
        bool NoTmp() => Directory.GetFiles(dir, "*.tmp", SearchOption.AllDirectories).Length == 0;
        AtomicFile.WriteAllText(f, "{\"a\":1}");
        r["newFile"] = File.ReadAllText(f) == "{\"a\":1}";
        r["tmpRemovedAfterCreate"] = NoTmp();
        AtomicFile.WriteAllText(f, "{\"a\":2}");
        r["overwrite"] = File.ReadAllText(f) == "{\"a\":2}" && NoTmp();
        AtomicFile.WriteAllText(f, "é€", null);
        var raw = File.ReadAllBytes(f);
        r["defaultUtf8NoBom"] = raw.SequenceEqual(new UTF8Encoding(false).GetBytes("é€"));
        AtomicFile.WriteAllText(f, "é€", Encoding.UTF8);
        raw = File.ReadAllBytes(f);
        r["utf8BomPreserved"] =
            raw.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF })
            && Encoding.UTF8.GetString(raw, 3, raw.Length - 3) == "é€";
        AtomicFile.WriteAllText(f, "hi", Encoding.Unicode);
        raw = File.ReadAllBytes(f);
        r["utf16Preserved"] =
            raw.Take(2).SequenceEqual(new byte[] { 0xFF, 0xFE })
            && Encoding.Unicode.GetString(raw, 2, raw.Length - 2) == "hi";
        AtomicFile.WriteAllBytes(f, new byte[] { 1, 2, 3, 0, 255 });
        r["bytes"] = File.ReadAllBytes(f).SequenceEqual(new byte[] { 1, 2, 3, 0, 255 });
        // failure mid-way: original untouched, tmp cleaned, exception propagates
        AtomicFile.WriteAllText(f, "GOOD");
        bool threw = false;
        try
        {
            AtomicFile.Write(
                f,
                s =>
                {
                    s.Write(new byte[] { 66, 65, 68 }, 0, 3);
                    throw new IOException("boom");
                }
            );
        }
        catch (IOException)
        {
            threw = true;
        }
        r["failureRethrown"] = threw;
        r["failureKeepsOriginal"] = File.ReadAllText(f) == "GOOD";
        r["failureRemovesTmp"] = NoTmp();
        // failure to swap (target is a directory): original intact elsewhere, tmp cleaned
        string dirTarget = Path.Combine(dir, "adir");
        Directory.CreateDirectory(dirTarget);
        threw = false;
        try
        {
            AtomicFile.WriteAllText(dirTarget, "x");
        }
        catch (Exception)
        {
            threw = true;
        }
        r["swapFailureThrowsAndCleans"] = threw && NoTmp() && Directory.Exists(dirTarget);
        // backup path keeps previous content
        string bak = f + ".bak";
        AtomicFile.WriteAllText(f, "NEW", null, bak);
        r["backupKeepsPrevious"] = File.ReadAllText(bak) == "GOOD" && File.ReadAllText(f) == "NEW";
        try
        {
            Directory.Delete(dir, true);
        }
        catch { }
        Assert.True(r.Values.All(v => v), string.Join(", ", r.Where(x => !x.Value).Select(x => x.Key)));
    }
}
