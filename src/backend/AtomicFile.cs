// AtomicFile: crash-safe replacement of durable user data (write a unique <path>.<guid>.tmp, flush to disk, then swap it in).

using System;
using System.IO;
using System.Text;

namespace Winlestia.Shared;

public static class AtomicFile {
    // Text is written with the encoding's own preamble, so Encoding.UTF8 keeps its BOM and null means UTF-8 without BOM (File.WriteAllText default).
    public static void WriteAllText(string path, string content, Encoding? encoding = null, string? backupPath = null) {
        var enc = encoding ?? new UTF8Encoding(false);
        byte[] preamble = enc.GetPreamble(), body = enc.GetBytes(content);
        Write(path, s => { s.Write(preamble, 0, preamble.Length); s.Write(body, 0, body.Length); }, backupPath);
    }

    public static void WriteAllBytes(string path, byte[] bytes, string? backupPath = null) => Write(path, s => s.Write(bytes, 0, bytes.Length), backupPath);

    // The previous content stays untouched until the new content is fully on disk; on any failure the tmp file is removed and the exception rethrown.
    public static void Write(string path, Action<Stream> writer, string? backupPath = null) {
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None)) {
                writer(fs);
                fs.Flush(true);
            }
            try { File.Replace(tmp, path, backupPath); }
            catch (FileNotFoundException) { File.Move(tmp, path, true); }
        } catch {
            try { File.Delete(tmp); } catch { }
            throw;
        }
    }
}
