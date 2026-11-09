// language: C# 12, file: WinjetPackage.cs, target: Windows 11 / .NET 8, paquet Winjet validé (manifeste + définition + dossier)
using System;
using System.Collections.Generic;
using System.IO;
namespace PanneauxGlass.Winjets;

internal sealed class WinjetPackage
{
    internal WinjetPackage(WinjetManifest manifest, WinjetDefinition definition, string dir, bool builtin)
    {
        Manifest = manifest;
        Definition = definition;
        Dir = dir;
        Builtin = builtin;
    }

    internal WinjetManifest Manifest { get; }
    internal WinjetDefinition Definition { get; }
    internal string Dir { get; }
    internal bool Builtin { get; }
    internal string Id => Manifest.Id;

    // Chemin de preview.png s'il existe, sinon chaîne vide.
    internal string Preview
    {
        get
        {
            string p = Path.Combine(Dir, "preview.png");
            return File.Exists(p) ? p : "";
        }
    }

    // Charge un dossier de paquet déjà en place (paquet inclus ou installé) ; null si invalide (erreurs dans `errors`).
    internal static WinjetPackage? Load(string dir, bool builtin, Version current, out IReadOnlyList<string> errors)
    {
        var result = WinjetValidator.ValidateDirectory(dir, current);
        errors = result.Errors;
        return result.Package is { } p ? new WinjetPackage(p.Manifest, p.Definition, dir, builtin) : null;
    }
}
