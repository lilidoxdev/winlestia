# Winlestia 0.6.0

La numerotation passe de 9.5.0 a 0.6.0 ; le setup accepte la mise a jour avec le meme identifiant.

- Spectre FFT dynamique a 48 bandes : capture WASAPI du processus du lecteur et de ses enfants, sans repli sur le son global. Analyse en memoire, aucun enregistrement. Test reel avec signal sinusoidal et processus exclu reussi.
- Menu alimentation : cartes avec icones, confirmation, position au-dessus du dock.
- Fluidite : retrait du hook souris global de fermeture des panneaux, remplacement par detection des transitions de boutons ; suppression des abonnements globaux bruyants aux changements de position/etat ; cadence du mixeur reduite. Le ralentissement du PC du collegue reste a confirmer sur cette machine, sa cause exacte n'est pas prouvee.
- Langues : detection de la langue d'affichage Windows (FR/EN/ES, anglais sinon), choix manuel persistant dans Personnalisation et premier catalogue de traduction de l'interface principale. Certains libelles secondaires et dialogues Windows/herites restent dans leur langue initiale ; la couverture n'est pas encore exhaustive.
- Meteo : en absence de ville manuelle, estimation de ville par adresse IP (wttr.in), puis meteo Open-Meteo. Ce n'est pas une localisation GPS ; VPN/reseau peuvent affecter la ville.
- Ctrl+F2 : raccourcis recents du dossier Windows Recent en tete ; applications observees au premier plan depuis le lancement sont aussi prises en compte. Les historiques Windows desactives/proteges ne sont pas contournes.
- Calibration : bouton Calibrer dans Personnalisation, panneau principal masque, reglage isole et ligne rouge non interactive sur l'ecran. Valider avec la coche ; Echap annule.

Tests : 32 tests Python ; runtime QML/menus sans erreur ; FFT/isolation native ; choix/persistance des langues et ordre recent d'abord. Le PC du collegue et une vraie ouverture de session restent a tester.

Implementation WASAPI : https://learn.microsoft.com/en-us/samples/microsoft/windows-classic-samples/applicationloopbackaudio-sample/
