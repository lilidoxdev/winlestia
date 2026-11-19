# Prompt pour générer un Winjet avec une IA
Collez le texte ci-dessous dans votre assistant, puis décrivez votre idée à la fin.
```text
Tu génères un paquet "Winjet" (widget de bureau Winlestia, format winjet/1) : uniquement des fichiers JSON, aucun code.
Réponds par un bloc de code par fichier : manifest.json, widget.json, puis chaque JSON de assets/.
Règles obligatoires :
- Les fichiers seront à la RACINE du ZIP : manifest.json, widget.json, preview.png (facultatif), assets/.
- manifest.json : format "winjet/1", id minuscules du type com.nom.monwinjet, name, version x.y.z, author,
  description, permissions.network = [] sauf si des hôtes web sont nécessaires (noms d'hôte exacts, HTTPS).
- widget.json : size {default, min, max} en pixels (grille : 168, 352, 536, 720), settings (toggle, text, choice, number),
  state, data (file sous assets/ ou http avec refresh >= 60), layouts du plus petit au plus grand
  (le premier a minWidth 0, le dernier dont les seuils sont atteints est utilisé).
- Conteneurs autorisés : column, row, stack, grid. Blocs autorisés, et aucun autre : text, image, clock, calendar,
  list, chart (variant line, area, bars, candles ; hover, valueFormat), stat, progress, dots, buttons, media, visualizer,
  spacer, divider. Le contenu se met à l'échelle tout seul avec la taille de la carte.
- Expressions : {{ settings.x }}, {{ state.x }}, {{ data.source.champ }}, {{ now }}, {{ media.title }} (title, artist, album,
  art, playing, position, length, canNext, canPrev, app), {{ system.cpu }} (cpu, ram en % de 0 à 100, uptime, battery, charging),
  {{ time.hours }} (hours, minutes, seconds). Indexation : data.m.symbols[state.pair].candles[state.tf]. Repli : a ?? b.
  Mise en forme : fmt(x, 'currency'|'percent'|'number') (percent attend une proportion). Aucun calcul (pas de division). Filtres :
  number:n, currency:EUR, percent:n, date:motif, upper, lower, default:valeur, first, last, delta.
- Conditions (visibleWhen, enabledWhen) sans accolades : == != < <= > >= && || ! et littéraux.
- Actions : setState (clé déclarée dans state), refresh, openUrl (hôte déclaré), openSettings, media.playPause, media.next, media.prev.
- Source data "market" : { "type": "market", "symbols": ["BTC","ETH"] } (4 au plus) ; permissions.network doit contenir api.binance.com et
  stream.binance.com. Valeur : data.market = {live, updated, symbols:{BTC:{price, change24h en %, candles:{"1h","1j","1s","1m"}}}}.
- chart candles : series = tableau de [t,o,h,l,c] ; hover:true écrit la valeur survolée dans state.hover (à déclarer dans state).
- Couleurs : seulement les rôles accent, primary, secondary, tertiary, onSurface, onSurfaceVariant, positive, negative.
  Jamais de #rrggbb ni rgb().
- Interdits : code, script, HTML, QML, CSS, SVG, exécutable, champ inconnu. Extensions : json png jpg jpeg webp gif txt md.
- Limites : ZIP 5 Mio, JSON 256 Kio chacun, 200 fichiers, 4 niveaux de dossiers.
À la fin, donne la commande PowerShell Compress-Archive qui crée le ZIP avec les fichiers à la racine.
Mon idée de Winjet :
```
