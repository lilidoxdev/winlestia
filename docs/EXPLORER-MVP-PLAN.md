# Winlestia Fichiers — plan du MVP

Statut : plan, rien d'implémenté. Local uniquement, pas de push ni de release.

## Principes
- **Optionnel** : désactivé par défaut. Il s'active dans Personnaliser > Explorateur > « Ouvrir les dossiers avec Winlestia Fichiers ».
- **Aucune dépendance tierce, aucune injection.** Il n'utilise que les API shell publiques (COM).
- **Repli permanent sur Explorer.** Tout ce qui n'est pas un chemin de fichiers ordinaire, et tout échec, est renvoyé vers `explorer.exe`.
- **HKCU uniquement.** L'installateur reste en `PrivilegesRequired=lowest`, et c'est réversible à 100 %.
- **Identité unifiée.** Il reprend `Tokens.cs` et `UiTokens.cs` (palette M3, rayons 12/17/25, Rubik, Material Symbols, Effets allégés).

## Architecture
- **Exécutable séparé** : `src/files/Winlestia.Files.csproj` (WPF, net8.0-windows10.0.19041.0), publié à côté de `Panneaux.Glass.exe`. Un plantage n'emporte donc ni le dock ni le backend.
- **Partage des jetons** : les fichiers `Tokens.cs`, `UiTokens.cs` et `Visuals.cs` sont liés (`<Compile Include="..\backend\…" Link=…>`), sans copie.
- **Choix de WPF plutôt que QML** : `IContextMenu3` et `IDropTarget` ont besoin d'un HWND et de COM dans le même processus. QML ne garantit pas la virtualisation des vues.
- **Instance unique** : une deuxième ouverture envoie le chemin par pipe nommé et ouvre un nouvel onglet.
- **Thème** : à l'ouverture, on lit le même fichier de réglages que le backend (palette, `reducedEffects`). Un changement de palette est reçu par le pipe.

