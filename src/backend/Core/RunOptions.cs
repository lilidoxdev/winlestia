// C# 12 / RunOptions.cs / Windows 11. Options de lancement figées une fois pour toutes dans Program.Main.
using System.Collections.Generic;
using System.Linq;
namespace PanneauxGlass;

// Normal : session réelle. Review : le frontend tourne en aperçu. Verify : le frontend tourne en vérification.
// Les deux derniers modes servent à la QA : ils ne doivent ni tuiler les fenêtres du bureau ni afficher d'erreur fatale.
internal enum LaunchMode
{
    Normal,
    Review,
    Verify
}

// Immuable : le pont et le coordinateur reçoivent l'instance au lieu de lire des drapeaux statiques mutables.
internal sealed record RunOptions(LaunchMode Mode = LaunchMode.Normal)
{
    internal static readonly RunOptions Default = new();

    internal bool IsAutomated => Mode != LaunchMode.Normal;

    // --qt-verify l'emporte sur --qt-review quand les deux sont donnés (même priorité que le lancement du frontend).
    internal static RunOptions FromArgs(IReadOnlyCollection<string> args)
        => new(args.Contains("--qt-verify") ? LaunchMode.Verify
            : args.Contains("--qt-review") ? LaunchMode.Review
            : LaunchMode.Normal);
}
