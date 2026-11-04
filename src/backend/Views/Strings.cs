using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace PanneauxGlass.Views;

// User-visible UI text. French is the original product language (the French values below are the
// historical literals, unchanged); English is the fallback for every other language. The language is
// resolved once, on first use: language.json written by the Qt shell (key "language": auto|fr|en|es),
// then the Windows UI culture, then English. Spanish only exists where the Qt shell already has a
// translation; other keys fall back to English. Shared with Winlestia.Files (linked file): no
// dependency on the rest of the backend.
public static class Strings
{
    public static readonly string Language = Resolve(
        DefaultDataDir(),
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
    );

    // Same rule as Store.DataDir: WINLESTIA_DATA_DIR overrides %LOCALAPPDATA%\Panneaux (tests).
    public static string DefaultDataDir() =>
        Environment.GetEnvironmentVariable("WINLESTIA_DATA_DIR") is { Length: > 0 } d
            ? d
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Panneaux");

    // fr | en | es. The setting file lives in <dataDir>\Caelestia\language.json, same file as the Qt shell.
    public static string Resolve(string dataDir, string uiCulture)
    {
        try
        {
            string path = Path.Combine(dataDir, "Caelestia", "language.json");
            if (File.Exists(path))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (
                    doc.RootElement.ValueKind == JsonValueKind.Object
                    && doc.RootElement.TryGetProperty("language", out var v)
                    && v.ValueKind == JsonValueKind.String
                    && Normalize(v.GetString()) is { } chosen
                )
                    return chosen;
            }
        }
        catch { }
        return Normalize(uiCulture) ?? "en";
    }

    static string? Normalize(string? code) =>
        code?.Trim().ToLowerInvariant() switch
        {
            "fr" => "fr",
            "en" => "en",
            "es" => "es",
            _ => null,
        };

    public static string Get(string key, string language)
    {
        if (!Table.TryGetValue(key, out var t))
            return key;
        return language == "fr" ? t.Fr
            : language == "es" && !string.IsNullOrEmpty(t.Es) ? t.Es
            : t.En;
    }

    static string Get(string key) => Get(key, Language);

    public static IReadOnlyDictionary<string, (string Fr, string En, string? Es)> All => Table;

    static readonly Dictionary<string, (string Fr, string En, string? Es)> Table = new()
    {
        ["PreparingMeasurements"] = ("Préparation des mesures…", "Preparing measurements…", null),
        ["Memory"] = ("Mémoire", "Memory", "Memoria"),
        ["NetworkConnection"] = ("Connexion réseau", "Network connection", null),
        ["LIVEMEMORY"] = ("MÉMOIRE VIVE", "LIVE MEMORY", null),
        ["NETWORKLIVETHROUGHPUT"] = ("RÉSEAU · DÉBIT EN DIRECT", "NETWORK · LIVE THROUGHPUT", null),
        ["MeasurementsStopWhenYouLeaveThis"] = (
            "Les mesures s’arrêtent en quittant cette page. Une température absente est indiquée par —.",
            "Measurements stop when you leave this page. A missing temperature is shown as —.",
            null
        ),
        ["PreparingSensors"] = ("Préparation des capteurs…", "Preparing sensors…", null),
        ["Temperature"] = ("Température —", "Temperature —", null),
        ["GBUsed"] = (" Go utilisés", " GB used", null),
        ["LaunchPanneauxAtSignIn"] = ("Lancer Panneaux à l’ouverture de session", "Launch Panneaux at sign-in", null),
        ["GiveThisShortcutAName"] = ("Donne un nom à ce raccourci.", "Give this shortcut a name.", null),
        ["ThisSiteIsAlreadyPresent"] = ("Ce site est déjà présent.", "This site is already present.", null),
        ["PanneauxGlassDidNotStartThe"] = (
            "Panneaux Glass n’a pas démarré. Le secours reste disponible dans Documents\\SECOURS_BUREAU.exe.\n\n",
            "Panneaux Glass did not start. The rescue tool remains available in Documents\\SECOURS_BUREAU.exe.\n\n",
            null
        ),
        ["WindowsDidNotRespond"] = ("Windows n’a pas répondu.", "Windows did not respond.", null),
        ["CtrlAltShiftNumpad"] = ("Ctrl + Alt + Maj + pavé +", "Ctrl + Alt + Shift + numpad +", null),
        ["CtrlAltShiftNumpad2"] = ("Ctrl + Alt + Maj + pavé -", "Ctrl + Alt + Shift + numpad -", null),
        ["MatchingFolderSIncludingShownFolders"] = (
            " dossier(s) assorti(s), y compris les dossiers affichés.",
            " matching folder(s), including shown folders.",
            null
        ),
        ["Ignored"] = (" ignoré(s).", " ignored.", null),
        ["NavigationSettingSModifiedElsewhereKept"] = (
            " réglage(s) de navigation modifié(s) ailleurs, conservé(s).",
            " navigation setting(s) modified elsewhere, kept.",
            null
        ),
        ["OriginalIconsRestored"] = ("Icônes d’origine restaurées.", "Original icons restored.", null),
        ["ConflictSKept"] = (" conflit(s) conservé(s).", " conflict(s) kept.", null),
        ["FolderSKeptCheckTheLog"] = (
            " dossier(s) conservé(s), consulter le journal.",
            " folder(s) kept, check the log.",
            null
        ),
        ["WinlestiaFilesWasNotFoundOption"] = (
            "Winlestia Fichiers est introuvable : option non activée.",
            "Winlestia Files was not found: option not enabled.",
            null
        ),
        ["RestoreIncompleteTryAgain"] = (
            "Restauration incomplète, réessaie : ",
            "Restore incomplete, try again: ",
            null
        ),
        ["WinlestiaFilesWasNotFoundExplorer"] = (
            "Winlestia Fichiers est introuvable : Explorer rouvre à nouveau les dossiers.",
            "Winlestia Files was not found: Explorer opens folders again.",
            null
        ),
        ["WinlestiaFilesCrashedThreeTimesExplorer"] = (
            "Winlestia Fichiers a planté trois fois : Explorer rouvre à nouveau les dossiers.",
            "Winlestia Files crashed three times: Explorer opens folders again.",
            null
        ),
        ["FolderRestoreIncomplete"] = ("Restauration des dossiers incomplète : ", "Folder restore incomplete: ", null),
        ["Close"] = ("Fermer", "Close", "Cerrar"),
        ["FilesOver200MBFromLargest"] = (
            "Les fichiers de plus de 200 Mo, classés du plus lourd au plus léger",
            "Files over 200 MB, from largest to smallest",
            null
        ),
        ["ChooseAFolderToSeeIts"] = (
            "Choisis un dossier pour voir ses fichiers de plus de 200 Mo, du plus lourd au plus léger.",
            "Choose a folder to see its files over 200 MB, from largest to smallest.",
            null
        ),
        ["NothingIsDeletedFromPanneauxEach"] = (
            "Rien n’est supprimé depuis Panneaux : chaque ligne ouvre l’Explorateur sur le fichier.",
            "Nothing is deleted from Panneaux: each row opens Explorer on the file.",
            null
        ),
        ["ChooseTheFolderToAnalyze"] = ("Choisir le dossier à analyser", "Choose the folder to analyze", null),
        ["AnalysisCancelled"] = ("Analyse annulée", "Analysis cancelled", null),
        ["CreateAZIPArchive"] = ("Créer une archive ZIP", "Create a ZIP archive", "Crear un archivo ZIP"),
        ["SystemSounds"] = ("Sons système", "System sounds", null),
        ["SwitchHeadsetMicrophoneOrMuteThe"] = (
            "Change de casque, de micro ou coupe le micro sans passer par les réglages",
            "Switch headset, microphone or mute the microphone without going through settings",
            null
        ),
        ["NoActiveOutputDetected"] = ("Aucune sortie active détectée.", "No active output detected.", null),
        ["NoActiveMicrophoneDetected"] = ("Aucun microphone actif détecté.", "No active microphone detected.", null),
        ["UnmuteMicrophone"] = ("Réactiver le micro", "Unmute microphone", null),
        ["MicrophoneMuted"] = ("Micro coupé", "Microphone muted", null),
        ["MicrophoneUnmuted"] = ("Micro réactivé", "Microphone unmuted", null),
        ["OpenWindowsSettings"] = ("Ouvrir les réglages Windows", "Open Windows settings", null),
        ["TheChangeAppliesImmediatelyToAll"] = (
            "Le changement est immédiat pour toutes les applications.",
            "The change applies immediately to all applications.",
            null
        ),
        ["AudioSettingUnavailable"] = ("Réglage audio indisponible : ", "Audio setting unavailable: ", null),
        ["WindowsRefusedAccessOpenWindowsSettings"] = (
            "Windows a refusé l’accès. Ouvre les réglages Windows pour changer de sortie.",
            "Windows refused access. Open Windows settings to change the output.",
            null
        ),
        ["DisplayBrightness"] = ("Luminosité de l’écran", "Display brightness", null),
        ["ThisDisplayDoesNotExposeA"] = (
            "Cet écran ne fournit pas de réglage DDC/CI à Windows.",
            "This display does not expose a DDC/CI setting to Windows.",
            null
        ),
        ["CPUTemperatureSensorDriverRequired"] = (
            "Température CPU : pilote de capteurs requis",
            "CPU temperature: sensor driver required",
            null
        ),
        ["SleepCouldNotBeTriggeredOn"] = (
            "La mise en veille n’a pas pu être déclenchée sur ce PC.",
            "Sleep could not be triggered on this PC.",
            null
        ),
        ["EmptyTheRecycleBinTheFiles"] = (
            "Vider la corbeille ?\n\nLes fichiers qu’elle contient seront supprimés définitivement, sans passer par le Bureau.",
            "Empty the Recycle Bin?\n\nThe files it contains will be permanently deleted, without going through the Desktop.",
            null
        ),
        ["RecycleBinEmptied"] = ("Corbeille vidée", "Recycle Bin emptied", null),
        ["TheRecycleBinIsAlreadyEmpty"] = ("La corbeille est déjà vide", "The Recycle Bin is already empty", null),
        ["Restart"] = ("Redémarrer", "Restart", "Reiniciar"),
        ["ShutDown"] = ("Éteindre", "Shut down", "Apagar"),
        ["Copied"] = (" copié", " copied", null),
        ["CouldNotBeLaunched"] = (" » n’a pas pu être lancée.\n", " » could not be launched.\n", null),
        ["NotConnected"] = ("Non connecté", "Not connected", "No conectado"),
        ["WindowsDoesNotAllowThisCommand"] = (
            "Windows n’autorise pas cette commande. Ouvre les réglages de connexion.",
            "Windows does not allow this command. Open the connection settings.",
            null
        ),
        ["PanneauxSideHandle"] = ("Panneaux — poignée latérale", "Panneaux — side handle", null),
        ["PanneauxGlassExplorerOnTheLeft"] = (
            "Panneaux Glass — explorer à gauche",
            "Panneaux Glass — explorer on the left",
            null
        ),
        ["PanneauxGlassExplorerOnTheRight"] = (
            "Panneaux Glass — explorer à droite",
            "Panneaux Glass — explorer on the right",
            null
        ),
        ["ShowPCStatus"] = ("Afficher l’état du PC", "Show PC status", null),
        ["OpenTheSpace"] = ("Ouvrir l’espace", "Open the space", null),
        ["RefreshShortcuts"] = ("Actualiser les accès", "Refresh shortcuts", null),
        ["RestoreTheOrganizedDesktop"] = ("Retrouver le Bureau organisé", "Restore the organized Desktop", null),
        ["OpenYourSpace"] = ("Ouvrir ton espace · ", "Open your space · ", null),
        ["OpenPanneauxGlass"] = ("Ouvrir Panneaux Glass", "Open Panneaux Glass", null),
        ["OpenTheQuickCommand"] = ("Ouvrir la commande rapide", "Open the quick command", null),
        ["BackToHome"] = ("Revenir à l’accueil", "Back to home", null),
        ["RestoreThePanelLayout"] = ("Rétablir la disposition des panneaux", "Restore the panel layout", null),
        ["TheIndependentRescueToolIsMissing"] = (
            "Le secours indépendant est absent.\nDocuments\\SECOURS_BUREAU.exe peut également être utilisé.",
            "The independent rescue tool is missing.\nDocuments\\SECOURS_BUREAU.exe can also be used.",
            null
        ),
        ["TheShortcut"] = ("L’accès « ", "The shortcut « ", null),
        ["ThisShortcutUsesAWindowsLaunch"] = (
            "Cet accès utilise un protocole de lancement Windows.",
            "This shortcut uses a Windows launch protocol.",
            null
        ),
        ["AddAFolderToYourSpace"] = ("Ajouter un dossier à ton espace", "Add a folder to your space", null),
        ["MyShortcuts"] = ("Mes accès", "My shortcuts", null),
        ["TheTaskbarRescueDidNotStart"] = (
            "Le secours de la barre n’a pas démarré. La barre Windows reste disponible.",
            "The taskbar rescue did not start. The Windows taskbar remains available.",
            null
        ),
        ["YourTaskbar"] = ("Ta barre des tâches", "Your taskbar", null),
        ["RestoreTheWindowsTaskbar"] = (
            "Rétablir la barre Windows",
            "Restore the Windows taskbar",
            "Restablecer la barra de Windows"
        ),
        ["OpenNewWindow"] = ("Ouvrir une nouvelle fenêtre", "Open new window", "Abrir nueva ventana"),
        ["UnpinFromTheTaskbar"] = ("Détacher de la barre", "Unpin from the taskbar", null),
        ["PinToTheTaskbar"] = ("Épingler à la barre", "Pin to the taskbar", null),
        ["NoWindowToPin"] = ("Aucune fenêtre à épingler", "No window to pin", null),
        ["WindowsRefusedThisChange"] = ("Windows a refusé ce changement", "Windows refused this change", null),
        ["TidiedNormally"] = ("Rangée normalement · ", "Tidied normally · ", null),
        ["NotificationsRestored"] = ("Notifications rétablies", "Notifications restored", null),
        ["NotificationsMuted"] = ("Notifications coupées", "Notifications muted", null),
        ["RestartWindowsExplorerTheTaskbarAnd"] = (
            "Redémarrer l’Explorateur Windows ?\n\nLa barre des tâches et le Bureau vont clignoter, tes fenêtres restent ouvertes.",
            "Restart Windows Explorer?\n\nThe taskbar and Desktop will flicker, your windows stay open.",
            null
        ),
        ["ExplorerRestarted"] = ("Explorateur redémarré", "Explorer restarted", null),
        ["CollapsePCStatus"] = ("Replier l’état du PC", "Collapse PC status", null),
        ["N1OtherWindow"] = ("+1 autre fenêtre", "+1 other window", null),
        ["OtherWindows"] = (" autres fenêtres", " other windows", null),
        ["DragToMoveThisPanel"] = ("Glisser pour déplacer ce panneau", "Drag to move this panel", null),
        ["ThisWindowHasNoLaunchableProgram"] = (
            "Cette fenêtre n’a pas de programme lançable.",
            "This window has no launchable program.",
            null
        ),
        ["WindowsRefusedToCloseThisWindow"] = (
            "Windows a refusé la fermeture de cette fenêtre.",
            "Windows refused to close this window.",
            null
        ),
        ["SomeWindowsCouldNotBeClosed"] = (
            "Certaines fenêtres n’ont pas pu être fermées.",
            "Some windows could not be closed.",
            null
        ),
        ["StopRefusedForThisProcess"] = ("Arrêt refusé pour ce processus.", "Stop refused for this process.", null),
        ["FilesCopiedToTheClipboard"] = (
            "Fichiers copiés dans le presse-papiers.",
            "Files copied to the clipboard.",
            "Archivos copiados al portapapeles."
        ),
        ["ZIPArchiveCreated"] = ("Archive ZIP créée.", "ZIP archive created.", "Archivo ZIP creado."),
        ["ChooseTheFolderToCustomize"] = ("Choisir le dossier à personnaliser", "Choose the folder to customize", null),
        ["CouldNotOpenThisFile"] = (
            "Impossible d’ouvrir ce fichier.",
            "Could not open this file.",
            "No se pudo abrir este archivo."
        ),
        ["WinlestiaCouldNotStartItsInterface"] = (
            "Winlestia n'a pas pu démarrer son interface.\n\n",
            "Winlestia could not start its interface.\n\n",
            null
        ),
        ["TheIncidentDetailsAreIn"] = ("Le détail de l'incident est dans :\n", "The incident details are in:\n", null),
        ["TheWindowsTaskbarHasBeenRestored"] = (
            "La barre des tâches Windows a été rétablie. Réinstalle Winlestia si cela se reproduit.",
            "The Windows taskbar has been restored. Reinstall Winlestia if this happens again.",
            null
        ),
        ["TheQtEngineDidNotStart"] = (
            "Le moteur Qt n'a pas démarré. La barre Windows est rétablie.",
            "The Qt engine did not start. The Windows taskbar is restored.",
            null
        ),
        ["TheQtEngineStoppedTheWindows"] = (
            "Le moteur Qt s'est arrêté. La barre Windows est rétablie.",
            "The Qt engine stopped. The Windows taskbar is restored.",
            null
        ),
        ["CapturePinned"] = ("Capture épinglée", "Capture pinned", "Captura fijada"),
        ["TextCopiedToTheClipboard"] = (
            "Texte copié dans le presse-papiers",
            "Text copied to the clipboard",
            "Texto copiado al portapapeles"
        ),
        ["PanneauxTaskbar"] = ("Panneaux — barre des tâches", "Panneaux — taskbar", null),
        ["MyApplicationsAndRecents"] = ("Mes applications et mes récents", "My applications and recents", null),
        ["Network"] = ("Réseau", "Network", null),
        ["TheCaptureCouldNotStart"] = ("La capture n’a pas pu démarrer.\n", "The capture could not start.\n", null),
        ["FrameTheTextToReadEsc"] = (
            "Encadre le texte à lire · Échap pour annuler",
            "Frame the text to read · Esc to cancel",
            null
        ),
        ["DragToFrameEscToCancel"] = (
            "Glisse pour encadrer · Échap pour annuler",
            "Drag to frame · Esc to cancel",
            null
        ),
        ["PinOnScreen"] = ("Épingler sur l’écran", "Pin on screen", null),
        ["CaptureCopied"] = ("Capture copiée", "Capture copied", null),
        ["TheCaptureCouldNotBeCopied"] = (
            "La capture n’a pas pu être copiée.\n",
            "The capture could not be copied.\n",
            null
        ),
        ["CaptureSaved"] = ("Capture enregistrée", "Capture saved", null),
        ["TheNoteCouldNotBePinned"] = ("Le papier n’a pas pu être épinglé.\n", "The note could not be pinned.\n", null),
        ["DragByTheRibbonToMove"] = ("Glisser par le ruban pour déplacer", "Drag by the ribbon to move", null),
        ["UnpinAndDeleteThisNote"] = ("Détacher et supprimer ce papier", "Unpin and delete this note", null),
        ["ReadingTheTextFailed"] = ("La lecture du texte a échoué.\n", "Reading the text failed.\n", null),
        ["CopiedToTheClipboardEditOr"] = (
            "Copié dans le presse-papiers · modifie ou recopie si besoin",
            "Copied to the clipboard · edit or copy again if needed",
            null
        ),
        ["CopyAgain"] = ("Copier à nouveau", "Copy again", null),
        ["CloseTheClipboard"] = ("Fermer le presse-papiers", "Close the clipboard", null),
        ["ClickAnItemToPutIt"] = (
            "Clique un élément pour le remettre dans le presse-papiers",
            "Click an item to put it back on the clipboard",
            null
        ),
        ["ClearTheClipboardHistoryPinnedItems"] = (
            "Vider l’historique du presse-papiers ?\n\nLes éléments épinglés restent.",
            "Clear the clipboard history?\n\nPinned items stay.",
            null
        ),
        ["WindowsKeepsTheseItemsWhilePanneaux"] = (
            "Windows garde ces éléments tant que Panneaux tourne. Rien n’est envoyé sur Internet.",
            "Windows keeps these items while Panneaux is running. Nothing is sent over the Internet.",
            null
        ),
        ["Item"] = (" élément", " item", null),
        ["NothingYetCopySomeTextOr"] = (
            "Rien encore : copie du texte ou une image, elle apparaîtra ici.",
            "Nothing yet: copy some text or an image and it will appear here.",
            null
        ),
        ["NoItemMatches"] = ("Aucun élément ne correspond.", "No item matches.", null),
        ["Pinned"] = (" · épinglé", " · pinned", null),
        ["Unpin"] = ("Détacher", "Unpin", null),
        ["Pin"] = ("Épingler", "Pin", null),
        ["ItemCopied"] = ("Élément copié", "Item copied", null),
        ["CloseTheEyedropper"] = ("Fermer la pipette", "Close the eyedropper", null),
        ["ClickAFirstPointEscTo"] = (
            "Clique un premier point · Échap pour fermer",
            "Click a first point · Esc to close",
            null
        ),
        ["MeasurementDoneEscToClose"] = (
            "Mesure terminée · Échap pour fermer",
            "Measurement done · Esc to close",
            null
        ),
        ["CloseTheQuickCommand"] = ("Fermer la commande rapide", "Close the quick command", null),
        ["NavigateOpenEscClose"] = (
            "↑ ↓ naviguer     ↵ ouvrir     Échap fermer",
            "↑ ↓ navigate     ↵ open     Esc close",
            null
        ),
        ["EnterCopiesTheResult"] = ("Entrée copie le résultat", "Enter copies the result", null),
        ["Result"] = ("Résultat", "Result", null),
        ["Shortcut"] = ("Accès", "Shortcut", null),
        ["YourQuickTabsAreAboveYour"] = (
            "Tes onglets rapides sont au-dessus, tes derniers accès apparaîtront ici.",
            "Your quick tabs are above, your latest shortcuts will appear here.",
            null
        ),
        ["BackToTodaySSpace"] = ("Retour à l’espace du jour", "Back to today's space", null),
        ["AllEverydayToolsInOnePlace"] = (
            "Tous les outils du quotidien, au même endroit",
            "All everyday tools in one place",
            null
        ),
        ["AnnotatedCapture"] = ("Capture annotée", "Annotated capture", "Captura anotada"),
        ["FrameDrawCopyOrPin"] = ("Encadrer, dessiner, copier ou épingler", "Frame, draw, copy or pin", null),
        ["FrameATextItIsCopied"] = ("Encadrer un texte, il est copié", "Frame a text, it is copied", null),
        ["StickyNote"] = ("Papier pense-bête", "Sticky note", "Nota adhesiva"),
        ["ASheetToStickOnThe"] = (
            "Une feuille à coller sur l’écran, gardée au redémarrage",
            "A sheet to stick on the screen, kept across restarts",
            null
        ),
        ["HexadecimalCodeUnderTheCursor"] = (
            "Code hexadécimal sous le curseur",
            "Hexadecimal code under the cursor",
            null
        ),
        ["PixelRuler"] = ("Règle pixel", "Pixel ruler", "Regla de píxeles"),
        ["MeasureADistanceOnScreen"] = (
            "Mesurer une distance à l’écran",
            "Measure a distance on screen",
            "Medir una distancia en pantalla"
        ),
        ["EverythingYouCopiedReadyToCome"] = (
            "Tout ce qui a été copié, prêt à revenir",
            "Everything you copied, ready to come back",
            null
        ),
        ["PinToTheForeground"] = ("Épingler au premier plan", "Pin to the foreground", null),
        ["TheActiveWindowStaysInFront"] = (
            "La fenêtre active reste devant · ",
            "The active window stays in front · ",
            null
        ),
        ["RestoreNotifications"] = ("Rétablir les notifications", "Restore notifications", null),
        ["RestartExplorer"] = ("Redémarrer l’Explorateur", "Restart Explorer", null),
        ["WhenTheTaskbarFreezes"] = ("Quand la barre des tâches se bloque", "When the taskbar freezes", null),
        ["SetAFocusDuration"] = ("Régler une durée de focus", "Set a focus duration", null),
        ["ThemesGlassShortcutsEveryday"] = (
            "Thèmes, verre, raccourcis, quotidien",
            "Themes, glass, shortcuts, everyday",
            null
        ),
        ["ConnectionsSoundBrightness"] = ("Connexions, son, luminosité", "Connections, sound, brightness", null),
        ["PCStatus"] = ("État du PC", "PC status", null),
        ["ProcessorMemoryDisks"] = ("Processeur, mémoire, disques", "Processor, memory, disks", null),
        ["OpenTheWindowsCalculator"] = ("Ouvrir la calculatrice de Windows", "Open the Windows calculator", null),
        ["TaskManager"] = ("Gestionnaire des tâches", "Task Manager", "Administrador de tareas"),
        ["ProcessesAndStartup"] = ("Processus et démarrage", "Processes and startup", "Procesos e inicio"),
        ["ChimeAtTheEnd"] = ("Sonnerie à la fin", "Chime at the end", null),
        ["ListenToTheChime"] = ("Écouter la sonnerie", "Listen to the chime", null),
        ["ListenToTheEndChime"] = ("Écouter la sonnerie de fin", "Listen to the end chime", null),
        ["Stop"] = ("Arrêter", "Stop", null),
        ["FocusFinished"] = ("Focus terminé · ", "Focus finished · ", null),
        ["FocusFinished2"] = ("Focus terminé", "Focus finished", null),
        ["HaveElapsed"] = (" sont écoulées.", " have elapsed.", null),
        ["ChooseCtrlAltOrWindowsAnd"] = (
            "Choisis Ctrl, Alt ou Windows et une touche. Ctrl + Alt + Retour arrière est réservé au secours.",
            "Choose Ctrl, Alt or Windows and a key. Ctrl + Alt + Backspace is reserved for the rescue tool.",
            null
        ),
        ["ThisCombinationIsAlreadyUsedBy"] = (
            "Cette combinaison est déjà utilisée par Panneaux.",
            "This combination is already used by Panneaux.",
            null
        ),
        ["ThisCombinationIsAlreadyUsedBy2"] = (
            "Cette combinaison est déjà utilisée par une autre application.",
            "This combination is already used by another application.",
            null
        ),
        ["ThisCombinationAlreadyStartsTheCapture"] = (
            "Cette combinaison lance déjà la capture.",
            "This combination already starts the capture.",
            null
        ),
        ["ChooseCtrlOrAltAndA"] = (
            "Choisis Ctrl ou Alt et une touche. Ctrl + Alt + Retour arrière est réservé au secours.",
            "Choose Ctrl or Alt and a key. Ctrl + Alt + Backspace is reserved for the rescue tool.",
            null
        ),
        ["ThisShortcutAlreadyOpensTheQuick"] = (
            "Ce raccourci ouvre déjà la commande rapide. Choisis une autre combinaison.",
            "This shortcut already opens the quick command. Choose another combination.",
            null
        ),
        ["ThisShortcutIsAlreadyInUse"] = (
            "Ce raccourci est déjà utilisé. Choisis une autre combinaison ; le précédent reste actif.",
            "This shortcut is already in use. Choose another combination; the previous one stays active.",
            null
        ),
        ["ThePreviousOneStaysActive"] = ("). Le précédent reste actif.", "). The previous one stays active.", null),
        ["ThisCombinationAlreadyOpensYourSpace"] = (
            "Cette combinaison ouvre déjà ton espace. Choisis une autre touche.",
            "This combination already opens your space. Choose another key.",
            null
        ),
        ["ANameIsRequired"] = ("Un nom est nécessaire.", "A name is required.", null),
        ["Open"] = ("Ouvrir", "Open", "Abrir"),
        ["ApplicationsAreNotClosed"] = (
            " » ?\n\nLes applications ne sont pas fermées.",
            " » ?\n\nApplications are not closed.",
            null
        ),
        ["ASpaceAlreadyHasThisName"] = ("Un espace porte déjà ce nom.", "A space already has this name.", null),
        ["NoVisibleApplicationToSave"] = (
            "Aucune application visible à enregistrer.",
            "No visible application to save.",
            null
        ),
        ["ApplicationsSaved"] = (" applications enregistrées", " applications saved", null),
        ["AddAShortcutTo"] = ("Ajouter un accès à ", "Add a shortcut to ", null),
        ["DragTheTitleStretchTheEdges"] = (
            "Glisser le titre · Étirer les bords",
            "Drag the title · Stretch the edges",
            null
        ),
        ["PreviousPage"] = ("Page précédente", "Previous page", null),
        ["ProgramsYourToolsInOnePlace"] = (
            " logiciels · tes outils au même endroit",
            " programs · your tools in one place",
            null
        ),
        ["ShortcutsFindYourNextGame"] = (" accès · trouve ton prochain jeu", " shortcuts · find your next game", null),
        ["SpacesPickUpWhereYouLeft"] = (
            " espaces · reprends là où tu en étais",
            " spaces · pick up where you left off",
            null
        ),
        ["FoldersEverydayLifeWithinReach"] = (
            " dossiers · le quotidien à portée de main",
            " folders · everyday life within reach",
            null
        ),
        ["Categories"] = ("Catégories · ", "Categories · ", null),
        ["NoShortcutInThisView"] = ("Aucun accès dans cette vue", "No shortcut in this view", null),
        ["CustomizeThisShortcut"] = ("Personnaliser cet accès…", "Customize this shortcut…", null),
        ["HideThisShortcut"] = ("Masquer cet accès", "Hide this shortcut", null),
        ["OpenTheQuickCommand2"] = ("Ouvrir la commande rapide · ", "Open the quick command · ", null),
        ["SetATimer"] = ("Régler un minuteur", "Set a timer", null),
        ["NiceAfternoonDj"] = ("Bel après-midi, dj", "Nice afternoon, dj", null),
        ["SystemVolume"] = ("Volume système", "System volume", null),
        ["SoundOn"] = ("Son activé", "Sound on", null),
        ["PanneauxGlassControlCenter"] = (
            "Panneaux Glass — centre de contrôle",
            "Panneaux Glass — control center",
            null
        ),
        ["ControlCenter"] = ("Centre de contrôle", "Control center", null),
        ["YourWay"] = ("À ta façon", "Your way", "A tu manera"),
        ["PreviousTrack"] = ("Morceau précédent", "Previous track", null),
        ["VolumeAndBrightness"] = ("Volume et luminosité", "Volume and brightness", null),
        ["Settings"] = ("Réglages", "Settings", null),
        ["ScreenArea"] = ("Zone d’écran", "Screen area", null),
        ["ShutDownSleep"] = ("Éteindre · veille", "Shut down · sleep", null),
        ["Brightness"] = ("Luminosité", "Brightness", "Brillo"),
        ["Brightness2"] = ("Luminosité  ›", "Brightness  ›", null),
        ["DisplaySettingsOpensTheWindowsOptions"] = (
            "Réglages écran · ouvre les options Windows",
            "Display settings · opens the Windows options",
            null
        ),
        ["OpenTheWindowsFocusSettings"] = (
            "Ouvrir les paramètres de concentration Windows",
            "Open the Windows focus settings",
            null
        ),
        ["OpenTheWindowsAudioControls"] = (
            "Ouvrir les commandes audio Windows",
            "Open the Windows audio controls",
            null
        ),
        ["SystemVolumeDragOrUseThe"] = (
            "Volume système · glisser ou utiliser la molette",
            "System volume · drag or use the wheel",
            null
        ),
        ["SoundMuted"] = ("Son coupé", "Sound muted", null),
        ["AutomaticSpotifyFirst"] = ("Automatique · priorité à Spotify", "Automatic · Spotify first", null),
        ["ConnectedToTheInternet"] = (" · Connecté à Internet", " · Connected to the Internet", null),
        ["Connected"] = ("Connecté", "Connected", "Conectado"),
        ["On"] = ("Activé", "On", null),
        ["Off"] = ("Désactivé", "Off", null),
        ["ToggleWiFiRightClickFor"] = (
            "Activer/désactiver le Wi-Fi · clic droit pour les réseaux",
            "Toggle Wi-Fi · right-click for networks",
            null
        ),
        ["WiFiMissingOpenSettings"] = ("Wi-Fi absent · ouvrir les réglages", "Wi-Fi missing · open settings", null),
        ["ToggleBluetoothRightClickForDevices"] = (
            "Activer/désactiver Bluetooth · clic droit pour les appareils",
            "Toggle Bluetooth · right-click for devices",
            null
        ),
        ["BluetoothMissingOpenSettings"] = (
            "Bluetooth absent · ouvrir les réglages",
            "Bluetooth missing · open settings",
            null
        ),
        ["OpenTheWindowsConnections"] = ("Ouvrir les connexions Windows", "Open the Windows connections", null),
        ["ReadingTheDisplaySetting"] = ("Lecture du réglage de l’écran…", "Reading the display setting…", null),
        ["DragToAdjust"] = (" · glisser pour régler", " · drag to adjust", null),
        ["ClickBrightnessToOpenTheDisplay"] = (
            " Clique sur Luminosité pour ouvrir les réglages écran.",
            " Click Brightness to open the display settings.",
            null
        ),
        ["SettingUnavailableClickBrightnessForThe"] = (
            "Réglage non disponible · clique sur Luminosité pour les réglages écran",
            "Setting unavailable · click Brightness for the display settings",
            null
        ),
        ["WindowsSettings"] = ("Réglages Windows", "Windows settings", null),
        ["CloseCustomization"] = ("Fermer la personnalisation", "Close customization", null),
        ["TheControlsYourWay"] = ("Les commandes, à ta façon", "The controls, your way", null),
        ["BrightnessSlider"] = ("Curseur luminosité", "Brightness slider", null),
        ["ChooseTheIcon"] = ("Choisir l’icône", "Choose the icon", null),
        ["Screen"] = ("Écran", "Screen", "Pantalla"),
        ["ShortcutIcon"] = ("Icône du raccourci", "Shortcut icon", null),
        ["ChooseAnApplicationOrAValid"] = (
            "Choisis une application ou une adresse de paramètre Windows valide.",
            "Choose an application or a valid Windows settings address.",
            null
        ),
        ["CloseTheTimer"] = ("Fermer le minuteur", "Close the timer", null),
        ["AFocusYourWay"] = ("Un focus, à ta façon.", "A focus, your way.", null),
        ["ChooseADurationInMinutesHours"] = (
            "Choisis une durée en minutes, heures ou secondes. Le compte à rebours reste au-dessus de tes fenêtres.",
            "Choose a duration in minutes, hours or seconds. The countdown stays above your windows.",
            null
        ),
        ["FocusDuration"] = ("Durée du focus", "Focus duration", null),
        ["CustomDuration"] = ("Durée libre", "Custom duration", null),
        ["FocusDurationUnit"] = ("Unité de la durée du focus", "Focus duration unit", null),
        ["StartTheFocus"] = ("Démarrer le focus", "Start the focus", null),
        ["EndsAt"] = ("Se termine à ", "Ends at ", null),
        ["OpenThePage"] = ("Ouvrir la page", "Open the page", "Abrir la página"),
        ["PreviousTitle"] = ("Titre précédent", "Previous title", null),
        ["YourApplicationProfilesAndTheFour"] = (
            "Tes profils d’applications et les quatre panneaux, retrouvés d’un geste.",
            "Your application profiles and the four panels, back in one gesture.",
            null
        ),
        ["CreateOrEditMySpaces"] = ("Créer ou modifier mes espaces…", "Create or edit my spaces…", null),
        ["OpenTheWorkshop"] = ("Ouvrir l’atelier", "Open the workshop", null),
        ["OpenTheFourPanels"] = ("Ouvrir les quatre panneaux", "Open the four panels", null),
        ["Shortcuts"] = (" accès · ", " shortcuts · ", null),
        ["InThePanelsRightClickA"] = (
            "Dans les panneaux, clic droit sur un accès → Ajouter aux favoris.",
            "In the panels, right-click a shortcut → Add to favorites.",
            null
        ),
        ["ExploreMyShortcuts"] = ("Explorer mes accès   ›", "Explore my shortcuts   ›", null),
        ["PreviousFavorites"] = ("Favoris précédents", "Previous favorites", null),
        ["YourLatestOpeningsFromPanneauxMost"] = (
            "Tes dernières ouvertures depuis Panneaux, la plus récente en premier",
            "Your latest openings from Panneaux, most recent first",
            null
        ),
        ["Recent"] = (" récent", " recent", null),
        ["YourNextOpenedShortcutsWillAppear"] = (
            "Tes prochains accès ouverts apparaîtront ici.",
            "Your next opened shortcuts will appear here.",
            null
        ),
        ["DragTheImageToMoveIt"] = (
            "Glisse l’image pour la déplacer. Molette ou curseur pour zoomer.",
            "Drag the image to move it. Wheel or slider to zoom.",
            null
        ),
        ["WholeImage"] = ("Image entière", "Whole image", null),
        ["N256256PNGTheCheckerboardStays"] = (
            "PNG 256 × 256 · le damier reste transparent dans l’image enregistrée.",
            "256 × 256 PNG · the checkerboard stays transparent in the saved image.",
            null
        ),
        ["CloseTheNote"] = ("Fermer la note", "Close the note", null),
        ["EverythingThatDoesNotDeserveA"] = (
            "Tout ce qui ne mérite pas un fichier · enregistré automatiquement",
            "Everything that does not deserve a file · saved automatically",
            null
        ),
        ["Modified"] = ("Modifié …", "Modified …", null),
        ["AnIdeaACommandALink"] = (
            "Une idée, une commande, un lien : tout reste ici.",
            "An idea, a command, a link: it all stays here.",
            null
        ),
        ["Saved"] = ("Enregistré", "Saved", null),
        ["SavedAt"] = ("Enregistré à ", "Saved at ", null),
        ["NoteCleared"] = ("Note effacée", "Note cleared", null),
        ["AvailableMemory"] = ("Mémoire disponible", "Available memory", null),
        ["ReadingAndWriting"] = ("Lecture et écriture", "Reading and writing", null),
        ["PanneauxGlassPCStatus"] = ("Panneaux Glass — état du PC", "Panneaux Glass — PC status", null),
        ["LiveMemory"] = ("Mémoire vive", "Live memory", null),
        ["ReceiveSendRealThroughputNotAn"] = (
            "↓ Réception     ↑ Envoi · débit réel, pas un test Internet",
            "↓ Receive     ↑ Send · real throughput, not an Internet test",
            null
        ),
        ["CollectionUnavailableReopenThePanel"] = (
            "Collecte indisponible · réouvre le panneau",
            "Collection unavailable · reopen the panel",
            null
        ),
        ["NoCardDetected"] = ("Aucune carte détectée", "No card detected", null),
        ["Video"] = ("Vidéo : ", "Video: ", null),
        ["Used"] = (" utilisés", " used", null),
        ["TemperatureNoMemoryModuleSensorAccessible"] = (
            "Température : aucune sonde de barrette accessible",
            "Temperature: no memory module sensor accessible",
            null
        ),
        ["Write"] = ("     Écriture ", "     Write ", null),
        ["CyanReceivePurpleSend"] = ("Cyan : réception · violet : envoi", "Cyan: receive · purple: send", null),
        ["N1ReadingSSensors3S"] = (
            "1 relevé/s · sondes 3 s · disques 30 s",
            "1 reading/s · sensors 3 s · disks 30 s",
            null
        ),
        ["RealProcessorTemperatureReadEvery3"] = (
            "Température réelle du processeur · relevée toutes les 3 secondes",
            "Real processor temperature · read every 3 seconds",
            null
        ),
        ["UsedOf"] = (" utilisés sur ", " used of ", null),
        ["LoadShareOfHardwareInUse"] = ("Charge : part du matériel occupée", "Load: share of hardware in use", null),
        ["TemperaturesPhysicalSensors"] = ("Températures : capteurs physiques", "Temperatures: physical sensors", null),
        ["ChartsLast60Readings"] = ("Graphiques : 60 derniers relevés", "Charts: last 60 readings", null),
        ["StorageAverageDiskActivity"] = (
            "Stockage : activité moyenne des disques",
            "Storage: average disk activity",
            null
        ),
        ["OpenTaskManager"] = ("Ouvrir le Gestionnaire des tâches", "Open Task Manager", null),
        ["LightlyLoaded"] = ("Peu sollicité", "Lightly loaded", null),
        ["Active"] = ("En activité", "Active", null),
        ["HeavilyLoaded"] = ("Très sollicité", "Heavily loaded", null),
        ["DetectingHardware"] = ("Détection du matériel…", "Detecting hardware…", null),
        ["YourWay2"] = ("À ta façon.", "Your way.", null),
        ["TheMaterialReflectionsAndDensityOf"] = (
            "La matière, les reflets et la densité de ton espace.",
            "The material, reflections and density of your space.",
            null
        ),
        ["ReflectionIntensity"] = ("Intensité des reflets", "Reflection intensity", null),
        ["IconSize"] = ("Taille des icônes", "Icon size", "Tamaño de los iconos"),
        ["RenameColorAndReorderTheSurfaces"] = (
            "Renomme, colore et réordonne les surfaces. Glisse les accès d’un panneau à l’autre pour les ranger.",
            "Rename, color and reorder the surfaces. Drag shortcuts from one panel to another to arrange them.",
            null
        ),
        ["FirstRowHeight"] = ("Hauteur de la première rangée", "First row height", null),
        ["UseThePanneauxBarInsteadOf"] = (
            "Utiliser la barre Panneaux à la place de la barre Windows",
            "Use the Panneaux bar instead of the Windows taskbar",
            null
        ),
        ["ShowTheHandleOnTheLeft"] = (
            "Afficher la poignée sur le bord gauche",
            "Show the handle on the left edge",
            null
        ),
        ["HandleWidth"] = ("Largeur de la poignée", "Handle width", null),
        ["HandleHeight"] = ("Hauteur de la poignée", "Handle height", null),
        ["OfTheScreen"] = (" % de l’écran", " % of the screen", null),
        ["AnimationDuration"] = ("Durée des animations", "Animation duration", null),
        ["CollapseAfter18SecondsOutside"] = (
            "Replier après 1,8 seconde hors des panneaux",
            "Collapse after 1.8 seconds outside the panels",
            null
        ),
        ["PauseTheEdgeDuringFullScreen"] = (
            "Mettre le bord en pause pendant le plein écran",
            "Pause the edge during full screen",
            null
        ),
        ["ShowThe"] = ("Réafficher les ", "Show the ", null),
        ["HiddenShortcutsAgain"] = (" accès masqués", " hidden shortcuts again", null),
        ["TheNativeRescueToolShowsAnd"] = (
            "Le secours natif réaffiche et organise tes icônes, arrête Panneaux et désactive son démarrage automatique. Il fonctionne même lorsque l’interface ne répond plus.",
            "The native rescue tool shows and organizes your icons again, stops Panneaux and disables its automatic startup. It works even when the interface stops responding.",
            null
        ),
        ["ShowOnlyTheDesktopIcons"] = ("Afficher seulement les icônes du Bureau", "Show only the Desktop icons", null),
        ["HideTheDesktopIcons"] = ("Masquer les icônes du Bureau", "Hide the Desktop icons", null),
        ["Done"] = ("Terminé", "Done", null),
        ["SavedAutomaticallyFilesStayWhereThey"] = (
            "Enregistré automatiquement · Les fichiers restent à leur place.",
            "Saved automatically · Files stay where they are.",
            null
        ),
        ["EachThemeChangesTheMaterialThe"] = (
            "Chaque thème change la matière, la typographie, la forme des cartes et la disposition de l’accueil. Toutes les fonctions restent identiques.",
            "Each theme changes the material, the typography, the card shape and the home layout. All features stay the same.",
            null
        ),
        ["Theme"] = ("Thème ", "Theme ", null),
        ["WhatYouUseEveryDayA"] = (
            "Ce qui sert tous les jours : un presse-papiers qui se souvient, une capture qui reste à l’écran, une fenêtre épinglée au premier plan. Chaque geste se coupe ici s’il ne te sert pas.",
            "What you use every day: a clipboard that remembers, a capture that stays on screen, a window pinned to the foreground. Each gesture can be turned off here if it is of no use to you.",
            null
        ),
        ["MotionIntensity"] = ("Intensité du mouvement : ", "Motion intensity: ", null),
        ["AlsoRememberCopiedImages"] = ("Mémoriser aussi les images copiées", "Also remember copied images", null),
        ["ItemsKept"] = ("Éléments conservés", "Items kept", null),
        ["Items"] = (" éléments", " items", null),
        ["PINTOTHEFOREGROUND"] = ("ÉPINGLER AU PREMIER PLAN", "PIN TO THE FOREGROUND", null),
        ["CAPTURETHESCREEN"] = ("CAPTURER L’ÉCRAN", "CAPTURE THE SCREEN", null),
        ["OnePressOpensYourSpaceIn"] = (
            "Un appui ouvre ton espace au premier plan. Un second le replie. Choisis aussi la vue à retrouver pour accéder au son et à ta musique immédiatement.",
            "One press opens your space in the foreground. A second one collapses it. Also choose the view to return to for immediate access to sound and your music.",
            null
        ),
        ["ToApply"] = ("À appliquer : ", "To apply: ", null),
        ["HomePCStatus"] = ("Accueil + état du PC", "Home + PC status", null),
        ["LastUsedView"] = ("Dernière vue utilisée", "Last used view", null),
        ["ViewToOpen"] = ("Vue à ouvrir", "View to open", null),
        ["CtrlAltSpaceRemainsAvailableThe"] = (
            "Ctrl + Alt + Espace reste disponible. Le raccourci fonctionne même si la poignée est masquée en plein écran.",
            "Ctrl + Alt + Space remains available. The shortcut works even if the handle is hidden in full screen.",
            null
        ),
        ["MoveThisPanelBeforeThePrevious"] = (
            "Déplacer ce panneau avant le précédent",
            "Move this panel before the previous one",
            null
        ),
        ["PanneauxGlassEditAShortcut"] = (
            "Panneaux Glass — modifier un accès",
            "Panneaux Glass — edit a shortcut",
            null
        ),
        ["ThisShortcutYourWay"] = ("Cet accès, à ta façon.", "This shortcut, your way.", null),
        ["DISPLAYEDNAME"] = ("NOM AFFICHÉ", "DISPLAYED NAME", null),
        ["CATEGORY"] = ("CATÉGORIE", "CATEGORY", null),
        ["ChooseAnIcon"] = ("Choisir une icône…", "Choose an icon…", null),
        ["ChooseAnIcon2"] = ("Choisir une icône", "Choose an icon", "Elegir un icono"),
        ["OriginalIcon"] = ("Icône d’origine", "Original icon", null),
        ["Controls"] = ("Contrôles", "Controls", null),
        ["CloseTheQuickTabs"] = ("Fermer les onglets rapides", "Close the quick tabs", null),
        ["WhatYouSeeAtTheTop"] = (
            "Ce que tu vois en haut de la commande rapide · renomme, change l’icône, réordonne",
            "What you see at the top of the quick command · rename, change the icon, reorder",
            null
        ),
        ["ChangeTheIcon"] = ("Changer l’icône", "Change the icon", null),
        ["IconOf"] = ("Icône de ", "Icon of ", null),
        ["GlobalShortcutActiveEvenWithLauncherClosed"] = (
            "Un raccourci global, actif même launcher fermé, depuis n’importe quelle application. Ctrl F2 par défaut.",
            "A global shortcut, active even with the launcher closed, from any application. Ctrl F2 by default.",
            null
        ),
        ["Screenshot"] = ("Capture d’écran", "Screenshot", "Captura de pantalla"),
        ["TwelveTabsAtMostBeyondThat"] = (
            "Douze onglets au maximum : au-delà, la palette devient un menu.",
            "Twelve tabs at most: beyond that, the palette becomes a menu.",
            null
        ),
        ["OpensTheQuickCommandEvenWith"] = (
            " ouvre la commande rapide, même launcher fermé.",
            " opens the quick command, even with the launcher closed.",
            null
        ),
        ["CreamClayAndSoftCardsThe"] = (
            "Crème, argile et cartes douces. Le nouveau bureau Panneaux.",
            "Cream, clay and soft cards. The new Panneaux desktop.",
            null
        ),
        ["PlumAndVioletABandOf"] = (
            "Prune et violet, une bande de lumière en travers du verre.",
            "Plum and violet, a band of light across the glass.",
            null
        ),
        ["DeepGreenTurquoiseMistAndGlass"] = (
            "Vert profond, brume turquoise et pétales de verre.",
            "Deep green, turquoise mist and glass petals.",
            null
        ),
        ["WarmAmberEndOfDayReflections"] = (
            "Ambre chaud, reflets de fin de journée.",
            "Warm amber, end-of-day reflections.",
            null
        ),
        ["RosyPlumCherryBlossomHalos"] = (
            "Prune rosé, halos fleur de cerisier.",
            "Rosy plum, cherry blossom halos.",
            null
        ),
        ["CreamPaperBrownInkAndSerif"] = (
            "Papier crème, encre brune et titres en serif. Les accès deviennent des fiches.",
            "Cream paper, brown ink and serif titles. Shortcuts become index cards.",
            null
        ),
        ["TerminalBlackPhosphorGreenMonospacedTypography"] = (
            "Noir de terminal, phosphore vert, typographie à chasse fixe. Tout est en listes.",
            "Terminal black, phosphor green, monospaced typography. Everything is lists.",
            null
        ),
        ["NavyCoralAndMustardPrintedBlocks"] = (
            "Bleu nuit, corail et moutarde. Blocs imprimés, bords francs, grandes fiches.",
            "Navy, coral and mustard. Printed blocks, bold edges, large cards.",
            null
        ),
        ["LightStoneSoftShadowsGenerousCurves"] = (
            "Pierre claire, ombres douces, courbes généreuses. Une matière mate, lumineuse.",
            "Light stone, soft shadows, generous curves. A matte, luminous material.",
            null
        ),
        ["CreamTrayAndSolidCellsBold"] = (
            "Plateau crème et cases pleines, couleurs franches. Chaque accès est une petite boîte.",
            "Cream tray and solid cells, bold colors. Each shortcut is a small box.",
            null
        ),
        ["NightBlackGlowingMagentaAndCyan"] = (
            "Noir de nuit, traits lumineux magenta et cyan. Les cadres brillent au lieu de refléter.",
            "Night black, glowing magenta and cyan strokes. Frames shine instead of reflecting.",
            null
        ),
        ["DesktopGrayBeveledEdgesSquareCorners"] = (
            "Gris de bureau, bords biseautés, coins carrés et listes à l’ancienne. Un hommage assumé.",
            "Desktop gray, beveled edges, square corners and old-style lists. A deliberate tribute.",
            null
        ),
        ["WhiteSheetThinRulesSignalRed"] = (
            "Feuille blanche, filets fins, rouge signal et lignes numérotées. Rien de décoratif.",
            "White sheet, thin rules, signal red and numbered lines. Nothing decorative.",
            null
        ),
        ["LightSandGreenInkAndWide"] = (
            "Sable clair, encre verte et courbes amples. Des rangées larges, beaucoup d’air.",
            "Light sand, green ink and wide curves. Wide rows, lots of air.",
            null
        ),
        ["CloseTheWorkshop"] = ("Fermer l’atelier", "Close the workshop", null),
        ["TheUsefulEverydayGesturesGatheredIn"] = (
            "Les gestes utiles du quotidien, réunis au même endroit",
            "The useful everyday gestures, gathered in one place",
            null
        ),
        ["FrameAnAreaDrawOnIt"] = (
            "Encadre une zone, dessine dessus, copie ou épingle",
            "Frame an area, draw on it, copy or pin",
            null
        ),
        ["FrameATextItIsCopied2"] = (
            "Encadre un texte, il est copié automatiquement",
            "Frame a text, it is copied automatically",
            null
        ),
        ["MagnifiesUnderTheCursorAndCopies"] = (
            "Agrandit sous le curseur et copie le code hexadécimal",
            "Magnifies under the cursor and copies the hexadecimal code",
            null
        ),
        ["MeasuresADistanceOnScreenIn"] = (
            "Mesure une distance à l’écran, en pixels et en centimètres",
            "Measures a distance on screen, in pixels and centimeters",
            null
        ),
        ["AWindowStaysInFrontOf"] = (
            "Une fenêtre reste devant toutes les autres",
            "A window stays in front of all the others",
            null
        ),
        ["MutesOrRestoresWindowsNotifications"] = (
            "Coupe ou rétablit les notifications Windows",
            "Mutes or restores Windows notifications",
            null
        ),
        ["On2"] = ("Activées", "On", null),
        ["Muted"] = ("Coupées", "Muted", null),
        ["WhenTheTaskbarOrTheDesktop"] = (
            "Quand la barre des tâches ou le Bureau se bloquent",
            "When the taskbar or the Desktop freezes",
            null
        ),
        ["ASheetStuckToTheScreen"] = (
            "Une feuille collée à l’écran : écris, place, garde",
            "A sheet stuck to the screen: write, place, keep",
            null
        ),
        ["EverythingYouCopiedReadyToCome2"] = (
            "Tout ce que tu as copié, prêt à revenir",
            "Everything you copied, ready to come back",
            null
        ),
        ["NothingInThisCategory"] = ("Rien dans cette catégorie.", "Nothing in this category.", null),
        ["CloseTheHelp"] = ("Fermer l’aide", "Close the help", null),
        ["ThreeGesturesAndEverythingIsThere"] = (
            "Trois gestes, et tout est là.",
            "Three gestures, and everything is there.",
            null
        ),
        ["PanneauxStaysDiscreetAHandleOn"] = (
            "Panneaux reste discret : une poignée à droite, deux raccourcis, un atelier",
            "Panneaux stays discreet: a handle on the right, two shortcuts, a workshop",
            null
        ),
        ["MoveThePointerToTheRight"] = (
            "Approche le pointeur du bord droit de l’écran, ou utilise le raccourci global. Un deuxième appui replie tout, avec les fenêtres ouvertes depuis l’espace.",
            "Move the pointer to the right edge of the screen, or use the global shortcut. A second press collapses everything, with the windows opened from the space.",
            null
        ),
        ["AlwaysOnTopPinTheActive"] = (
            "Toujours au premier plan : épingle la fenêtre active",
            "Always on top: pin the active window",
            null
        ),
        ["ReturnsToHome"] = ("Revient à l’accueil", "Returns to home", null),
        ["Esc"] = ("Échap", "Esc", null),
        ["CtrlAltBackspace"] = ("Ctrl Alt Retour arrière", "Ctrl Alt Backspace", null),
        ["RestoresTheOrganizedDesktopEvenIf"] = (
            "Retrouve le Bureau organisé, même si l’interface ne répond plus",
            "Restores the organized Desktop, even if the interface stops responding",
            null
        ),
        ["YOURSETTINGS"] = ("TES RÉGLAGES", "YOUR SETTINGS", null),
        ["InPersonalizeThemeCalmStandardExpressive"] = (
            "Dans Personnaliser : thème, mouvement Calme/Standard/Expressif, raccourcis, préservation du presse-papiers. Rien n’est imposé, tout se règle.",
            "In Personalize: theme, Calm/Standard/Expressive motion, shortcuts, clipboard retention. Nothing is imposed, everything is adjustable.",
            null
        ),
        ["ShowAGlassSquareBehindThe"] = (
            "Afficher un carré de verre derrière le logo",
            "Show a glass square behind the logo",
            null
        ),
        ["ASiteYourIcon"] = ("Un site. Ton icône.", "A site. Your icon.", null),
        ["YourShortcutYourWay"] = ("Ton raccourci, à ta façon.", "Your shortcut, your way.", null),
        ["TheLogoStaysAloneOnScreen"] = (
            "Le logo reste seul à l’écran. Son nom apparaît au survol.",
            "The logo stays alone on screen. Its name appears on hover.",
            null
        ),
        ["ImageReadyCropSavedWhenThe"] = (
            "Image prête · recadrage enregistré lors de la validation du site.",
            "Image ready · crop saved when the site is confirmed.",
            null
        ),
        ["ThisImageCannotBeOpened"] = (
            "Cette image ne peut pas être ouverte : ",
            "This image cannot be opened: ",
            null
        ),
        ["OpenInBrowser"] = ("Ouvrir dans le navigateur", "Open in browser", "Abrir en el navegador"),
        ["MoveLeft"] = ("Déplacer vers la gauche", "Move left", null),
        ["MoveRight"] = ("Déplacer vers la droite", "Move right", null),
        ["FolderDeletedOrRenamedBackTo"] = (
            "Dossier supprimé ou renommé : retour au dossier parent",
            "Folder deleted or renamed: back to the parent folder",
            null
        ),
        ["LargeIconsCtrlShift2"] = ("Grandes icônes (Ctrl+Maj+2)", "Large icons (Ctrl+Shift+2)", null),
        ["BackAltLeft"] = ("Précédent (Alt+Gauche)", "Back (Alt+Left)", null),
        ["Modified2"] = ("Modifié", "Modified", null),
        ["DetailsCtrlShift1"] = ("Détails (Ctrl+Maj+1)", "Details (Ctrl+Shift+1)", null),
        ["CloseCtrlW"] = ("Fermer (Ctrl+W)", "Close (Ctrl+W)", null),
        ["AccessDenied"] = ("Accès refusé", "Access denied", null),
        ["OpenInExplorer"] = ("Ouvrir dans Explorer", "Open in Explorer", null),
        ["UnableToOpenThisFolder"] = ("Impossible d'ouvrir ce dossier", "Unable to open this folder", null),
        ["ThisFolderContainsNoItems"] = (
            "Ce dossier ne contient aucun élément.",
            "This folder contains no items.",
            null
        ),
        ["OpenInANewTab"] = ("Ouvrir dans un nouvel onglet", "Open in a new tab", null),
        ["PermanentlyDelete"] = ("Supprimer définitivement ?", "Permanently delete?", null),
        ["PermanentlyDelete2"] = ("Supprimer définitivement", "Permanently delete", null),
        ["TheNameCannotBeEmpty"] = ("Le nom ne peut pas être vide.", "The name cannot be empty.", null),
        ["ThisNameAlreadyExistsInThis"] = (
            "Ce nom existe déjà dans ce dossier.",
            "This name already exists in this folder.",
            null
        ),
        ["FilterCtrlFEscToClear"] = ("Filtrer (Ctrl+F, Échap pour effacer)", "Filter (Ctrl+F, Esc to clear)", null),
        ["ClearTheFilterEsc"] = ("Effacer le filtre (Échap)", "Clear the filter (Esc)", null),
        ["NoResults"] = ("Aucun résultat", "No results", "Sin resultados"),
        ["NoItemMatches2"] = ("Aucun élément ne correspond à « ", "No item matches « ", null),
        ["Move"] = ("Déplacer ", "Move ", null),
        ["ShortcutSaved"] = ("Raccourci enregistré", "Shortcut saved", "Atajo guardado"),
        ["CopiedImage"] = ("Image copiée", "Copied image", "Imagen copiada"),
        ["Start2"] = ("Démarrer", "Start", null),
        ["IconOrApplicationPngIcoExe"] = (
            "Icône ou application|*.png;*.ico;*.exe;*.lnk|Tous les fichiers|*.*",
            "Icon or application|*.png;*.ico;*.exe;*.lnk|All files|*.*",
            null
        ),
        ["OpenWithWinlestiaFiles"] = ("Ouvrir avec Winlestia Fichiers", "Open with Winlestia Files", null),
        ["StatusCountOne"] = ("{0} élément", "{0} item", null),
        ["StatusCountMany"] = ("{0} éléments", "{0} items", null),
        ["StatusFilteredOne"] = ("{0} sur {1} élément", "{0} of {1} item", null),
        ["StatusFilteredMany"] = ("{0} sur {1} éléments", "{0} of {1} items", null),
        ["StatusSelectedOne"] = ("{0} sélectionné", "{0} selected", null),
        ["StatusSelectedMany"] = ("{0} sélectionnés", "{0} selected", null),
        ["DeletePermanentlyOne"] = (
            "« {0} » sera supprimé définitivement, sans passer par la Corbeille.",
            "“{0}” will be permanently deleted, without going through the Recycle Bin.",
            null
        ),
        ["DeletePermanentlyMany"] = (
            "{0} éléments seront supprimés définitivement, sans passer par la Corbeille.",
            "{0} items will be permanently deleted, without going through the Recycle Bin.",
            null
        ),
        ["DesktopSettingNotApplied"] = (
            "Le Bureau Windows n’a pas appliqué le réglage.",
            "The Windows desktop did not apply the setting.",
            null
        ),
        ["DesktopSettingNotKept"] = (
            "Le Bureau Windows n’a pas retenu le réglage.",
            "The Windows desktop did not keep the setting.",
            null
        ),
        ["DesktopComponentMissing"] = (
            "Le composant natif du Bureau est absent.",
            "The native desktop component is missing.",
            null
        ),
        ["NoAudioSessionForPlayer"] = (
            "Aucune session audio pour ce lecteur.",
            "No audio session for this player.",
            null
        ),
        ["AppNoLongerHasAudioSession"] = (
            "Cette application n’a plus de session audio. Relance sa lecture.",
            "This application no longer has an audio session. Restart its playback.",
            null
        ),
        ["DeviceNoLongerConnected"] = (
            "Ce périphérique n’est plus connecté.",
            "This device is no longer connected.",
            null
        ),
        ["OpenMixerToAdjustApp"] = (
            "Ouvre le mixeur pour régler une application.",
            "Open the mixer to adjust an application.",
            null
        ),
        ["WindowsRefusedOutputChange"] = (
            "Windows a refusé le changement de sortie.",
            "Windows refused the output change.",
            null
        ),
        ["DisplayRefusedBrightness"] = (
            "L’écran a refusé le réglage de luminosité.",
            "The display refused the brightness setting.",
            null
        ),
        ["BrightnessNotAdjustable"] = (
            "Luminosité non réglable sur cet écran.",
            "Brightness cannot be adjusted on this display.",
            null
        ),
        ["ConnectionChangeNotApplied"] = (
            "Windows n’a pas appliqué ce changement de connexion.",
            "Windows did not apply this connection change.",
            null
        ),
        ["ModelDownloadIncomplete"] = (
            "Téléchargement du modèle incomplet ({0} Mo reçus). Réessaie.",
            "Incomplete model download ({0} MB received). Try again.",
            null
        ),
        ["NoRecognitionEngine"] = (
            "Aucun moteur de reconnaissance n’est installé pour cette langue.",
            "No recognition engine is installed for this language.",
            null
        ),
        ["InvalidIcon"] = ("Icône invalide.", "Invalid icon.", null),
        ["LogoFullyTransparent"] = ("Le logo est entièrement transparent.", "The logo is entirely transparent.", null),
        ["ActivationImpossible"] = ("Activation impossible : ", "Activation failed: ", null),
        ["Category.Tout"] = ("Tout", "All", null),
        ["Category.Système"] = ("Système", "System", null),
        ["Category.Fichiers"] = ("Fichiers", "Files", null),
        ["Category.Confort"] = ("Confort", "Comfort", null),
        ["Category.Tous"] = ("Toutes les catégories", "All categories", null),
        ["Category.Sélection"] = ("Sélection", "Selection", null),
        ["Category.Création"] = ("Création", "Creation", null),
        ["Category.Médias"] = ("Médias", "Media", null),
        ["Category.IA"] = ("IA", "AI", null),
        ["EverydaySelection"] = ("Sélection du quotidien", "Everyday selection", null),
        ["FavouritesPrefix"] = ("Favoris · ", "Favourites · ", null),
        ["QuickAccess"] = ("Accès rapide", "Quick access", null),
        ["Drives"] = ("Lecteurs", "Drives", null),
        ["LocalDisk"] = ("Disque local", "Local disk", null),
        ["FreeOf"] = ("{0} libres sur {1}", "{0} free of {1}", null),
        ["Folder.Desktop"] = ("Bureau", "Desktop", null),
        ["Folder.Documents"] = ("Documents", "Documents", null),
        ["Folder.Downloads"] = ("Téléchargements", "Downloads", null),
        ["Folder.Pictures"] = ("Images", "Pictures", null),
        ["Folder.Music"] = ("Musique", "Music", null),
        ["Folder.Videos"] = ("Vidéos", "Videos", null),
    };

    public static string PreparingMeasurements => Get("PreparingMeasurements");
    public static string Memory => Get("Memory");
    public static string NetworkConnection => Get("NetworkConnection");
    public static string LIVEMEMORY => Get("LIVEMEMORY");
    public static string NETWORKLIVETHROUGHPUT => Get("NETWORKLIVETHROUGHPUT");
    public static string MeasurementsStopWhenYouLeaveThis => Get("MeasurementsStopWhenYouLeaveThis");
    public static string PreparingSensors => Get("PreparingSensors");
    public static string Temperature => Get("Temperature");
    public static string GBUsed => Get("GBUsed");
    public static string LaunchPanneauxAtSignIn => Get("LaunchPanneauxAtSignIn");
    public static string GiveThisShortcutAName => Get("GiveThisShortcutAName");
    public static string ThisSiteIsAlreadyPresent => Get("ThisSiteIsAlreadyPresent");
    public static string PanneauxGlassDidNotStartThe => Get("PanneauxGlassDidNotStartThe");
    public static string WindowsDidNotRespond => Get("WindowsDidNotRespond");
    public static string CtrlAltShiftNumpad => Get("CtrlAltShiftNumpad");
    public static string CtrlAltShiftNumpad2 => Get("CtrlAltShiftNumpad2");
    public static string MatchingFolderSIncludingShownFolders => Get("MatchingFolderSIncludingShownFolders");
    public static string Ignored => Get("Ignored");
    public static string NavigationSettingSModifiedElsewhereKept => Get("NavigationSettingSModifiedElsewhereKept");
    public static string OriginalIconsRestored => Get("OriginalIconsRestored");
    public static string ConflictSKept => Get("ConflictSKept");
    public static string FolderSKeptCheckTheLog => Get("FolderSKeptCheckTheLog");
    public static string WinlestiaFilesWasNotFoundOption => Get("WinlestiaFilesWasNotFoundOption");
    public static string RestoreIncompleteTryAgain => Get("RestoreIncompleteTryAgain");
    public static string WinlestiaFilesWasNotFoundExplorer => Get("WinlestiaFilesWasNotFoundExplorer");
    public static string WinlestiaFilesCrashedThreeTimesExplorer => Get("WinlestiaFilesCrashedThreeTimesExplorer");
    public static string FolderRestoreIncomplete => Get("FolderRestoreIncomplete");
    public static string Close => Get("Close");
    public static string FilesOver200MBFromLargest => Get("FilesOver200MBFromLargest");
    public static string ChooseAFolderToSeeIts => Get("ChooseAFolderToSeeIts");
    public static string NothingIsDeletedFromPanneauxEach => Get("NothingIsDeletedFromPanneauxEach");
    public static string ChooseTheFolderToAnalyze => Get("ChooseTheFolderToAnalyze");
    public static string AnalysisCancelled => Get("AnalysisCancelled");
    public static string CreateAZIPArchive => Get("CreateAZIPArchive");
    public static string SystemSounds => Get("SystemSounds");
    public static string SwitchHeadsetMicrophoneOrMuteThe => Get("SwitchHeadsetMicrophoneOrMuteThe");
    public static string NoActiveOutputDetected => Get("NoActiveOutputDetected");
    public static string NoActiveMicrophoneDetected => Get("NoActiveMicrophoneDetected");
    public static string UnmuteMicrophone => Get("UnmuteMicrophone");
    public static string MicrophoneMuted => Get("MicrophoneMuted");
    public static string MicrophoneUnmuted => Get("MicrophoneUnmuted");
    public static string OpenWindowsSettings => Get("OpenWindowsSettings");
    public static string TheChangeAppliesImmediatelyToAll => Get("TheChangeAppliesImmediatelyToAll");
    public static string AudioSettingUnavailable => Get("AudioSettingUnavailable");
    public static string WindowsRefusedAccessOpenWindowsSettings => Get("WindowsRefusedAccessOpenWindowsSettings");
    public static string DisplayBrightness => Get("DisplayBrightness");
    public static string ThisDisplayDoesNotExposeA => Get("ThisDisplayDoesNotExposeA");
    public static string CPUTemperatureSensorDriverRequired => Get("CPUTemperatureSensorDriverRequired");
    public static string SleepCouldNotBeTriggeredOn => Get("SleepCouldNotBeTriggeredOn");
    public static string EmptyTheRecycleBinTheFiles => Get("EmptyTheRecycleBinTheFiles");
    public static string RecycleBinEmptied => Get("RecycleBinEmptied");
    public static string TheRecycleBinIsAlreadyEmpty => Get("TheRecycleBinIsAlreadyEmpty");
    public static string Restart => Get("Restart");
    public static string ShutDown => Get("ShutDown");
    public static string Copied => Get("Copied");
    public static string CouldNotBeLaunched => Get("CouldNotBeLaunched");
    public static string NotConnected => Get("NotConnected");
    public static string WindowsDoesNotAllowThisCommand => Get("WindowsDoesNotAllowThisCommand");
    public static string PanneauxSideHandle => Get("PanneauxSideHandle");
    public static string PanneauxGlassExplorerOnTheLeft => Get("PanneauxGlassExplorerOnTheLeft");
    public static string PanneauxGlassExplorerOnTheRight => Get("PanneauxGlassExplorerOnTheRight");
    public static string ShowPCStatus => Get("ShowPCStatus");
    public static string OpenTheSpace => Get("OpenTheSpace");
    public static string RefreshShortcuts => Get("RefreshShortcuts");
    public static string RestoreTheOrganizedDesktop => Get("RestoreTheOrganizedDesktop");
    public static string OpenYourSpace => Get("OpenYourSpace");
    public static string OpenPanneauxGlass => Get("OpenPanneauxGlass");
    public static string OpenTheQuickCommand => Get("OpenTheQuickCommand");
    public static string BackToHome => Get("BackToHome");
    public static string RestoreThePanelLayout => Get("RestoreThePanelLayout");
    public static string TheIndependentRescueToolIsMissing => Get("TheIndependentRescueToolIsMissing");
    public static string TheShortcut => Get("TheShortcut");
    public static string ThisShortcutUsesAWindowsLaunch => Get("ThisShortcutUsesAWindowsLaunch");
    public static string AddAFolderToYourSpace => Get("AddAFolderToYourSpace");
    public static string MyShortcuts => Get("MyShortcuts");
    public static string TheTaskbarRescueDidNotStart => Get("TheTaskbarRescueDidNotStart");
    public static string YourTaskbar => Get("YourTaskbar");
    public static string RestoreTheWindowsTaskbar => Get("RestoreTheWindowsTaskbar");
    public static string OpenNewWindow => Get("OpenNewWindow");
    public static string UnpinFromTheTaskbar => Get("UnpinFromTheTaskbar");
    public static string PinToTheTaskbar => Get("PinToTheTaskbar");
    public static string NoWindowToPin => Get("NoWindowToPin");
    public static string WindowsRefusedThisChange => Get("WindowsRefusedThisChange");
    public static string TidiedNormally => Get("TidiedNormally");
    public static string NotificationsRestored => Get("NotificationsRestored");
    public static string NotificationsMuted => Get("NotificationsMuted");
    public static string RestartWindowsExplorerTheTaskbarAnd => Get("RestartWindowsExplorerTheTaskbarAnd");
    public static string ExplorerRestarted => Get("ExplorerRestarted");
    public static string CollapsePCStatus => Get("CollapsePCStatus");
    public static string N1OtherWindow => Get("N1OtherWindow");
    public static string OtherWindows => Get("OtherWindows");
    public static string DragToMoveThisPanel => Get("DragToMoveThisPanel");
    public static string ThisWindowHasNoLaunchableProgram => Get("ThisWindowHasNoLaunchableProgram");
    public static string WindowsRefusedToCloseThisWindow => Get("WindowsRefusedToCloseThisWindow");
    public static string SomeWindowsCouldNotBeClosed => Get("SomeWindowsCouldNotBeClosed");
    public static string StopRefusedForThisProcess => Get("StopRefusedForThisProcess");
    public static string FilesCopiedToTheClipboard => Get("FilesCopiedToTheClipboard");
    public static string ZIPArchiveCreated => Get("ZIPArchiveCreated");
    public static string ChooseTheFolderToCustomize => Get("ChooseTheFolderToCustomize");
    public static string CouldNotOpenThisFile => Get("CouldNotOpenThisFile");
    public static string WinlestiaCouldNotStartItsInterface => Get("WinlestiaCouldNotStartItsInterface");
    public static string TheIncidentDetailsAreIn => Get("TheIncidentDetailsAreIn");
    public static string TheWindowsTaskbarHasBeenRestored => Get("TheWindowsTaskbarHasBeenRestored");
    public static string TheQtEngineDidNotStart => Get("TheQtEngineDidNotStart");
    public static string TheQtEngineStoppedTheWindows => Get("TheQtEngineStoppedTheWindows");
    public static string CapturePinned => Get("CapturePinned");
    public static string TextCopiedToTheClipboard => Get("TextCopiedToTheClipboard");
    public static string PanneauxTaskbar => Get("PanneauxTaskbar");
    public static string MyApplicationsAndRecents => Get("MyApplicationsAndRecents");
    public static string Network => Get("Network");
    public static string TheCaptureCouldNotStart => Get("TheCaptureCouldNotStart");
    public static string FrameTheTextToReadEsc => Get("FrameTheTextToReadEsc");
    public static string DragToFrameEscToCancel => Get("DragToFrameEscToCancel");
    public static string PinOnScreen => Get("PinOnScreen");
    public static string CaptureCopied => Get("CaptureCopied");
    public static string TheCaptureCouldNotBeCopied => Get("TheCaptureCouldNotBeCopied");
    public static string CaptureSaved => Get("CaptureSaved");
    public static string TheNoteCouldNotBePinned => Get("TheNoteCouldNotBePinned");
    public static string DragByTheRibbonToMove => Get("DragByTheRibbonToMove");
    public static string UnpinAndDeleteThisNote => Get("UnpinAndDeleteThisNote");
    public static string ReadingTheTextFailed => Get("ReadingTheTextFailed");
    public static string CopiedToTheClipboardEditOr => Get("CopiedToTheClipboardEditOr");
    public static string CopyAgain => Get("CopyAgain");
    public static string CloseTheClipboard => Get("CloseTheClipboard");
    public static string ClickAnItemToPutIt => Get("ClickAnItemToPutIt");
    public static string ClearTheClipboardHistoryPinnedItems => Get("ClearTheClipboardHistoryPinnedItems");
    public static string WindowsKeepsTheseItemsWhilePanneaux => Get("WindowsKeepsTheseItemsWhilePanneaux");
    public static string Item => Get("Item");
    public static string NothingYetCopySomeTextOr => Get("NothingYetCopySomeTextOr");
    public static string NoItemMatches => Get("NoItemMatches");
    public static string Pinned => Get("Pinned");
    public static string Unpin => Get("Unpin");
    public static string Pin => Get("Pin");
    public static string ItemCopied => Get("ItemCopied");
    public static string CloseTheEyedropper => Get("CloseTheEyedropper");
    public static string ClickAFirstPointEscTo => Get("ClickAFirstPointEscTo");
    public static string MeasurementDoneEscToClose => Get("MeasurementDoneEscToClose");
    public static string CloseTheQuickCommand => Get("CloseTheQuickCommand");
    public static string NavigateOpenEscClose => Get("NavigateOpenEscClose");
    public static string EnterCopiesTheResult => Get("EnterCopiesTheResult");
    public static string Result => Get("Result");
    public static string Shortcut => Get("Shortcut");
    public static string YourQuickTabsAreAboveYour => Get("YourQuickTabsAreAboveYour");
    public static string BackToTodaySSpace => Get("BackToTodaySSpace");
    public static string AllEverydayToolsInOnePlace => Get("AllEverydayToolsInOnePlace");
    public static string AnnotatedCapture => Get("AnnotatedCapture");
    public static string FrameDrawCopyOrPin => Get("FrameDrawCopyOrPin");
    public static string FrameATextItIsCopied => Get("FrameATextItIsCopied");
    public static string StickyNote => Get("StickyNote");
    public static string ASheetToStickOnThe => Get("ASheetToStickOnThe");
    public static string HexadecimalCodeUnderTheCursor => Get("HexadecimalCodeUnderTheCursor");
    public static string PixelRuler => Get("PixelRuler");
    public static string MeasureADistanceOnScreen => Get("MeasureADistanceOnScreen");
    public static string EverythingYouCopiedReadyToCome => Get("EverythingYouCopiedReadyToCome");
    public static string PinToTheForeground => Get("PinToTheForeground");
    public static string TheActiveWindowStaysInFront => Get("TheActiveWindowStaysInFront");
    public static string RestoreNotifications => Get("RestoreNotifications");
    public static string RestartExplorer => Get("RestartExplorer");
    public static string WhenTheTaskbarFreezes => Get("WhenTheTaskbarFreezes");
    public static string SetAFocusDuration => Get("SetAFocusDuration");
    public static string ThemesGlassShortcutsEveryday => Get("ThemesGlassShortcutsEveryday");
    public static string ConnectionsSoundBrightness => Get("ConnectionsSoundBrightness");
    public static string PCStatus => Get("PCStatus");
    public static string ProcessorMemoryDisks => Get("ProcessorMemoryDisks");
    public static string OpenTheWindowsCalculator => Get("OpenTheWindowsCalculator");
    public static string TaskManager => Get("TaskManager");
    public static string ProcessesAndStartup => Get("ProcessesAndStartup");
    public static string ChimeAtTheEnd => Get("ChimeAtTheEnd");
    public static string ListenToTheChime => Get("ListenToTheChime");
    public static string ListenToTheEndChime => Get("ListenToTheEndChime");
    public static string Stop => Get("Stop");
    public static string FocusFinished => Get("FocusFinished");
    public static string FocusFinished2 => Get("FocusFinished2");
    public static string HaveElapsed => Get("HaveElapsed");
    public static string ChooseCtrlAltOrWindowsAnd => Get("ChooseCtrlAltOrWindowsAnd");
    public static string ThisCombinationIsAlreadyUsedBy => Get("ThisCombinationIsAlreadyUsedBy");
    public static string ThisCombinationIsAlreadyUsedBy2 => Get("ThisCombinationIsAlreadyUsedBy2");
    public static string ThisCombinationAlreadyStartsTheCapture => Get("ThisCombinationAlreadyStartsTheCapture");
    public static string ChooseCtrlOrAltAndA => Get("ChooseCtrlOrAltAndA");
    public static string ThisShortcutAlreadyOpensTheQuick => Get("ThisShortcutAlreadyOpensTheQuick");
    public static string ThisShortcutIsAlreadyInUse => Get("ThisShortcutIsAlreadyInUse");
    public static string ThePreviousOneStaysActive => Get("ThePreviousOneStaysActive");
    public static string ThisCombinationAlreadyOpensYourSpace => Get("ThisCombinationAlreadyOpensYourSpace");
    public static string ANameIsRequired => Get("ANameIsRequired");
    public static string Open => Get("Open");
    public static string ApplicationsAreNotClosed => Get("ApplicationsAreNotClosed");
    public static string ASpaceAlreadyHasThisName => Get("ASpaceAlreadyHasThisName");
    public static string NoVisibleApplicationToSave => Get("NoVisibleApplicationToSave");
    public static string ApplicationsSaved => Get("ApplicationsSaved");
    public static string AddAShortcutTo => Get("AddAShortcutTo");
    public static string DragTheTitleStretchTheEdges => Get("DragTheTitleStretchTheEdges");
    public static string PreviousPage => Get("PreviousPage");
    public static string ProgramsYourToolsInOnePlace => Get("ProgramsYourToolsInOnePlace");
    public static string ShortcutsFindYourNextGame => Get("ShortcutsFindYourNextGame");
    public static string SpacesPickUpWhereYouLeft => Get("SpacesPickUpWhereYouLeft");
    public static string FoldersEverydayLifeWithinReach => Get("FoldersEverydayLifeWithinReach");
    public static string Categories => Get("Categories");
    public static string NoShortcutInThisView => Get("NoShortcutInThisView");
    public static string CustomizeThisShortcut => Get("CustomizeThisShortcut");
    public static string HideThisShortcut => Get("HideThisShortcut");
    public static string OpenTheQuickCommand2 => Get("OpenTheQuickCommand2");
    public static string SetATimer => Get("SetATimer");
    public static string NiceAfternoonDj => Get("NiceAfternoonDj");
    public static string SystemVolume => Get("SystemVolume");
    public static string SoundOn => Get("SoundOn");
    public static string PanneauxGlassControlCenter => Get("PanneauxGlassControlCenter");
    public static string ControlCenter => Get("ControlCenter");
    public static string YourWay => Get("YourWay");
    public static string PreviousTrack => Get("PreviousTrack");
    public static string VolumeAndBrightness => Get("VolumeAndBrightness");
    public static string Settings => Get("Settings");
    public static string ScreenArea => Get("ScreenArea");
    public static string ShutDownSleep => Get("ShutDownSleep");
    public static string Brightness => Get("Brightness");
    public static string Brightness2 => Get("Brightness2");
    public static string DisplaySettingsOpensTheWindowsOptions => Get("DisplaySettingsOpensTheWindowsOptions");
    public static string OpenTheWindowsFocusSettings => Get("OpenTheWindowsFocusSettings");
    public static string OpenTheWindowsAudioControls => Get("OpenTheWindowsAudioControls");
    public static string SystemVolumeDragOrUseThe => Get("SystemVolumeDragOrUseThe");
    public static string SoundMuted => Get("SoundMuted");
    public static string AutomaticSpotifyFirst => Get("AutomaticSpotifyFirst");
    public static string ConnectedToTheInternet => Get("ConnectedToTheInternet");
    public static string Connected => Get("Connected");
    public static string On => Get("On");
    public static string Off => Get("Off");
    public static string ToggleWiFiRightClickFor => Get("ToggleWiFiRightClickFor");
    public static string WiFiMissingOpenSettings => Get("WiFiMissingOpenSettings");
    public static string ToggleBluetoothRightClickForDevices => Get("ToggleBluetoothRightClickForDevices");
    public static string BluetoothMissingOpenSettings => Get("BluetoothMissingOpenSettings");
    public static string OpenTheWindowsConnections => Get("OpenTheWindowsConnections");
    public static string ReadingTheDisplaySetting => Get("ReadingTheDisplaySetting");
    public static string DragToAdjust => Get("DragToAdjust");
    public static string ClickBrightnessToOpenTheDisplay => Get("ClickBrightnessToOpenTheDisplay");
    public static string SettingUnavailableClickBrightnessForThe => Get("SettingUnavailableClickBrightnessForThe");
    public static string WindowsSettings => Get("WindowsSettings");
    public static string CloseCustomization => Get("CloseCustomization");
    public static string TheControlsYourWay => Get("TheControlsYourWay");
    public static string BrightnessSlider => Get("BrightnessSlider");
    public static string ChooseTheIcon => Get("ChooseTheIcon");
    public static string Screen => Get("Screen");
    public static string ShortcutIcon => Get("ShortcutIcon");
    public static string ChooseAnApplicationOrAValid => Get("ChooseAnApplicationOrAValid");
    public static string CloseTheTimer => Get("CloseTheTimer");
    public static string AFocusYourWay => Get("AFocusYourWay");
    public static string ChooseADurationInMinutesHours => Get("ChooseADurationInMinutesHours");
    public static string FocusDuration => Get("FocusDuration");
    public static string CustomDuration => Get("CustomDuration");
    public static string FocusDurationUnit => Get("FocusDurationUnit");
    public static string StartTheFocus => Get("StartTheFocus");
    public static string EndsAt => Get("EndsAt");
    public static string OpenThePage => Get("OpenThePage");
    public static string PreviousTitle => Get("PreviousTitle");
    public static string YourApplicationProfilesAndTheFour => Get("YourApplicationProfilesAndTheFour");
    public static string CreateOrEditMySpaces => Get("CreateOrEditMySpaces");
    public static string OpenTheWorkshop => Get("OpenTheWorkshop");
    public static string OpenTheFourPanels => Get("OpenTheFourPanels");
    public static string Shortcuts => Get("Shortcuts");
    public static string InThePanelsRightClickA => Get("InThePanelsRightClickA");
    public static string ExploreMyShortcuts => Get("ExploreMyShortcuts");
    public static string PreviousFavorites => Get("PreviousFavorites");
    public static string YourLatestOpeningsFromPanneauxMost => Get("YourLatestOpeningsFromPanneauxMost");
    public static string Recent => Get("Recent");
    public static string YourNextOpenedShortcutsWillAppear => Get("YourNextOpenedShortcutsWillAppear");
    public static string DragTheImageToMoveIt => Get("DragTheImageToMoveIt");
    public static string WholeImage => Get("WholeImage");
    public static string N256256PNGTheCheckerboardStays => Get("N256256PNGTheCheckerboardStays");
    public static string CloseTheNote => Get("CloseTheNote");
    public static string EverythingThatDoesNotDeserveA => Get("EverythingThatDoesNotDeserveA");
    public static string Modified => Get("Modified");
    public static string AnIdeaACommandALink => Get("AnIdeaACommandALink");
    public static string Saved => Get("Saved");
    public static string SavedAt => Get("SavedAt");
    public static string NoteCleared => Get("NoteCleared");
    public static string AvailableMemory => Get("AvailableMemory");
    public static string ReadingAndWriting => Get("ReadingAndWriting");
    public static string PanneauxGlassPCStatus => Get("PanneauxGlassPCStatus");
    public static string LiveMemory => Get("LiveMemory");
    public static string ReceiveSendRealThroughputNotAn => Get("ReceiveSendRealThroughputNotAn");
    public static string CollectionUnavailableReopenThePanel => Get("CollectionUnavailableReopenThePanel");
    public static string NoCardDetected => Get("NoCardDetected");
    public static string Video => Get("Video");
    public static string Used => Get("Used");
    public static string TemperatureNoMemoryModuleSensorAccessible => Get("TemperatureNoMemoryModuleSensorAccessible");
    public static string Write => Get("Write");
    public static string CyanReceivePurpleSend => Get("CyanReceivePurpleSend");
    public static string N1ReadingSSensors3S => Get("N1ReadingSSensors3S");
    public static string RealProcessorTemperatureReadEvery3 => Get("RealProcessorTemperatureReadEvery3");
    public static string UsedOf => Get("UsedOf");
    public static string LoadShareOfHardwareInUse => Get("LoadShareOfHardwareInUse");
    public static string TemperaturesPhysicalSensors => Get("TemperaturesPhysicalSensors");
    public static string ChartsLast60Readings => Get("ChartsLast60Readings");
    public static string StorageAverageDiskActivity => Get("StorageAverageDiskActivity");
    public static string OpenTaskManager => Get("OpenTaskManager");
    public static string LightlyLoaded => Get("LightlyLoaded");
    public static string Active => Get("Active");
    public static string HeavilyLoaded => Get("HeavilyLoaded");
    public static string DetectingHardware => Get("DetectingHardware");
    public static string YourWay2 => Get("YourWay2");
    public static string TheMaterialReflectionsAndDensityOf => Get("TheMaterialReflectionsAndDensityOf");
    public static string ReflectionIntensity => Get("ReflectionIntensity");
    public static string IconSize => Get("IconSize");
    public static string RenameColorAndReorderTheSurfaces => Get("RenameColorAndReorderTheSurfaces");
    public static string FirstRowHeight => Get("FirstRowHeight");
    public static string UseThePanneauxBarInsteadOf => Get("UseThePanneauxBarInsteadOf");
    public static string ShowTheHandleOnTheLeft => Get("ShowTheHandleOnTheLeft");
    public static string HandleWidth => Get("HandleWidth");
    public static string HandleHeight => Get("HandleHeight");
    public static string OfTheScreen => Get("OfTheScreen");
    public static string AnimationDuration => Get("AnimationDuration");
    public static string CollapseAfter18SecondsOutside => Get("CollapseAfter18SecondsOutside");
    public static string PauseTheEdgeDuringFullScreen => Get("PauseTheEdgeDuringFullScreen");
    public static string ShowThe => Get("ShowThe");
    public static string HiddenShortcutsAgain => Get("HiddenShortcutsAgain");
    public static string TheNativeRescueToolShowsAnd => Get("TheNativeRescueToolShowsAnd");
    public static string ShowOnlyTheDesktopIcons => Get("ShowOnlyTheDesktopIcons");
    public static string HideTheDesktopIcons => Get("HideTheDesktopIcons");
    public static string Done => Get("Done");
    public static string SavedAutomaticallyFilesStayWhereThey => Get("SavedAutomaticallyFilesStayWhereThey");
    public static string EachThemeChangesTheMaterialThe => Get("EachThemeChangesTheMaterialThe");
    public static string Theme => Get("Theme");
    public static string WhatYouUseEveryDayA => Get("WhatYouUseEveryDayA");
    public static string MotionIntensity => Get("MotionIntensity");
    public static string AlsoRememberCopiedImages => Get("AlsoRememberCopiedImages");
    public static string ItemsKept => Get("ItemsKept");
    public static string Items => Get("Items");
    public static string PINTOTHEFOREGROUND => Get("PINTOTHEFOREGROUND");
    public static string CAPTURETHESCREEN => Get("CAPTURETHESCREEN");
    public static string OnePressOpensYourSpaceIn => Get("OnePressOpensYourSpaceIn");
    public static string ToApply => Get("ToApply");
    public static string HomePCStatus => Get("HomePCStatus");
    public static string LastUsedView => Get("LastUsedView");
    public static string ViewToOpen => Get("ViewToOpen");
    public static string CtrlAltSpaceRemainsAvailableThe => Get("CtrlAltSpaceRemainsAvailableThe");
    public static string MoveThisPanelBeforeThePrevious => Get("MoveThisPanelBeforeThePrevious");
    public static string PanneauxGlassEditAShortcut => Get("PanneauxGlassEditAShortcut");
    public static string ThisShortcutYourWay => Get("ThisShortcutYourWay");
    public static string DISPLAYEDNAME => Get("DISPLAYEDNAME");
    public static string CATEGORY => Get("CATEGORY");
    public static string ChooseAnIcon => Get("ChooseAnIcon");
    public static string ChooseAnIcon2 => Get("ChooseAnIcon2");
    public static string OriginalIcon => Get("OriginalIcon");
    public static string Controls => Get("Controls");
    public static string CloseTheQuickTabs => Get("CloseTheQuickTabs");
    public static string WhatYouSeeAtTheTop => Get("WhatYouSeeAtTheTop");
    public static string ChangeTheIcon => Get("ChangeTheIcon");
    public static string IconOf => Get("IconOf");
    public static string GlobalShortcutActiveEvenWithLauncherClosed =>
        Get("GlobalShortcutActiveEvenWithLauncherClosed");
    public static string Screenshot => Get("Screenshot");
    public static string TwelveTabsAtMostBeyondThat => Get("TwelveTabsAtMostBeyondThat");
    public static string OpensTheQuickCommandEvenWith => Get("OpensTheQuickCommandEvenWith");
    public static string CreamClayAndSoftCardsThe => Get("CreamClayAndSoftCardsThe");
    public static string PlumAndVioletABandOf => Get("PlumAndVioletABandOf");
    public static string DeepGreenTurquoiseMistAndGlass => Get("DeepGreenTurquoiseMistAndGlass");
    public static string WarmAmberEndOfDayReflections => Get("WarmAmberEndOfDayReflections");
    public static string RosyPlumCherryBlossomHalos => Get("RosyPlumCherryBlossomHalos");
    public static string CreamPaperBrownInkAndSerif => Get("CreamPaperBrownInkAndSerif");
    public static string TerminalBlackPhosphorGreenMonospacedTypography =>
        Get("TerminalBlackPhosphorGreenMonospacedTypography");
    public static string NavyCoralAndMustardPrintedBlocks => Get("NavyCoralAndMustardPrintedBlocks");
    public static string LightStoneSoftShadowsGenerousCurves => Get("LightStoneSoftShadowsGenerousCurves");
    public static string CreamTrayAndSolidCellsBold => Get("CreamTrayAndSolidCellsBold");
    public static string NightBlackGlowingMagentaAndCyan => Get("NightBlackGlowingMagentaAndCyan");
    public static string DesktopGrayBeveledEdgesSquareCorners => Get("DesktopGrayBeveledEdgesSquareCorners");
    public static string WhiteSheetThinRulesSignalRed => Get("WhiteSheetThinRulesSignalRed");
    public static string LightSandGreenInkAndWide => Get("LightSandGreenInkAndWide");
    public static string CloseTheWorkshop => Get("CloseTheWorkshop");
    public static string TheUsefulEverydayGesturesGatheredIn => Get("TheUsefulEverydayGesturesGatheredIn");
    public static string FrameAnAreaDrawOnIt => Get("FrameAnAreaDrawOnIt");
    public static string FrameATextItIsCopied2 => Get("FrameATextItIsCopied2");
    public static string MagnifiesUnderTheCursorAndCopies => Get("MagnifiesUnderTheCursorAndCopies");
    public static string MeasuresADistanceOnScreenIn => Get("MeasuresADistanceOnScreenIn");
    public static string AWindowStaysInFrontOf => Get("AWindowStaysInFrontOf");
    public static string MutesOrRestoresWindowsNotifications => Get("MutesOrRestoresWindowsNotifications");
    public static string On2 => Get("On2");
    public static string Muted => Get("Muted");
    public static string WhenTheTaskbarOrTheDesktop => Get("WhenTheTaskbarOrTheDesktop");
    public static string ASheetStuckToTheScreen => Get("ASheetStuckToTheScreen");
    public static string EverythingYouCopiedReadyToCome2 => Get("EverythingYouCopiedReadyToCome2");
    public static string NothingInThisCategory => Get("NothingInThisCategory");
    public static string CloseTheHelp => Get("CloseTheHelp");
    public static string ThreeGesturesAndEverythingIsThere => Get("ThreeGesturesAndEverythingIsThere");
    public static string PanneauxStaysDiscreetAHandleOn => Get("PanneauxStaysDiscreetAHandleOn");
    public static string MoveThePointerToTheRight => Get("MoveThePointerToTheRight");
    public static string AlwaysOnTopPinTheActive => Get("AlwaysOnTopPinTheActive");
    public static string ReturnsToHome => Get("ReturnsToHome");
    public static string Esc => Get("Esc");
    public static string CtrlAltBackspace => Get("CtrlAltBackspace");
    public static string RestoresTheOrganizedDesktopEvenIf => Get("RestoresTheOrganizedDesktopEvenIf");
    public static string YOURSETTINGS => Get("YOURSETTINGS");
    public static string InPersonalizeThemeCalmStandardExpressive => Get("InPersonalizeThemeCalmStandardExpressive");
    public static string ShowAGlassSquareBehindThe => Get("ShowAGlassSquareBehindThe");
    public static string ASiteYourIcon => Get("ASiteYourIcon");
    public static string YourShortcutYourWay => Get("YourShortcutYourWay");
    public static string TheLogoStaysAloneOnScreen => Get("TheLogoStaysAloneOnScreen");
    public static string ImageReadyCropSavedWhenThe => Get("ImageReadyCropSavedWhenThe");
    public static string ThisImageCannotBeOpened => Get("ThisImageCannotBeOpened");
    public static string OpenInBrowser => Get("OpenInBrowser");
    public static string MoveLeft => Get("MoveLeft");
    public static string MoveRight => Get("MoveRight");
    public static string FolderDeletedOrRenamedBackTo => Get("FolderDeletedOrRenamedBackTo");
    public static string LargeIconsCtrlShift2 => Get("LargeIconsCtrlShift2");
    public static string BackAltLeft => Get("BackAltLeft");
    public static string Modified2 => Get("Modified2");
    public static string DetailsCtrlShift1 => Get("DetailsCtrlShift1");
    public static string CloseCtrlW => Get("CloseCtrlW");
    public static string AccessDenied => Get("AccessDenied");
    public static string OpenInExplorer => Get("OpenInExplorer");
    public static string UnableToOpenThisFolder => Get("UnableToOpenThisFolder");
    public static string ThisFolderContainsNoItems => Get("ThisFolderContainsNoItems");
    public static string OpenInANewTab => Get("OpenInANewTab");
    public static string PermanentlyDelete => Get("PermanentlyDelete");
    public static string PermanentlyDelete2 => Get("PermanentlyDelete2");
    public static string TheNameCannotBeEmpty => Get("TheNameCannotBeEmpty");
    public static string ThisNameAlreadyExistsInThis => Get("ThisNameAlreadyExistsInThis");
    public static string FilterCtrlFEscToClear => Get("FilterCtrlFEscToClear");
    public static string ClearTheFilterEsc => Get("ClearTheFilterEsc");
    public static string NoResults => Get("NoResults");
    public static string NoItemMatches2 => Get("NoItemMatches2");
    public static string Move => Get("Move");
    public static string ShortcutSaved => Get("ShortcutSaved");
    public static string CopiedImage => Get("CopiedImage");
    public static string Start2 => Get("Start2");
    public static string IconOrApplicationPngIcoExe => Get("IconOrApplicationPngIcoExe");

    public static string DesktopSettingNotApplied => Get("DesktopSettingNotApplied");
    public static string DesktopSettingNotKept => Get("DesktopSettingNotKept");
    public static string DesktopComponentMissing => Get("DesktopComponentMissing");
    public static string NoAudioSessionForPlayer => Get("NoAudioSessionForPlayer");
    public static string AppNoLongerHasAudioSession => Get("AppNoLongerHasAudioSession");
    public static string DeviceNoLongerConnected => Get("DeviceNoLongerConnected");
    public static string OpenMixerToAdjustApp => Get("OpenMixerToAdjustApp");
    public static string WindowsRefusedOutputChange => Get("WindowsRefusedOutputChange");
    public static string DisplayRefusedBrightness => Get("DisplayRefusedBrightness");
    public static string BrightnessNotAdjustable => Get("BrightnessNotAdjustable");
    public static string ConnectionChangeNotApplied => Get("ConnectionChangeNotApplied");
    public static string NoRecognitionEngine => Get("NoRecognitionEngine");
    public static string InvalidIcon => Get("InvalidIcon");
    public static string LogoFullyTransparent => Get("LogoFullyTransparent");
    public static string ActivationImpossible => Get("ActivationImpossible");

    public static string ModelDownloadIncompleteMessage(long megabytes) =>
        string.Format(Get("ModelDownloadIncomplete"), megabytes);

    // Category / folder names double as data keys in the board, tools and sidebar code (filters, card
    // categories, saved state). Only the displayed text goes through here; unknown keys (catalogue categories)
    // are shown as they are.
    public static string CategoryLabel(string key) =>
        Table.ContainsKey("Category." + key) ? Get("Category." + key) : key;

    public static string EverydaySelection => Get("EverydaySelection");
    public static string FavouritesPrefix => Get("FavouritesPrefix");
    public static string QuickAccess => Get("QuickAccess");
    public static string Drives => Get("Drives");
    public static string LocalDisk => Get("LocalDisk");

    public static string FreeOf(string free, string total) => string.Format(Get("FreeOf"), free, total);

    public static string KnownFolder(string key) => Table.ContainsKey("Folder." + key) ? Get("Folder." + key) : key;

    public static string OpenWithWinlestiaFiles => Get("OpenWithWinlestiaFiles");

    // Status line of Winlestia Files: "<count> · <selected>", with the singular/plural form of each part.
    public static string StatusLine(int shown, int total, bool filtered, int selected)
    {
        string count = filtered
            ? string.Format(Get(total > 1 ? "StatusFilteredMany" : "StatusFilteredOne"), shown, total)
            : string.Format(Get(shown > 1 ? "StatusCountMany" : "StatusCountOne"), shown);
        return $"{count} · {string.Format(Get(selected > 1 ? "StatusSelectedMany" : "StatusSelectedOne"), selected)}";
    }

    public static string DeletePermanentlyConfirm(int count, string firstName) =>
        count == 1
            ? string.Format(Get("DeletePermanentlyOne"), firstName)
            : string.Format(Get("DeletePermanentlyMany"), count);
}
