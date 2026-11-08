// MainWindow : durées d'attente nommées des auto-tests (le shell n'expose aucun événement de fin fiable).

namespace WinlestiaFiles;

public sealed partial class MainWindow
{
    // Attente de test : un tick du dispatcher : l'UI traite ses messages en attente.
    internal const int PauseTickMs = 100;
    // Attente de test : un rendu WPF complet après un changement d'affichage.
    internal const int PauseRenduMs = 150;
    // Attente de test : la plus courte attente observable (minuteur ou relâchement d'un verrou).
    internal const int PauseBreveMs = 200;
    // Attente de test : une animation courte de l'interface.
    internal const int PauseAnimationMs = 250;
    // Attente de test : une animation longue ou un survol temporisé.
    internal const int PauseAnimationLongueMs = 300;
    // Attente de test : une transition de page ou de dialogue.
    internal const int PauseTransitionMs = 400;
    // Attente de test : les observateurs de fichiers et les temporisations antirebond ont eu le temps de tirer.
    internal const int PauseStabilisationMs = 500;
    // Attente de test : dépasser les temporisations antirebond de l'application.
    internal const int PauseDebounceMs = 600;
    // Attente de test : la page est entièrement dessinée avant d'être manipulée.
    internal const int PauseDessinMs = 700;
    // Attente de test : la mise en page asynchrone est terminée.
    internal const int PauseMiseEnPageMs = 800;
    // Attente de test : une opération asynchrone brève est terminée.
    internal const int PauseSecondeMs = 1000;
    // Attente de test : une petite opération de fichier asynchrone est terminée.
    internal const int PauseOperationMs = 1500;
    // Attente de test : la copie d'un petit fichier est terminée.
    internal const int PauseCopieMs = 2000;
    // Attente de test : une sonde périodique de l'application a eu le temps de passer.
    internal const int PauseSondeMs = 2500;
    // Attente de test : un scan en arrière-plan (palette, fond d'écran) est terminé.
    internal const int PauseScanMs = 3000;
    // Attente de test : le chargement initial d'un dossier volumineux est terminé.
    internal const int PauseChargementMs = 4000;
    // Attente de test : une opération lente (réseau local, disque) est terminée.
    internal const int PauseLongueMs = 5000;
    // Attente de test : délai maximal d'un événement attendu avant d'échouer le test.
    const int TimeoutEvenementMs = 8000;
    // Attente de test : une opération volumineuse est terminée.
    internal const int PauseTresLongueMs = 10000;
    // Attente de test : délai maximal d'une attente moyenne avant d'échouer le test.
    const int TimeoutMoyenMs = 15000;
    // Attente de test : délai maximal d'une opération longue avant d'échouer le test.
    const int TimeoutLongMs = 25000;
    // Attente de test : un transfert complet est terminé.
    internal const int PauseTransfertMs = 30000;
    // Attente de test : délai maximal d'une opération de fichier avant d'échouer le test.
    const int TimeoutOperationMs = 40000;
    // Attente de test : délai maximal d'une suppression avant d'échouer le test.
    const int TimeoutSuppressionMs = 45000;
    // Attente de test : délai maximal d'un scénario de conflit avant d'échouer le test.
    const int TimeoutConflitMs = 60000;
}
