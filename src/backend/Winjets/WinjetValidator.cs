// language: C# 12, file: WinjetValidator.cs, target: Windows 11 / .NET 8, validation complète d'un paquet Winjet (ZIP ou dossier)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
namespace PanneauxGlass.Winjets;

// Résultat d'une validation. Quand le paquet venait d'un ZIP ou d'un dossier source, `StagingDir` est la copie contrôlée : le
// propriétaire la déplace (Release) ou la supprime (Dispose).
internal sealed class WinjetValidation : IDisposable
{
    internal WinjetValidation(IReadOnlyList<string> errors, WinjetPackage? package, string? staging)
    {
        Errors = errors;
        Package = package;
        StagingDir = staging;
    }

    internal IReadOnlyList<string> Errors { get; }
    internal WinjetPackage? Package { get; }
    internal string? StagingDir { get; private set; }
    internal bool Ok => Errors.Count == 0 && Package != null;

    internal string Release()
    {
        string dir = StagingDir ?? throw new InvalidOperationException("Aucune zone de préparation.");
        StagingDir = null;
        return dir;
    }

    public void Dispose()
    {
        if (StagingDir == null)
            return;
        try
        {
            Directory.Delete(StagingDir, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Store.Log("Winjets : nettoyage impossible : " + ex.Message); }
        StagingDir = null;
    }
}

internal static class WinjetValidator
{
    // Version de Winlestia confrontée à compatibility.winlestia.
    internal static Version CurrentVersion()
    {
        string text = (typeof(WinjetValidator).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "").Split('+')[0];
        return WinjetSemVer.TryParseCore(text, out var v) ? v : new Version(0, 9, 2);
    }

    // Valide un ZIP ou un dossier source : copie contrôlée dans `tempRoot` (créé au besoin), puis validation de la copie.
    internal static WinjetValidation Validate(string path, Version? current = null, string? tempRoot = null)
    {
        var list = new List<string>();
        var issues = new WinjetIssues(list);
        string stage = Path.Combine(tempRoot ?? Path.Combine(Path.GetTempPath(), "winlestia-winjets"), "stage-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(stage);
            if (!WinjetArchive.Stage(path, stage, issues))
            {
                Cleanup(stage);
                return new WinjetValidation(list, null, null);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Cleanup(stage);
            list.Add("Préparation impossible : " + ex.Message);
            return new WinjetValidation(list, null, null);
        }
        var inner = ValidateDirectory(stage, current);
        if (!inner.Ok)
        {
            Cleanup(stage);
            return new WinjetValidation(inner.Errors, null, null);
        }
        return new WinjetValidation(inner.Errors, inner.Package, stage);
    }

    // Valide un dossier de paquet en place (paquet inclus, installé, ou copie déjà contrôlée).
    internal static WinjetValidation ValidateDirectory(string dir, Version? current = null)
    {
        var list = new List<string>();
        var issues = new WinjetIssues(list);
        var version = current ?? CurrentVersion();

        issues.Scope = "manifest.json";
        var manifestJson = issues.ReadJson(Path.Combine(dir, "manifest.json"), "manifest.json");
        issues.Scope = "widget.json";
        var widgetJson = issues.ReadJson(Path.Combine(dir, "widget.json"), "widget.json");
        WinjetManifest? manifest = manifestJson is { } m ? WinjetManifest.Parse(m, version, issues) : null;
        WinjetDefinition? definition = manifest != null && widgetJson is { } w ? WinjetDefinition.Parse(w, manifest, dir, issues) : null;
        CheckFiles(dir, issues);
        if (issues.Any || manifest == null || definition == null)
            return new WinjetValidation(list, null, null);
        return new WinjetValidation(list, new WinjetPackage(manifest, definition, dir, false), null);
    }

    static void Cleanup(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Store.Log("Winjets : nettoyage impossible : " + ex.Message); }
    }

    // Contenu réel des fichiers : un « .png » doit être un PNG, un « .json » doit être du JSON, preview.png ≤ 1600×1000.
    static void CheckFiles(string dir, WinjetIssues issues)
    {
        issues.Scope = "";
        if (!Directory.Exists(dir))
            return;
        foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(dir, file).Replace('\\', '/');
            string ext = Path.GetExtension(file).ToLowerInvariant();
            try
            {
                if (rel is "manifest.json" or "widget.json")
                    continue;
                if (ext == ".json")
                {
                    var length = new FileInfo(file).Length;
                    if (length > WinjetLimits.MaxJsonBytes)
                        issues.Add("", rel + " : JSON trop gros (maximum " + WinjetLimits.MaxJsonBytes / 1024 + " Kio).");
                    else
                        JsonDocument.Parse(File.ReadAllBytes(file), new JsonDocumentOptions { MaxDepth = WinjetLimits.MaxJsonDepth }).Dispose();
                }
                else if (WinjetLimits.ImageExtensions.Contains(ext))
                    CheckImage(file, rel, ext, issues);
            }
            catch (JsonException)
            {
                issues.Add("", rel + " : JSON invalide ou trop profond (profondeur maximale " + WinjetLimits.MaxJsonDepth + ").");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add("", rel + " : lecture impossible (" + ex.Message + ").");
            }
        }
    }

