# Winlestia 0.6.5.1

- Reparation de l'installation du theme PowerShell : Set-ExecutionPolicy pouvait modifier CurrentUser puis signaler ExecutionPolicyOverride a cause de la politique du processus du setup. Le helper verifie maintenant la valeur relue et accepte uniquement une modification effectivement reussie. Les restrictions de groupe restent respectees.
- Le theme CMD/PowerShell est inclus automatiquement, y compris lors d'une mise a jour ayant conserve une ancienne liste de taches.
- Synchronisation initiale de la palette avant le lancement du worker ; arret de l'ancien worker enregistre ; mutex propre au dossier d'installation. Les fichiers de couleurs sont publies meme si Windows Terminal n'a pas encore de configuration.
- Windows Terminal nouvellement initialise : ajout du schema Winlestia manquant et activation de son apparence. Les editions Preview sont aussi detectees. Le CMD classique utilise les couleurs RVB du theme ; Windows Terminal conserve ses couleurs indexees adaptatives.
- Ctrl+F2 : extraction des icones des applications et fichiers/dossiers recents, et demande de chargement des la creation des lignes.
- Echecs d'installation : stderr du helper est conserve dans install-result.json pour un diagnostic exploitable.

## Verification

PowerShell 5.1 interactif dans un vrai pseudo-terminal : logo ASCII, panneau systeme et prompt affiches, couleur rouge RVB verifiee, fermeture propre. Test reproduisant ExecutionPolicyOverride avec changement applique, puis restauration de la valeur precedente. Tests de palettes rouge/bleue et de configuration Terminal neuve ; test d'import du worker avec le Python embarque ; installation/reinstallation/restauration isolees ; regressions QML sans erreur.

Le rendu du fond transparent exige un hote compatible tel que Windows Terminal. Une strategie d'entreprise interdisant les profils non signes doit etre geree par son administrateur. Le PC du collegue reste a retester ; les tests locaux ne constituent pas une verification sur cette machine.
