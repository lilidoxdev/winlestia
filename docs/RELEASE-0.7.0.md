# Winlestia 0.7.0 — theme personnalise

Quatrieme theme Personnalise, en plus de Porcelaine, Nuit et Automatique. Le studio reprend les cartes, arrondis et typographie du panneau Personnaliser.

- Cinq couleurs : fond, texte, principal, secondaire et troisieme accent. Les surfaces, contours et couleurs de contraste des boutons sont derives de ces roles. Palette initiale inspiree de Porcelaine.
- Roue HSV avec luminosite, saisie hexadecimale #RRGGBB et apercu avant application.
- Pipette d'ecran : interface masquee pendant l'echantillonnage d'un pixel ; clic intercepte temporairement sur un fil Windows dedie, sans hook permanent ni enregistrement d'image. Echap ou clic droit annule. Le hook est retire a la fin et a la fermeture.
- Verre optionnel : desactive, le fond choisi est opaque et le curseur disparait ; active, un curseur regle sa transparence. Le cadre, le dashboard et le dock utilisent les memes roles et le meme materiau.
- Couleurs et transparence personnalisees sauvegardees. Annuler l'editeur ne sauvegarde pas le brouillon. Le theme Automatique conserve son propre reglage de verre.
- Les terminaux reprennent la palette personnalisee ; Windows Terminal adapte aussi son opacite et son effet acrylique. Le mode allege conserve son comportement pour les configurations modestes.

Tests : palette et validation hexadecimale, roue/editeur QML sans erreur, modes opaque/verre, selection et annulation d'un pixel deterministe, persistance native des cinq roles et du verre, 32 regressions Python, import du service terminal et cycle de vie reel du hook temporaire dans le runtime embarque. Capture du studio inspectee. Les couleurs visibles peuvent dependre de la gestion HDR du bureau ; le rendu sur le second PC reste a tester.