    static void CheckImage(string file, string rel, string ext, WinjetIssues issues)
    {
        var head = new byte[32];
        int n;
        using (var fs = File.OpenRead(file))
            n = fs.Read(head, 0, head.Length);
        bool png = n >= 24 && head[0] == 0x89 && head[1] == 'P' && head[2] == 'N' && head[3] == 'G';
        bool ok = ext switch
        {
            ".png" => png,
            ".jpg" or ".jpeg" => n >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF,
            ".gif" => n >= 6 && head[0] == 'G' && head[1] == 'I' && head[2] == 'F' && head[3] == '8',
            ".webp" => n >= 12 && head[0] == 'R' && head[1] == 'I' && head[2] == 'F' && head[3] == 'F' && head[8] == 'W' && head[9] == 'E',
            _ => false
        };
        if (!ok)
        {
            issues.Add("", rel + " : le contenu ne correspond pas à l'extension " + ext + ".");
            return;
        }
        if (!TryDimensions(file, ext, out int w, out int h))
        {
            issues.Add("", rel + " : dimensions de l'image illisibles.");
            return;
        }
        bool preview = string.Equals(rel, "preview.png", StringComparison.OrdinalIgnoreCase);
        int maxW = preview ? WinjetLimits.MaxPreviewWidth : WinjetLimits.MaxImageSide;
        int maxH = preview ? WinjetLimits.MaxPreviewHeight : WinjetLimits.MaxImageSide;
        if (w <= 0 || h <= 0 || w > maxW || h > maxH)
            issues.Add("", rel + " : dimensions " + w + "×" + h + " hors limites (maximum " + maxW + "×" + maxH + ").");
    }

    // Lecture de l'en-tête seulement (pas de décodage) : l'image est refusée avant qu'un décodeur n'alloue sa mémoire.
    internal static bool TryDimensions(string file, string ext, out int w, out int h)
    {
        w = h = 0;
        try
        {
            using var fs = File.OpenRead(file);
            var b = new byte[Math.Min(fs.Length, 262144)];
            int n = fs.Read(b, 0, b.Length);
            switch (ext)
            {
                case ".png" when n >= 24:
                    w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
                    h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
                    return true;
                case ".gif" when n >= 10:
                    w = b[6] | (b[7] << 8);
                    h = b[8] | (b[9] << 8);
                    return true;
                case ".webp" when n >= 30:
                    if (b[12] == 'V' && b[13] == 'P' && b[14] == '8' && b[15] == 'X')
                    {
                        w = 1 + (b[24] | (b[25] << 8) | (b[26] << 16));
                        h = 1 + (b[27] | (b[28] << 8) | (b[29] << 16));
                        return true;
                    }
                    if (b[12] == 'V' && b[13] == 'P' && b[14] == '8' && b[15] == 'L')
                    {
                        w = 1 + (b[21] | ((b[22] & 0x3F) << 8));
                        h = 1 + ((b[22] >> 6) | (b[23] << 2) | ((b[24] & 0x0F) << 10));
                        return true;
                    }
                    if (b[12] == 'V' && b[13] == 'P' && b[14] == '8' && b[15] == ' ')
                    {
                        w = (b[26] | (b[27] << 8)) & 0x3FFF;
                        h = (b[28] | (b[29] << 8)) & 0x3FFF;
                        return true;
                    }
                    return false;
                case ".jpg" or ".jpeg":
                    for (int i = 2; i + 9 < n;)
                    {
                        if (b[i] != 0xFF)
                        {
                            i++;
                            continue;
                        }
                        int m = b[i + 1];
                        if (m == 0xFF)
                        {
                            i++;
                            continue;
                        }
                        if (m is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
                        {
                            i += 2;
                            continue;
                        }
                        int len = (b[i + 2] << 8) | b[i + 3];
                        if (m is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
                        {
                            h = (b[i + 5] << 8) | b[i + 6];
                            w = (b[i + 7] << 8) | b[i + 8];
                            return true;
                        }
                        i += 2 + Math.Max(len, 2);
                    }
                    return false;
            }
        }
        catch (IOException) { }
        return false;
    }
}
