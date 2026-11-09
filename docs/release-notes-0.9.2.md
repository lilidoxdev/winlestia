# Winlestia 0.9.2

Winlestia est un shell de bureau libre pour Windows 11, inspiré de Caelestia. Cette version refond les réglages et le gestionnaire de fichiers, et corrige la dictée vocale.

## Nouveautés

- Réglages redessinés en huit pages, avec recherche et navigation latérale ; éditeur de palette ; le terminal suit le mode clair ou sombre du thème.
- Page « Raccourcis clavier » : liste complète, réaffectation des raccourcis configurables, refus expliqué des combinaisons réservées ou en conflit. Win+Alt+S ouvre les Réglages.
- Gestionnaire de fichiers redessiné.
- Icônes dans le dock pour les applications empaquetées ; catalogue d'applications mis à jour en direct.
- Dock, signets et panneau social masquables séparément.
- « Ajouter un site web » est retiré.

## Corrections

- Dictée vocale : Whisper.net revient sur .NET 8.
- Flèches affichées comme des cases vides dans la liste des raccourcis de mosaïque.
- Raccourci non enregistré au démarrage signalé à tort « inchangé » ; raccourci Réglages en doublon remplacé par une combinaison libre.
- Dock, signets et panneau social non masqués avant le premier instantané des réglages.

## Installation

Télécharger `Winlestia-Setup-0.9.2.exe` ci-dessous, comparer son SHA-256 (`Get-FileHash`) à la valeur indiquée dans ces notes, puis lancer l'installateur. Il n'est pas signé : SmartScreen peut avertir (Informations complémentaires, puis Exécuter quand même). Guide : `docs/INSTALL.md`.

SHA-256 de `Winlestia-Setup-0.9.2.exe` : `1DB3D7994B8066195A7C14E67810400097762F5EBC3EB7FD88D5F3B7DEEEAA0F`

## Restauration

Si la barre des tâches Windows ne revient pas : Ctrl+Alt+Retour arrière, ou `--restore-taskbar`.

## Interne

Version unique dans `Directory.Build.props` ; CI (build, xUnit, pytest) ; tests regroupés ; blocs `catch` vides classés ; auto-tests du gestionnaire de fichiers exclus des builds Release ; code reformaté, backend et `frontend.py` découpés ; dépendances épinglées ; l'installateur vérifie l'empreinte du Python téléchargé.

Détail : `CHANGELOG.md`.
