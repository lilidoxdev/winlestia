# Logos des sites

Les silhouettes de Gmail et GitHub proviennent de leurs ressources officielles publiques, récupérées le 20 octobre 2026 :

- Gmail : https://ssl.gstatic.com/ui/v1/icons/mail/rfr/gmail.ico
- GitHub : https://github.githubassets.com/favicons/favicon.png

Le launcher les décode en PNG avec alpha. Les logos monochromes foncés utilisent une silhouette claire pour rester lisibles sur le verre sombre. Aucun fond carré n’est ajouté.

Les autres sites sont interrogés directement : liens `rel=icon`, `apple-touch-icon`, puis `/favicon.ico`. Aucun service tiers de collecte d’URL n’est utilisé. Le globe de secours est dessiné localement par le programme.
