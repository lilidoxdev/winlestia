// C# 12, fichier Log.cs, cible Windows 11 / .NET 8, journal court de l'assistant (aucune donnée personnelle)
using System;
using System.IO;

namespace WinlestiaSensors;

internal static class Log
{
    const long MaxBytes = 64 * 1024;
    static readonly object gate = new();

    // Seulement dans Program Files (réservé aux administrateurs) ; jamais dans un dossier modifiable par un utilisateur.
    internal static string Folder => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "Winlestia", "Sensors");

    // Vrai pendant l'installation, avant la copie dans Program Files.
    internal static volatile bool Suspended;

    internal static string Path => System.IO.Path.Combine(Folder, "sensors.log");

    // Résumé sans message d'exception (peut contenir nom d'utilisateur ou chemins) : type et HRESULT.
    internal static string Describe(Exception ex) => ex.GetType().Name + " 0x" + ex.HResult.ToString("X8");

    internal static void Write(string message)
    {
        if (Suspended)
            return;
        lock (gate)
        {
            try
            {
                var dir = new DirectoryInfo(Folder);
                // Pas de dossier (avant la copie de l'installation) ou point d'analyse : on ne journalise pas.
                if (!dir.Exists || dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    return;
                string path = Path;
                var info = new FileInfo(path);
                if (info.Exists)
                {
                    if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        return;
                    if (info.Length > MaxBytes)
                        File.Delete(path);
                }
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine);
            }
            catch (Exception) { }
        }
    }
}
