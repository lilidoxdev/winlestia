# Panneaux 8.3 — Cadre Caelestia et thème du fond d’écran

Un contour permanent de 12 pixels logiques entoure chaque écran. Il utilise le masque arrondi du composant `modules/drawers/Border.qml` de Caelestia. Le dock reprend le tracé inférieur de `modules/launcher/Background.qml`. Les fenêtres de ces contours sont natives, transparentes au clic et incapables de prendre le focus. Le panneau supérieur et le dock sortent maintenant du contour. Le contour se masque sur l’écran d’une application en plein écran et pendant une capture.

Windows réserve une marge de 20 pixels logiques sur chaque bord, ajustée au DPI. Les fenêtres maximisées suivent cette zone de travail ; les fenêtres ordinaires ouvertes ou déplacées sont ramenées à l’intérieur si elles débordent. Le plein écran reste exclu de ce placement. La réservation utilise les appbars Windows et leurs fenêtres de contrôle : elle disparaît avec le processus. À la fermeture normale, Panneaux retire les réservations et restitue les positions qu’il a ajustées, tant qu’elles n’ont pas été modifiées entre-temps. Le réglage **Cadre de l’écran** permet également de le désactiver.

Dans **Personnaliser → Apparence**, trois modes sont disponibles : **Porcelaine**, **Nuit** et **Automatique**. Les deux palettes fixes sont conservées. Le mode automatique lit le fond déclaré par Windows pour chaque écran, puis produit une palette Material You : surfaces, textes, pictogrammes et contrôles. Les familles de teintes sont regroupées pour identifier la couleur dominante ; les fonds monochromes restent neutres. La luminosité moyenne du fond décide de la variante claire ou sombre.

Le fond d’écran n’est jamais capturé à l’écran et n’est envoyé à aucun service. Windows est interrogé toutes les cinq secondes ; une nouvelle analyse n’est faite que si le fichier ou sa date de modification change. Le calcul travaille sur une miniature de 128 pixels, sur un fil distinct. Pour les logiciels de fond animé qui ne publient pas leur image via Windows, la référence utilisée est l’image déclarée par Windows.

Le mixeur audio dispose maintenant de son propre fil COM : détection des périphériques, recherche des sessions et réglages des applications ne s’exécutent plus sur le fil de l’interface. L’ouverture d’une vue active ce service sans attendre la détection. Les valeurs se mettent à jour ensuite et le suivi s’arrête à la fermeture. Les transitions des menus audio et des commandes rapides passent de 350 à 210 ms à l’échelle normale, en conservant les courbes de Caelestia.

Validation locale :

- `validation-8.3/theme-report.json` : palettes bleue, jaune, monochrome, contraste, transparence du contour, passage des clics, plein écran et capture.
- `validation-8.3/frame-verification.json` : réservation réelle des quatre bords Windows et restitution de la zone de travail.
- `validation-8.3/audio-mixer-verification.json` : réglages réels d’une session silencieuse dédiée et activation asynchrone du service audio.
- `validation-8.3/live-report.json` : parcours des menus avec les périphériques et le fond d’écran de ce PC, premières images rendues et mesure au repos.

Sources originales : [Caelestia Shell](https://github.com/caelestia-dots/shell), commit `04bfc87f65d6e13b287358e1024d9bbc055b0c58`. Adaptation Windows et correspondance dans `PORT_PATCHES.json`. Sauvegardes avant cette version : `Glass-v5/checkpoint-20261026-before-8.3` et `Caelestia-Windows/checkpoint-8.2-adaptive/frontend.zip`.
