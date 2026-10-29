# Panneaux 8.4 — Verre unifié et accents du fond réel

Le contour passe de 12 à 16 pixels logiques. Le dashboard, le dock et le contour utilisent exactement la même couleur et la même opacité ; le trait extérieur des panneaux et le recouvrement d’un pixel à leur jonction sont supprimés. Porcelaine et Nuit restent opaques.

En Automatique, les trois surfaces reçoivent un hôte Acrylic Windows séparé, découpé selon leur silhouette. Il floute le contenu réellement derrière les fenêtres, y compris un fond animé. Le centre du cadre reste absent de la région native. La matière utilise une base presque noire teintée selon le fond ; elle ne passe plus en blanc sur un fond lumineux. Les cartes et surfaces secondaires sont également translucides en Automatique et suivent ce même réglage. Le réglage **Personnaliser → Apparence → Transparence du verre**, de 5 à 80 %, agit immédiatement et est enregistré.

La palette utilise la présence chromatique : pixels saturés et visibles regroupés en familles de teintes. Les ombres bleues très sombres ne prennent plus le dessus sur les fleurs rouges. Les pictogrammes de navigation et les contrôles reprennent cet accent ; les logos des applications conservent leur identité. Les fonds monochromes gardent des accents neutres. Le contraste des accents et du texte est contrôlé sans décaler la teinte.

Le logiciel lit toujours le fond Windows par écran. Quand DesktopHut est actif, il lit aussi sa configuration, y compris les chemins virtualisés MSIX, et analyse la miniature du fond réellement sélectionné. Il ne dépend donc plus du vieux fond statique Windows pendant la lecture de la vidéo. Le fond actuel dark-knight-in-red-flowers produit l’accent #fa3c5b. Le changement de source est recherché toutes les quatre secondes ; Windows actualise sa liste toutes les cinq secondes. Les configurations DesktopHut sont lues uniquement. Les autres moteurs non reconnus utilisent la source publiée par Windows.

Le suivi du verre est déclenché par les mouvements, dimensions, visibilité et changements de thème, avec une surveillance légère toutes les 250 ms. La fermeture différée du contenu du dashboard ne dépend plus de sa propre hauteur animée, ce qui supprime une boucle de liaison QML à la réouverture.

## Vérification

- 27 vérifications Python : comportement du dock, recherche et accents rouge/bleu/vert/jaune/neutre, ombres sombres et contraste.
- validation-8.4/theme-report.json : comparaison des pixels des trois surfaces, alpha réglable, modes opaques, régions natives, passage des clics, plein écran, capture et absence d’avertissements QML.
- validation-8.4/native-blur-report.json : bandes de 8 pixels derrière une fenêtre transparente ; l’écart type passe d’environ 79 à moins de 1 après activation du verre.
- validation-8.4/live-material-report.json : fond réel DesktopHut, accent rouge, bascule des modes, retour du réglage par le backend et persistance sur disque.

Sauvegardes : checkpoint-8.3-before-8.4 et ../Glass-v5/checkpoint-20261026-before-8.4. Les sources C++ et le script build-qt-glass.cmd sont inclus ; aucun nouveau paquet Python n’est requis.

Références : [DWM et régions de flou](https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ns-dwmapi-dwm_blurbehind), [composition des fenêtres](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos). La couche Acrylic réutilise le mécanisme Windows déjà employé par le backend ; sa fenêtre de rendu est maintenant placée immédiatement sous le contenu Qt.
