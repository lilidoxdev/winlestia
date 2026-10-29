# Panneaux 8.6 — Suppression des rectangles noirs

Les fonds Acrylic sont remplacés par des surfaces de composition transparentes. Le flou du contenu derrière chaque fenêtre est découpé par un masque géométrique au niveau du compositeur. Les zones extérieures à la forme restent transparentes.

Le cadre conserve huit auxiliaires bornés aux bords et aux coins. Le dashboard et le dock utilisent le même masque. Transparence, épaisseur, thèmes et masquage des icônes du bureau sont conservés.

Le test reproduit le défaut avec la DLL 8.5 : 1 936 pixels sombres dans les quatre zones de coin testées. La DLL 8.6 en produit zéro aux épaisseurs 4, 16 et 40 px. Les pixels du centre restent inchangés. Les tests de fermeture, masquage et destruction passent.

Le test de flou conserve une luminance moyenne de 84/255 tout en supprimant les bandes fines : il vérifie un flou effectif et exclut un fond noir. Les contrôles QML des trois surfaces et des réglages passent sans avertissement.

Rapports : validation-8.6/. baseline-corner-report.json utilise volontairement l’ancienne DLL ; son échec reproduit le défaut. Les autres rapports concernent la version corrigée.

Sauvegardes : checkpoint-8.5-before-8.6 et ../Glass-v5/checkpoint-20261026-before-8.6.
