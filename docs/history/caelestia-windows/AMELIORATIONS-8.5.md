# Panneaux 8.5 — Correction du voile noir et épaisseur réglable

La couche Acrylic du cadre ne possède plus de fenêtre de la taille de l’écran avec un trou central. Elle utilise huit fenêtres natives bornées : quatre bandes et quatre coins. Même si le compositeur applique un fond opaque ou ignore un découpage, ces fenêtres ne peuvent pas peindre le centre du bureau.

Les auxiliaires restent cachés jusqu’à la validation complète de leur géométrie. Les dimensions invalides, un découpage refusé ou une mise à jour incohérente masquent le verre. L’API destinée aux panneaux refuse les fenêtres presque aussi larges et hautes qu’un écran. Les auxiliaires ne changent plus le propriétaire des fenêtres Qt, ne reçoivent pas les clics et disparaissent à la destruction ou au masquage du contenu. La réapparition après masquage natif est également vérifiée.

Le réglage **Personnaliser → Apparence → Épaisseur des contours** va de **4 à 40 pixels logiques**, dans les trois thèmes, avec 16 px par défaut. Le cadre, le raccord du dashboard et le dock suivent immédiatement cette valeur. Windows réserve l’épaisseur choisie plus quatre pixels pour les applications. La valeur est enregistrée dans les préférences. Le curseur de transparence reste disponible dans le mode Automatique.

Validation de la version :

- `validation-8.5/desktop-regression-report.json` : véritable capture du bureau de test sous le compositeur, pixels centraux inchangés à 4/16/40 px, auxiliaires bornés, refus d’un voile plein écran, fermeture en cas d’erreur, masquage/réaffichage et destruction.
- `validation-8.5/theme-report.json` : trois surfaces identiques, transparence, thèmes fixes opaques, géométrie native et QML, changement d’épaisseur, plein écran et capture.
- `validation-8.5/native-blur-report.json` : flou réel conservé derrière une fenêtre de test.
- `validation-8.5/live-thickness-report.json` : curseur, frontend, backend et disque synchronisés ; marges Windows réellement mesurées à 8, 20 et 44 px pour des épaisseurs de 4, 16 et 40 px.
- Les 27 vérifications Python de recherche, comportement du dock et palette passent.

Sauvegardes : `checkpoint-8.4-before-8.5` et `../Glass-v5/checkpoint-20261026-before-8.5`. Préférences précédentes conservées. L’application reste fermée après l’installation pour le test final de dj.

Le masquage des icônes du bureau est rétabli au démarrage normal. Les icônes sont réaffichées à la fermeture ou si le moteur Qt s’arrête. Le cycle démarrage/fermeture a été vérifié sur Windows.
