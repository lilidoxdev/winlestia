# Panneaux 8.2 — Atelier et audio natifs Caelestia

L’atelier, les sorties audio, les microphones et la capture utilisent maintenant les composants Qt de Caelestia : couleurs du thème, icônes Material, courbes d’animation, boutons et curseurs d’origine. Les accès à l’historique ouvrent la vue Qt existante. Ces vues restent dans la fenêtre native superposée ; aucun navigateur n’est nécessaire.

Dans **Commandes rapides**, la flèche à droite du volume déploie le **mixeur d’applications** sur le côté. Le groupe reste centré pendant son agrandissement. Chaque application ayant une session audio Windows dispose de son volume et de son bouton muet. Les sessions d’une même application sont regroupées, y compris sur plusieurs sorties. Windows conserve ses réglages audio. Une application sans session apparaît dès qu’elle crée une lecture audio ; aucune liste artificielle d’applications silencieuses n’est affichée.

Les vues **Sortie audio** et **Microphones** permettent de choisir le périphérique par défaut et de régler son niveau ou son bouton muet. Le microphone n’est pas enregistré. Les lectures des sessions s’arrêtent lorsque ces vues et le mixeur sont fermés. Les changements de valeur mettent à jour les délégués existants afin de garder un curseur stable sous la souris.

La capture propose sélection de zone, crayon, surligneur, annulation, copie, enregistrement PNG, reconnaissance de texte Windows et épinglage. **Ctrl F3** ouvre la sélection native. L’image reste en mémoire ; un fichier temporaire n’est créé que pour transmettre une capture à l’OCR ou à l’épinglage. L’OCR rejoint le presse-papiers. La capture concerne l’écran où se trouve la souris ; l’enregistrement permet de choisir un fichier.

Les outils spécialisés déjà existants (pipette, règle, papiers épinglés, gros fichiers et profils) restent accessibles depuis le nouvel atelier. Leur moteur historique est conservé. La version de secours du Bureau n’a pas été modifiée.

Validation : `validation-8.2/tools-report.json`, `validation-8.2/live-report.json` et `validation-8.2/audio-mixer-verification.json`. Le test audio crée sa propre session silencieuse, vérifie gain, muet, réactivation et bornes, puis restaure ses valeurs. Il vérifie également que les volumes des autres applications restent inchangés. Les tests de cycle de vie du dock et les tests de recherche/raccourcis sont conservés.

Sauvegardes avant modification : `Glass-v5/checkpoint-20261026-before-8.2` et `Caelestia-Windows/checkpoint-8.1-tools/frontend.zip`.
