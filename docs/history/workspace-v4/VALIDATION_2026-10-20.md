# Panneaux v4 — Vérification du 20 octobre 2026

## Version livrée

Application native .NET Framework compilée en exécutable Windows x64. Sources conservées dans ce dossier. Ancienne application PowerShell conservée dans le dossier parent.

## Contrôles réussis

- Compilation C# terminée sans erreur ni avertissement sur la version finale.
- Tous les scripts PowerShell passent l’analyse syntaxique.
- Inventaire généré et régénéré : **355 accès**, aucun diagnostic de collecte.
- **254 applications et outils**, **76 dossiers**, **24 jeux référencés** et **1 accès Rocket League pour terminer son installation dans Epic**.
- Aucun identifiant dupliqué et aucun doublon de nom parmi les logiciels/jeux.
- Aucune cible locale absente dans le catalogue.
- Canada pointe sur `Documents\04_Documents_Administratifs\Canada`.
- Les raccourcis Ubisoft Anno 1800, The Crew Motorfest et The Division 2 dont les installations sont absentes sont écartés.
- Les jeux et applications encore présents sur E: sont pris en compte, indépendamment des suppressions décrites dans le rapport précédent.
- Recherche « canada » vérifiée dans l’interface : Canada et ses deux sous-dossiers apparaissent.
- Favori Canada ajouté dans l’interface et retrouvé après arrêt puis relancement.
- Repli par Échap vérifié à l’écran.
- Déclenchement de la poignée par le pointeur au bord gauche vérifié à l’écran.
- Clic sur la poignée : réouverture du panneau vérifiée à l’écran.
- Ouverture de Canada : fenêtre Explorateur et chemin réel vérifiés.
- Arrêt du processus par son canal d’instance unique vérifié après correction de la libération des ressources.
- Registre de démarrage : entrée Panneaux vers `Workspace-v4\Panneaux.Workspace.exe`.
- Réglage Windows `HideIcons=1`, appliqué par le Bureau Windows.
- Ancien réglage du Bureau, ancien démarrage et anciens raccourcis sauvegardés avant intégration.

## Limites de vérification

La session Windows n’a pas été redémarrée. Le démarrage automatique a été contrôlé par sa configuration et par le lancement du même exécutable en arrière-plan. Les jeux et logiciels n’ont pas tous été lancés ; les cibles locales et les manifestes ont été contrôlés. Le comportement multi-écrans et plein écran est implémenté, mais n’a pas été essayé sur plusieurs écrans ni pendant une partie.

Rocket League dispose de données sur disque, mais son manifeste Epic est dans `Pending`, avec `bIsIncompleteInstall=true` et un exécutable de lancement manquant. Sa tuile ouvre Epic et indique « installation à terminer ».
