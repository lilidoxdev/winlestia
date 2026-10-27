# Winlestia 9.5.0

- L'installateur embarque le theme CMD/PowerShell et propose son installation. Profils interactifs, ASCII Winlestia, Clink, couleurs, verre Windows Terminal et adaptation au fond d'ecran. Les reglages existants sont sauvegardes. Les sessions elevees du meme compte utilisent les memes profils.
- Media : bouton volume cible a la place du bouton de deplacement ; pochette tournante pendant la lecture, angle conserve en pause.
- Le visualiseur utilise le niveau des sessions audio du lecteur selectionne, sans repli sur la sortie globale. C'est un indicateur d'amplitude, pas une analyse FFT par frequence. Le volume d'un navigateur concerne ses sessions, pas un onglet individuel.
- Dock : separation des groupes epingles/ouverts, deplacement des epingles par glissement, largeur reglable au bord droit et centrage conserve.
- Applications : onglets en cours/arriere-plan ; executables accessibles de la session, groupes par chemin. Les processus proteges ou inaccessibles ne sont pas listes.
- Alimentation : veille, redemarrage et arret avec confirmation.
- Reglages : hauteur de detection du bord superieur de 0 a 160 pixels, persistante.
- Fenetres maximisees : decoupe arrondie dans la zone de travail ; region precedente restauree au retour en mode normal ou a la fermeture du shell. Les pleins ecrans exclusifs restent geres par leur application.
- Curseurs : protection des valeurs choisies contre les retours d'etat anciens pendant les commandes audio et luminosite.
- Mise a jour : conservation du catalogue et des preferences backend ; correctif Explorer 9.4 conserve.

## Verification

32 tests Python ; chargement QML et menus avec etat de test ; protection contre etat obsolete ; installation/restauration terminal dans un environnement isole ; fixture native pour arrondis et ordre des epingles ; verification Core Audio avec session silencieuse dediee. Aucune commande d'arret, redemarrage ou veille n'est executee par ces tests. Le demarrage de session Windows et un second ordinateur restent a tester manuellement.

Le setup est gratuit, Windows 11 x64. Le depot reste prive : les testeurs doivent disposer d'un acces GitHub.
