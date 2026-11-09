using System;

// Aides pour les erreurs ignorées exprès : l'appel nomme l'intention, là où un catch vide ne dit rien à un relecteur.
// Espace de noms global : le fichier est lié tel quel à Winlestia.Files (voir Winlestia.Files.csproj).
internal static class Safe
{
    // Nettoyage au mieux (fichier temporaire, désabonnement, libération COM) : s'il échoue, il n'y a plus rien à réparer.
    public static void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception)
        {
            // Volontaire : un nettoyage raté (objet déjà libéré, fichier verrouillé) ne doit pas masquer l'opération principale.
        }
    }

    // Libération au mieux : un objet déjà libéré ou une ressource native disparue ne se libère pas deux fois.
    public static void Dispose(IDisposable? disposable) => Try(() => disposable?.Dispose());
}
