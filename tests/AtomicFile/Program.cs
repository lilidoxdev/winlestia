// C#12 / AtomicFile: crash-safe writes of durable user data (C# helper + frontend/atomicfile.py via an isolated python -I script).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Winlestia.Shared;

class Test
{
    static string? Py(string script, params string[] args)
    {
        string py =
            Environment.GetEnvironmentVariable("WINLESTIA_TEST_PYTHON")
            ?? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Winlestia\Caelestia-Windows\.venv\Scripts\python.exe");
        if (!File.Exists(py))
            return null;
        var psi = new ProcessStartInfo(py)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("-I");
        psi.ArgumentList.Add(script);
        foreach (var a in args)
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        string o = p.StandardOutput.ReadToEnd();
        p.StandardError.ReadToEnd();
        p.WaitForExit(30000);
        return p.ExitCode == 0 ? o.Trim() : "exit" + p.ExitCode;
    }

    static int Main()
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
        // python helper
        string script = Path.Combine(dir, "t.py");
        string pf = Path.Combine(dir, "py.json");
        File.WriteAllText(
            script,
            "import sys\nsys.path.insert(0,sys.argv[1])\nimport atomicfile,os\np=sys.argv[2]\natomicfile.write_text(p,'{\"é\":1}')\natomicfile.write_text(p,'{\"é\""
                + ":2}')\nok=open(p,'rb').read()=='{\"é\":2}'.encode('utf-8') and not __import__('glob').glob(os.path.dirname(p)+'/*.tmp')\nclass B(str):\n    def "
                + "encode(self,*a):raise RuntimeError('x')\ntry:atomicfile.write_text(p,B('z'))\nexcept RuntimeError:pass\nok=ok and open(p,'rb').read()=='{\"é\":2}'.encode('utf-8') "
                + "and not __import__('glob').glob(os.path.dirname(p)+'/*.tmp')\nprint('OK' if ok else 'BAD')\n"
        );
        string front = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "frontend")
        );
        string? o = Py(script, front, pf);
        if (o == null)
            Console.WriteLine("SKIP python not found");
        else
            r["pythonHelper"] = o == "OK";
        try
        {
            Directory.Delete(dir, true);
        }
        catch { }
        int ok = 0;
        foreach (var v in r.Values)
            if (v)
                ok++;
        foreach (var k in r)
            if (!k.Value)
                Console.WriteLine("FAIL " + k.Key);
        Console.WriteLine("TOTAL " + ok + "/" + r.Count);
        return ok == r.Count ? 0 : 1;
    }
}
