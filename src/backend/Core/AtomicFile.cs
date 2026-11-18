// AtomicFile : remplacement à l'épreuve des plantages de données utilisateur durables (écriture d'un <path>.<guid>.tmp unique, flush sur
// disque, puis permutation).

using System;
using System.IO;
using System.Text;

namespace Winlestia.Shared;

public static class AtomicFile
{
    // Le texte est écrit avec le préambule propre à l'encodage : Encoding.UTF8 conserve donc son BOM et null signifie UTF-8 sans BOM
    // (comportement par défaut de File.WriteAllText).
    public static void WriteAllText(string path, string content, Encoding? encoding = null, string? backupPath = null)
    {
        var enc = encoding ?? new UTF8Encoding(false);
        byte[] preamble = enc.GetPreamble(),
            body = enc.GetBytes(content);
        Write(
            path,
            s =>
            {
                s.Write(preamble, 0, preamble.Length);
                s.Write(body, 0, body.Length);
            },
            backupPath
        );
    }

    public static void WriteAllBytes(string path, byte[] bytes, string? backupPath = null) =>
        Write(path, s => s.Write(bytes, 0, bytes.Length), backupPath);

    // Le contenu précédent reste intact tant que le nouveau n'est pas entièrement sur disque ; en cas d'échec, le fichier tmp est supprimé
    // et l'exception est relancée.
    public static void Write(string path, Action<Stream> writer, string? backupPath = null)
    {
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                writer(fs);
                fs.Flush(true);
            }
            try
            {
                File.Replace(tmp, path, backupPath);
            }
            catch (FileNotFoundException)
            {
                File.Move(tmp, path, true);
            }
        }
        catch
        {
            try
            {
                File.Delete(tmp);
            }
            catch { }
            throw;
        }
    }
}