## Périmètre MVP
| # | Fonction | Mise en œuvre |
|---|---|---|
| 1 | Navigation : adresse (fil d'Ariane + saisie), précédent/suivant/parent, onglets | `IShellItem` / `SHCreateItemFromParsingName` |
| 2 | Volet latéral : Accès rapide (lecture), dossiers personnels, lecteurs | `SHGetKnownFolderPath`, `DriveInfo` |
| 3 | Liste et grille virtualisées, tri par nom/date/taille/type | `VirtualizingStackPanel` / `VirtualizingWrapPanel`, recyclage |
| 4 | Énumération asynchrone par lots de 256, annulable | `Directory.EnumerateFileSystemEntries` sur un thread de fond |
| 5 | Icônes et vignettes | `IShellItemImageFactory` sur un pool limité, cache LRU, annulation au défilement |
| 6 | Ouvrir, ouvrir avec | `ShellExecuteEx` (verbe par défaut) |
| 7 | Menu clic droit natif complet | `IContextMenu3` hébergé, avec en tête nos entrées stylées (Copier le chemin, Ouvrir dans Explorer) |
| 8 | Copier / couper / coller / supprimer (Corbeille) / renommer / nouveau dossier, avec annulation Windows | `IFileOperation` (progression native) |
| 9 | Glisser-déposer entrant et sortant | `DoDragDrop` / `IDropTarget` OLE |
| 10 | Rafraîchissement automatique | `SHChangeNotifyRegister`, avec mise à jour incrémentale sans reconstruction de liste |
| 11 | Filtre rapide sur le dossier courant (pas de recherche indexée) | Filtre en mémoire |
| 12 | États vides et d'erreur (accès refusé, dossier introuvable) | Bloc `EmptyState` |

**Hors MVP** : recherche Windows Search, volet d'aperçu, téléphones (MTP), zip comme dossier, réseau en navigation, bibliothèques, Win+E, épingle de la barre des tâches.

## Repli vers Explorer (`PathRouter`)
Le chemin passe par `explorer.exe "<arg>"` puis l'application se ferme, dans chacun de ces cas :
- l'argument commence par `shell:`, `::{`, `\\?\` ou `\\` (UNC), ou bien c'est un `.zip`, ou un chemin qui n'est pas un dossier existant ;
- le dossier est la Corbeille, le Panneau de configuration, Ce PC ou un ID shell virtuel ;
- l'énumération échoue pour une autre raison qu'un accès refusé.

La commande « Ouvrir dans Explorer » est toujours présente, dans le menu et en `Ctrl+Maj+E`.

**Anti-boucle** : le lancement d'Explorer utilise `explorer.exe` avec un chemin explicite et ne passe jamais par le verbe `Directory`.

## Enregistrement comme gestionnaire par défaut
Clés écrites seulement à l'activation :
```
HKCU\Software\Classes\Directory\shell\winlestia\command  (Default) = "<install>\Glass-v5\Winlestia.Files.exe" "%1"
HKCU\Software\Classes\Directory\shell                    (Default) = winlestia
HKCU\Software\Classes\Drive\shell\winlestia\command      (Default) = idem
HKCU\Software\Classes\Drive\shell                        (Default) = winlestia
```
- `Folder\shell` n'est **pas** touché, pour ne pas capturer les dossiers virtuels.
- Avant l'écriture, une sauvegarde des valeurs existantes est faite dans `%LOCALAPPDATA%\Winlestia\Files\handler-backup.json`.
- `--restore` remet ces valeurs exactement et supprime nos clés. Il est appelé à la désactivation, par l'installateur (`[UninstallRun]`) et par le garde-fou.

**Garde-fou :**
- Au démarrage, le backend vérifie que l'exe existe. S'il est absent, il appelle `--restore`.
- Trois plantages au démarrage en moins de 60 s désactivent l'option, appellent `--restore` et affichent un toast.
- Un journal `%LOCALAPPDATA%\Winlestia\Files\crash.log` garde au plus 200 lignes.

## Performance (critères mesurés, pas estimés)
- **Ouverture d'un dossier de 50 fichiers** : moins de 300 ms jusqu'à la première peinture, en démarrage à chaud. Le démarrage à froid est mesuré à part.
- **Dossier de 10 000 fichiers** : premières lignes en moins de 150 ms, aucun gel du fil d'interface de plus de 50 ms (minuteur de détection), mémoire privée sous 250 Mo.
- **Au repos** : 0 % de CPU et aucun minuteur actif quand la fenêtre est réduite. Les vignettes sont suspendues hors écran.
- **Fuites** : 50 ouvertures et fermetures d'onglets, puis 30 s de repos et un nouvel échantillon. Handles et threads doivent revenir à ±5 % de la mesure de départ.
- **Effets allégés** : pas d'animations ni de flou, comme le reste du shell.

## Étapes et validation
Chaque étape est validée à l'écran sur cette machine avant de passer à la suivante.

1. **Squelette** : projet, fenêtre unifiée, `PathRouter` et tests unitaires du routage (`tests/FilesRouter`). Ajout au `build.ps1` et à `Deploy-Local.ps1`.
2. **Navigation et liste** : énumération asynchrone, vue virtualisée, tri, onglets, volet latéral. Première mesure de performance.
3. **Icônes et vignettes** : pool de threads, cache, annulation. Nouvelle mesure.
4. **Actions** : ouvrir, `IFileOperation`, renommer, presse-papiers, menu natif, glisser-déposer.
5. **Rafraîchissement automatique**, états vides et d'erreur, filtre.
6. **Gestionnaire par défaut** : réglage dans Personnaliser, écriture des clés, sauvegarde, `--restore`, garde-fou. Test aller-retour sur une ruche de test, puis sur HKCU réel avec contrôle manuel du Bureau et de « Afficher dans le dossier ».
7. **Installateur** : fichiers dans `Winlestia.iss` et `--restore` à la désinstallation. Test d'installation et de désinstallation sur une session Windows propre (autre compte ou VM) : c'est le seul test « autre machine ».
8. **Revue** (sécurité du registre, fuites), puis rapport en quatre colonnes : modifié / testé / validé visuellement / imparfait ou dépendant de Windows.

## Limites connues, même terminé
- Win+E, l'épingle Explorer de la barre des tâches, `explorer.exe /select,` appelé directement par une application, et les boîtes Ouvrir/Enregistrer restent natifs.
- Le contenu du menu clic droit natif (entrées des autres logiciels) garde le style Windows. Seuls le cadre et nos entrées sont stylés.
