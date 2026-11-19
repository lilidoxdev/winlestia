// Dossier temporaire de test, supprime a la fin du using. Remplace les `catch { }` de nettoyage : un echec de
// suppression reste visible (apres quelques essais) au lieu d'etre avale.
using System;
using System.IO;
using System.Threading;
namespace Winlestia.Tests;
internal sealed class TempDir : IDisposable
{
    public string Path { get; }
    public TempDir(string? name = null)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), name ?? "WinlestiaTest-" + Guid.NewGuid().ToString("N"));
        if (Directory.Exists(Path))
            Delete(Path);
        Directory.CreateDirectory(Path);
    }
    public string Combine(params string[] parts) => System.IO.Path.Combine(Path, System.IO.Path.Combine(parts));
    public override string ToString() => Path;
    public void Dispose() => Delete(Path);
    /// <summary>Supprime un dossier en levant les attributs lecture seule ; trois essais (antivirus, indexeur).</summary>
    internal static void Delete(string dir)
    {
        for (int attempt = 1; Directory.Exists(dir); attempt++)
        {
            foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
                entry.Attributes = FileAttributes.Normal;
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException) when (attempt < 3)
            {
                Thread.Sleep(100 * attempt);
            }
        }
    }
}
