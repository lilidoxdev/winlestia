# Python 3.12 / Windows 11. Correspondance locale du lanceur et arithmétique bornée ; ni eval ni exécution de shell.
import ast, json, math, operator, os, re, shutil, threading, time, unicodedata
from pathlib import Path
from atomicfile import write_text as atomic_write_text


def tr(text, language='fr'):
    # Même table que le qsTr() du QML ; le français reste le texte source. La correspondance utilise toujours les
    # originaux français.
    try:
        from localization import TABLE
    except ImportError:
        return text
    return TABLE.get(text, {}).get(language, text)


def fold(value):
    return ''.join(c for c in unicodedata.normalize('NFD', value.lower()) if not unicodedata.combining(c))


COMMANDS = [
    ('dashboard', 'Tableau de bord', 'Horloge, calendrier et lecture', 'dashboard', 'navigate'),
    ('controls', 'Commandes Windows', 'Volume, écran et connexions', 'tune', 'navigate'),
    ('performance', 'Performances', 'Températures et activité du PC', 'speed', 'navigate'),
    ('workspace:apps', 'Applications', 'Tous tes logiciels', 'apps', 'navigate'),
    ('workspace:games', 'Jeux', 'Ta bibliothèque de jeux', 'sports_esports', 'navigate'),
    ('workspace:projects', 'Projets', 'Reprendre tes projets', 'code', 'navigate'),
    ('workspace:documents', 'Documents', 'Dossiers et médias', 'folder', 'navigate'),
    ('clipboard', 'Presse-papiers', 'Retrouver un élément copié', 'content_paste', 'navigate'),
    ('settings', 'Paramètres', 'Apparence et comportement', 'settings', 'navigate'),
    ('capture', 'Capture annotée', 'Encadrer une zone de l’écran', 'screenshot_monitor', 'command'),
    ('notes', 'Note rapide', 'Bloc-notes persistant', 'edit_note', 'command'),
    ('focus', 'Minuteur', 'Une session de concentration', 'timer', 'command'),
    ('audio', 'Son et micro', 'Périphériques et applications', 'speaker', 'command'),
    ('profiles', 'Profils d’espace', 'Retrouver une session de travail', 'workspaces', 'command'),
    ('quicktabs', 'Onglets rapides', 'Personnaliser les raccourcis de cette commande', 'bolt', 'command'),
    ('tools', 'Atelier', 'Les outils du quotidien', 'handyman', 'command'),
    ('taskmgr', 'Gestionnaire des tâches', 'Processus et démarrage', 'monitoring', 'command'),
    ('calculator', 'Calculatrice Windows', 'Ouvrir la calculatrice', 'calculate', 'command'),
    ('>wallpaper ', 'Fond d’écran', 'Wallpaper · changer le fond du bureau', 'wallpaper', 'autocomplete'),
    ('>emoji ', 'Emojis', 'Emoji · chercher et copier un emoji', 'mood', 'autocomplete'),
    ('>file ', 'Fichiers', 'File · chercher dans l’index Windows', 'folder_search', 'autocomplete'),
    ('shutdown', 'Éteindre', 'Shutdown · éteindre le PC (confirmation demandée)', 'power_settings_new', 'session'),
    ('restart', 'Redémarrer', 'Reboot · redémarrer le PC (confirmation demandée)', 'cached', 'session'),
    ('logout', 'Se déconnecter', 'Logout · fermer la session (confirmation demandée)', 'exit_to_app', 'session'),
    ('lock', 'Verrouiller', 'Lock · verrouiller la session (confirmation demandée)', 'lock', 'session'),
    ('sleep', 'Mettre en veille', 'Sleep · mise en veille (confirmation demandée)', 'bedtime', 'session'),
    ('hibernate', 'Hibernation', 'Hibernate · hiberner le PC (confirmation demandée)', 'downloading', 'session'),
]

# Pages Paramètres Windows : (URI ms-settings, fr, en, es, mots-clés). Seules ces URI peuvent être ouvertes depuis la palette.
SETTINGS_PAGES = (
    ('ms-settings:network-wifi', 'Wi-Fi', 'Wi-Fi', 'Wi-Fi', 'wifi reseau sans fil network wireless'),
    ('ms-settings:network', 'Réseau et Internet', 'Network & internet', 'Red e Internet', 'reseau network ethernet'),
    ('ms-settings:network-vpn', 'VPN', 'VPN', 'VPN', ''),
    (
        'ms-settings:bluetooth',
        'Bluetooth et appareils',
        'Bluetooth & devices',
        'Bluetooth y dispositivos',
        'bluetooth peripheriques devices',
    ),
    (
        'ms-settings:printers',
        'Imprimantes et scanners',
        'Printers & scanners',
        'Impresoras y escáneres',
        'printer imprimante scanner',
    ),
    ('ms-settings:display', 'Écran', 'Display', 'Pantalla', 'ecran display resolution luminosite brightness'),
    ('ms-settings:nightlight', 'Éclairage nocturne', 'Night light', 'Luz nocturna', 'night light lumiere bleue'),
    ('ms-settings:sound', 'Son', 'Sound', 'Sonido', 'audio volume sound haut-parleur speaker microphone'),
    ('ms-settings:notifications', 'Notifications', 'Notifications', 'Notificaciones', 'alertes alerts'),
    ('ms-settings:batterysaver', 'Batterie', 'Battery', 'Batería', 'batterie battery economie energie power'),
    (
        'ms-settings:powersleep',
        'Alimentation et mise en veille',
        'Power & sleep',
        'Energía y suspensión',
        'veille sleep power',
    ),
    ('ms-settings:storagesense', 'Stockage', 'Storage', 'Almacenamiento', 'disque disk espace storage'),
    ('ms-settings:multitasking', 'Multitâche', 'Multitasking', 'Multitarea', 'snap fenetres'),
    ('ms-settings:about', 'À propos', 'About', 'Acerca de', 'systeme system info version'),
    (
        'ms-settings:personalization',
        'Personnalisation',
        'Personalization',
        'Personalización',
        'theme fond wallpaper couleurs',
    ),
    ('ms-settings:personalization-colors', 'Couleurs', 'Colors', 'Colores', 'mode sombre dark accent'),
    ('ms-settings:taskbar', 'Barre des tâches', 'Taskbar', 'Barra de tareas', ''),
    (
        'ms-settings:appsfeatures',
        'Applications installées',
        'Installed apps',
        'Aplicaciones instaladas',
        'desinstaller uninstall programmes programs',
    ),
    (
        'ms-settings:defaultapps',
        'Applications par défaut',
        'Default apps',
        'Aplicaciones predeterminadas',
        'navigateur browser',
    ),
    (
        'ms-settings:startupapps',
        'Applications au démarrage',
        'Startup apps',
        'Aplicaciones de inicio',
        'demarrage startup',
    ),
    ('ms-settings:yourinfo', 'Comptes', 'Accounts', 'Cuentas', 'compte utilisateur account profil'),
    ('ms-settings:dateandtime', 'Date et heure', 'Date & time', 'Fecha y hora', 'fuseau timezone horloge clock'),
    ('ms-settings:regionformatting', 'Région', 'Region', 'Región', 'format pays country'),
    ('ms-settings:regionlanguage', 'Langue', 'Language', 'Idioma', 'langue language'),
    ('ms-settings:typing', 'Clavier', 'Keyboard', 'Teclado', 'saisie typing'),
    (
        'ms-settings:mousetouchpad',
        'Souris et pavé tactile',
        'Mouse & touchpad',
        'Ratón y panel táctil',
        'souris mouse touchpad curseur',
    ),
    ('ms-settings:easeofaccess', 'Accessibilité', 'Accessibility', 'Accesibilidad', 'loupe magnifier narrator'),
    ('ms-settings:privacy', 'Confidentialité', 'Privacy', 'Privacidad', 'securite security'),
    (
        'ms-settings:privacy-webcam',
        'Caméra (confidentialité)',
        'Camera privacy',
        'Privacidad de la cámara',
        'camera webcam',
    ),
    (
        'ms-settings:privacy-microphone',
        'Microphone (confidentialité)',
        'Microphone privacy',
        'Privacidad del micrófono',
        'micro',
    ),
    ('ms-settings:windowsupdate', 'Windows Update', 'Windows Update', 'Windows Update', 'mise a jour updates'),
)
SETTINGS_RU = {
    'ms-settings:network-wifi': 'Wi-Fi',
    'ms-settings:network': 'Сеть и Интернет',
    'ms-settings:network-vpn': 'VPN',
    'ms-settings:bluetooth': 'Bluetooth и устройства',
    'ms-settings:printers': 'Принтеры и сканеры',
    'ms-settings:display': 'Дисплей',
    'ms-settings:nightlight': 'Ночник',
    'ms-settings:sound': 'Звук',
    'ms-settings:notifications': 'Уведомления',
    'ms-settings:batterysaver': 'Батарея',
    'ms-settings:powersleep': 'Питание и спящий режим',
    'ms-settings:storagesense': 'Хранилище',
    'ms-settings:multitasking': 'Многозадачность',
    'ms-settings:about': 'О системе',
    'ms-settings:personalization': 'Персонализация',
    'ms-settings:personalization-colors': 'Цвета',
    'ms-settings:taskbar': 'Панель задач',
    'ms-settings:appsfeatures': 'Установленные приложения',
    'ms-settings:defaultapps': 'Приложения по умолчанию',
    'ms-settings:startupapps': 'Автозагрузка приложений',
    'ms-settings:yourinfo': 'Учётные записи',
    'ms-settings:dateandtime': 'Дата и время',
    'ms-settings:regionformatting': 'Регион',
    'ms-settings:regionlanguage': 'Язык',
    'ms-settings:typing': 'Клавиатура',
    'ms-settings:mousetouchpad': 'Мышь и сенсорная панель',
    'ms-settings:easeofaccess': 'Специальные возможности',
    'ms-settings:privacy': 'Конфиденциальность',
    'ms-settings:privacy-webcam': 'Конфиденциальность камеры',
    'ms-settings:privacy-microphone': 'Конфиденциальность микрофона',
    'ms-settings:windowsupdate': 'Центр обновления Windows',
}
SETTINGS_URIS = frozenset(page[0] for page in SETTINGS_PAGES)
SETTINGS_LABEL = {'fr': 'Paramètres Windows', 'en': 'Windows Settings', 'es': 'Configuración de Windows', 'ru': 'Параметры Windows'}

SESSION_KEYWORDS = {
    'shutdown': ('shut down', 'poweroff', 'eteindre', 'arreter', 'apagar', 'выключить', 'завершение работы'),
    'restart': ('reboot', 'redemarrer', 'reiniciar', 'перезагрузить', 'перезагрузка'),
    'logout': ('log out', 'sign out', 'deconnecter', 'deconnexion', 'cerrar sesion', 'выйти', 'выход'),
    'lock': ('verrouiller', 'bloquear', 'заблокировать', 'блокировка'),
    'sleep': ('veille', 'mettre en veille', 'suspend', 'suspender', 'спящий режим', 'сон'),
    'hibernate': ('hibernation', 'hiberner', 'hibernar', 'гибернация'),
}


def settings_uri_known(uri):
    return uri in SETTINGS_URIS


def settings_results(words, language='fr', limit=4):
    column = {'fr': 1, 'en': 2, 'es': 3}.get(language, 1)
    found = []
    for page in SETTINGS_PAGES:
        if all(w in fold(' '.join(page[1:]) + ' ' + SETTINGS_RU[page[0]]) for w in words):
            found.append(
                {
                    'id': page[0],
                    'name': SETTINGS_RU[page[0]] if language == 'ru' else page[column],
                    'comment': SETTINGS_LABEL.get(language, SETTINGS_LABEL['fr']),
                    'icon': 'settings',
                    'kind': 'setting',
                }
            )
    return found[:limit]


def window_results(needle, windows, limit=5, language='fr'):
    # Fenêtres ouvertes dont le titre ou le nom d'exécutable contient chaque mot saisi ; les fenêtres du shell (titres
    # « Panneaux · ... ») sont exclues.
    words = fold(needle).split()
    ranked = []
    for entry in windows or []:
        try:
            hwnd = int(entry.get('hwnd', 0))
        except (AttributeError, TypeError, ValueError):
            # Entrée de fenêtre malformée : ignorée.
            continue
        title = str(entry.get('title') or '')
        exe = str(entry.get('exe') or '')
        if not hwnd or not title or fold(title).startswith('panneaux ·'):
            continue
        if words and all(w in fold(title + ' ' + exe) for w in words):
            ranked.append((0 if fold(title).startswith(words[0]) else 1, hwnd, title, exe))
    ranked.sort(key=lambda v: v[0])
    return [
        {
            'id': 'window:%d' % hwnd,
            'hwnd': hwnd,
            'name': title,
            'comment': exe or tr('Fenêtre', language),
            'icon': 'web_asset',
            'kind': 'window',
        }
        for _, hwnd, title, exe in ranked[:limit]
    ]


CALC_WORDS = (
    (re.compile(r'(?<=[\d)\s])\s*divis[eé]e?\s+par\s*', re.I), '/'),
    (re.compile(r'\sfois\s', re.I), '*'),
    (re.compile(r'\splus\s', re.I), '+'),
    (re.compile(r'\smoins\s', re.I), '-'),
)
TIME_LIKE = re.compile(r'^\d{1,2}:\d{2}(:\d{2})?\s*(am|pm)?$', re.I)


# Conversion d'unités hors ligne : (catégorie, facteur vers l'unité de base). Les alias sont comparés après fold() et
# suppression des espaces, des « ° » et des points. Pas de devises (réseau). Octets : « ko/mo/go » en base 10 (usage
# français), « kio/mio/gio » en base 2. Gallon et once liquide : unités US.
_UNIT_TABLE = {}


def _units(category, base, *rows):
    for factor, names in rows:
        for name in names.split():
            _UNIT_TABLE[name] = (category, factor)


_units('length', 'm',
       (1e-6, 'um micron microns micrometre micrometres micrometer micrometers'),
       (1e-3, 'mm millimetre millimetres millimeter millimeters'),
       (1e-2, 'cm centimetre centimetres centimeter centimeters'),
       (0.1, 'dm decimetre decimetres'),
       (1, 'm metre metres meter meters'),
       (1000, 'km kilometre kilometres kilometer kilometers'),
       (0.0254, 'in inch inches pouce pouces "'),
       (0.3048, 'ft foot feet pied pieds'),
       (0.9144, 'yd yard yards'),
       (1609.344, 'mi mile miles'),
       (1852, 'nmi'))
_units('mass', 'kg',
       (1e-6, 'mg milligramme milligrammes milligram milligrams'),
       (1e-3, 'g gramme grammes gram grams'),
       (1, 'kg kilo kilos kilogramme kilogrammes kilogram kilograms'),
       (1000, 't tonne tonnes ton tons'),
       (0.45359237, 'lb lbs livre livres pound pounds'),
       (0.028349523125, 'oz once onces ounce ounces'))
_units('time', 's',
       (1e-3, 'ms milliseconde millisecondes millisecond milliseconds'),
       (1, 's sec seconde secondes second seconds'),
       (60, 'min mins minute minutes'),
       (3600, 'h hr hrs heure heures hour hours'),
       (86400, 'j d jour jours day days'),
       (604800, 'sem semaine semaines week weeks'),
       (31536000, 'an ans annee annees y yr yrs year years'))
_units('data', 'o',
       (0.125, 'bit bits'),
       (1, 'o octet octets byte bytes'),
       (1e3, 'ko kb'), (1e6, 'mo mb'), (1e9, 'go gb'), (1e12, 'to tb'), (1e15, 'po pb'),
       (1024, 'kio kib'), (1024**2, 'mio mib'), (1024**3, 'gio gib'), (1024**4, 'tio tib'))
_units('speed', 'm/s',
       (1, 'm/s mps'),
       (1 / 3.6, 'km/h kmh kph kmph'),
       (0.44704, 'mph mi/h'),
       (0.514444, 'kn kt kts noeud noeuds knot knots'),
       (0.3048, 'ft/s fps'))
_units('volume', 'l',
       (1e-3, 'ml millilitre millilitres milliliter milliliters cm3 cc'),
       (1e-2, 'cl centilitre centilitres centiliter centiliters'),
       (0.1, 'dl decilitre decilitres deciliter deciliters'),
       (1, 'l litre litres liter liters'),
       (1000, 'm3'),
       (3.785411784, 'gal gallon gallons'),
       (0.0295735295625, 'floz'))
_units('area', 'm2',
       (1e-6, 'mm2'),
       (1e-4, 'cm2'),
       (1, 'm2'),
       (1e6, 'km2'),
       (1e4, 'ha hectare hectares'),
       (0.00064516, 'in2 sqin'),
       (0.09290304, 'ft2 sqft'),
       (0.83612736, 'yd2 sqyd'),
       (4046.8564224, 'acre acres'),
       (2589988.110336, 'mi2 sqmi'))
_TEMPERATURES = {
    'c': 'c', 'celsius': 'c', 'degc': 'c', 'degreec': 'c', 'degrec': 'c', 'degres': 'c',
    'f': 'f', 'fahrenheit': 'f', 'degf': 'f',
    'k': 'k', 'kelvin': 'k', 'kelvins': 'k',
}
_CONVERT = re.compile(
    r'^([+-]?\d+(?:[.,]\d+)*(?:e[+-]?\d+)?)\s*(.+?)\s*(?:\s(?:en|in|to)\s|\s*→\s*)\s*(.+)$', re.I
)


def _unit_key(token):
    token = fold(token).replace('µ', 'u').replace('³', '3').replace('²', '2')
    return re.sub(r'[\s°.]', '', token)


_COMMA_DECIMAL = frozenset(('fr', 'es', 'de', 'it', 'pt', 'ru', 'nl', 'pl'))  # langues dont le séparateur décimal est la virgule
_THOUSANDS = re.compile(r'^[+-]?\d{1,3}(?:,\d{3})+(?:\.\d+)?(?:e[+-]?\d+)?$', re.I)


def _convert_shown(value):
    if abs(value) >= 1e6:
        if value.is_integer() and abs(value) < 2**53:
            return str(int(value))  # entier exact (1 gio en o = 1073741824), sans passer par la notation scientifique
        shown = format(value, '.12g')  # 12 chiffres significatifs : coupe le bruit flottant sans tronquer les grands nombres
        if 'e' in shown and abs(value) < 1e15:
            shown = format(float(shown), '.3f').rstrip('0').rstrip('.')
        return shown
    shown = format(value, '.6g')
    if 'e' in shown and 1e-6 <= abs(value) < 1e15:
        shown = format(float(shown), '.10f').rstrip('0').rstrip('.')
    return '0' if shown in ('-0', '') else shown


def convert_units(text, language='fr'):
    # « 12 km en mi », « 100 f to c », « 5 kg in lb » : valeur convertie suivie de l'unité d'arrivée telle que saisie.
    # None si la requête n'est pas une conversion valide (unité inconnue, catégories différentes, sous le zéro absolu).
    match = _CONVERT.match(text.strip()) if len(text) <= 160 else None
    if not match:
        return None
    try:
        number = match.group(1)
        if language not in _COMMA_DECIMAL and _THOUSANDS.match(number):
            number = number.replace(',', '')  # « 1,000 km in m » en anglais : la virgule sépare les milliers
        value = float(number.replace(',', '.'))
    except ValueError:
        return None
    source, target = _unit_key(match.group(2)), _unit_key(match.group(3))
    if not source or not target or not math.isfinite(value) or abs(value) > 1e100:
        return None
    if source in _TEMPERATURES and target in _TEMPERATURES:
        source, target = _TEMPERATURES[source], _TEMPERATURES[target]
        kelvin = {'c': lambda v: v + 273.15, 'f': lambda v: (v - 32) * 5 / 9 + 273.15, 'k': lambda v: v}[source](value)
        if kelvin < 0:
            return None
        result = {'c': lambda v: v - 273.15, 'f': lambda v: (v - 273.15) * 9 / 5 + 32, 'k': lambda v: v}[target](kelvin)
    else:
        if source not in _UNIT_TABLE or target not in _UNIT_TABLE or _UNIT_TABLE[source][0] != _UNIT_TABLE[target][0]:
            return None
        result = value * _UNIT_TABLE[source][1] / _UNIT_TABLE[target][1]
    if not math.isfinite(result) or abs(result) > 1e100:
        return None
    shown = _convert_shown(round(result, 9) if abs(result) < 1e6 else result)
    if language in _COMMA_DECIMAL:
        shown = shown.replace('.', ',')
    return shown + ' ' + match.group(3).strip()


def calculate(text, language='fr'):
    text = text.strip()
    if text.startswith('>calc '):
        text = text[6:].strip()
    if len(text) > 160 or TIME_LIKE.match(text):
        return None
    converted = convert_units(text, language)
    if converted is not None:
        return converted
    for pattern, symbol in CALC_WORDS:
        text = pattern.sub(symbol, text)
    text = text.replace(':', '/')
    if not any(c.isdigit() for c in text) or not any(c in '+-*/%^×÷(' for c in text.lstrip('+-')):
        return None
    try:
        tree = ast.parse(text.replace('^', '**').replace(',', '.').replace('×', '*').replace('÷', '/'), mode='eval')
        if sum(1 for _ in ast.walk(tree)) > 64:
            return None
        binary = {
            ast.Add: operator.add,
            ast.Sub: operator.sub,
            ast.Mult: operator.mul,
            ast.Div: operator.truediv,
            ast.Mod: operator.mod,
        }

        def run(node):
            if isinstance(node, ast.Constant) and type(node.value) in (int, float):
                value = float(node.value)
            elif isinstance(node, ast.UnaryOp) and isinstance(node.op, (ast.UAdd, ast.USub)):
                value = run(node.operand) * (1 if isinstance(node.op, ast.UAdd) else -1)
            elif isinstance(node, ast.BinOp):
                left, right = run(node.left), run(node.right)
                if isinstance(node.op, ast.Pow):
                    if abs(right) > 16 or abs(left) > 1e12:
                        raise ValueError()
                    value = left**right
                else:
                    value = binary[type(node.op)](left, right)
            else:
                raise ValueError()
            if not isinstance(value, (int, float)) or not math.isfinite(value) or abs(value) > 1e100:
                raise ValueError()
            return value

        shown = format(run(tree.body), '.12g')
        return shown.replace('.', ',') if language in _COMMA_DECIMAL else shown
    except (ValueError, SyntaxError, OverflowError, ZeroDivisionError, TypeError, KeyError):
        return None


# Classement des applications : score de texte par paliers + bonus d'usage plafonné. L'écart entre paliers dépasse le
# plafond du bonus, donc l'usage réordonne à l'intérieur d'un palier (et remonte un fuzzy fréquent au-dessus des fuzzy
# rares) sans jamais faire passer un palier sous un autre : un sous-séquence ne dépasse jamais un nom exact.
# Invariant (testé) : pour deux paliers voisins, plancher du palier haut (base - pénalité max) > base du bas + MAX_USAGE_BONUS.
TIER_EXACT, TIER_PREFIX, TIER_WORD, TIER_ACRONYM, TIER_SUBSTRING, TIER_FUZZY, TIER_CATEGORY = 1100, 940, 800, 660, 520, 360, 100
MAX_USAGE_BONUS = 120
# Pénalité maximale que name_score/app_score retire à la base de chaque palier (longueur du nom, position, trous du fuzzy).
TIER_MAX_PENALTY = {
    TIER_EXACT: 0, TIER_PREFIX: 10, TIER_WORD: 10, TIER_ACRONYM: 10, TIER_SUBSTRING: 20, TIER_FUZZY: 115, TIER_CATEGORY: 10,
}


def fuzzy_span(needle, name):
    # Plus petite fenêtre du nom contenant les lettres du motif dans l'ordre ; None sans sous-séquence.
    best = None
    start = name.find(needle[0])
    while start != -1:
        pos = start
        for char in needle[1:]:
            pos = name.find(char, pos + 1)
            if pos == -1:
                break
        else:
            if best is None or pos - start < best[1] - best[0]:
                best = (start, pos)
        start = name.find(needle[0], start + 1)
    return best


def name_score(needle, words, name):
    # needle et name sont déjà passés par fold(). None si le nom ne correspond pas.
    if not needle:
        return None
    tail = min(len(name) * 0.1, 10)  # à palier égal, le nom le plus court (donc le plus proche du motif) passe devant
    if name == needle:
        return TIER_EXACT
    if name.startswith(needle):
        return TIER_PREFIX - tail
    name_words = [w for w in re.split(r'[^a-z0-9]+', name) if w]
    if all(any(nw.startswith(w) for nw in name_words) for w in words):
        return TIER_WORD - tail
    if len(needle) >= 2 and ' ' not in needle and ''.join(w[0] for w in name_words).startswith(needle):
        return TIER_ACRONYM - tail
    if needle in name:
        return TIER_SUBSTRING - tail - min(name.index(needle) * 0.2, 10)
    if len(needle) >= 2 and ' ' not in needle:
        span = fuzzy_span(needle, name)
        if span is not None:
            gaps = span[1] - span[0] + 1 - len(needle)
            if gaps <= max(4, len(needle) * 2):
                return TIER_FUZZY - min(gaps * 4, 100) - min(span[0], 10) * 0.5 - tail
    return None


def usage_bonus(count, recent):
    # Fréquence logarithmique (0..80) + récence (0..40, rang 0 = dernier lancé), le tout plafonné à MAX_USAGE_BONUS.
    try:
        count, recent = max(int(count), 0), int(recent)
    except (TypeError, ValueError):
        return 0
    bonus = min(20 * math.log2(1 + count), 80)
    if 0 <= recent < 48:
        bonus += 40 * (1 - recent / 48)
    return min(bonus, MAX_USAGE_BONUS)


def app_score(needle, words, entry, count=0):
    name = fold(entry['name'])
    score = name_score(needle, words, name)
    if score is None and all(w in name + ' ' + fold(entry.get('category', '')) for w in words):
        score = TIER_CATEGORY - min(len(name) * 0.1, 10)
    return None if score is None else score + usage_bonus(count, entry.get('recent', -1))


def palette_search(query, state, language='fr', windows=None):
    raw = query.strip()
    needle = fold(raw.lstrip('>'))
    words = needle.split()
    results = []
    result = calculate(raw, language)
    if result is not None:
        results.append(
            {
                'id': raw,
                'name': '= ' + result,
                'comment': tr('Entrée pour copier le résultat', language),
                'icon': 'calculate',
                'kind': 'calc',
            }
        )
    if not raw and not raw.startswith('>'):
        for entry in state.get('systemRecent', []):
            results.append({**entry, 'kind': 'systemRecent'})
        for entry in state.get('recent', []):
            if not any(r['id'] == entry['id'] for r in results):
                results.append({**entry, 'kind': 'app', 'icon': 'apps', 'comment': tr('Récents', language)})
    if not raw.startswith('>'):
        for tab in state.get('quickTabs', []):
            name = tr(tab['name'], language)
            if not raw or all(w in fold(tab['name']) or w in fold(name) for w in words):
                results.append(
                    {**tab, 'name': name, 'kind': 'quickTab', 'icon': 'bolt', 'comment': tr('Onglet rapide', language)}
                )
        if raw:
            results.extend(window_results(needle, windows, language=language))
        entries = state.get('apps', []) if raw else []
        counts = state.get('launchCounts')
        counts = counts if isinstance(counts, dict) else {}
        ranked = []
        for entry in entries:
            score = app_score(needle, words, entry, counts.get(entry['id'], 0))
            if score is not None:
                ranked.append((-score, fold(entry['name']), entry))
        for _, _, entry in sorted(ranked, key=lambda v: v[:2])[:8]:
            results.append(
                {
                    **entry,
                    'comment': entry.get('category', '') + ' · ' + entry.get('board', ''),
                    'kind': 'app',
                    'icon': 'apps',
                }
            )
        for entry in state.get('websites', []):
            if all(w in fold(entry['name']) for w in words):
                results.append(
                    {**entry, 'kind': 'website', 'icon': 'language', 'comment': tr('Site internet', language)}
                )
    for key, name, detail, icon, kind in COMMANDS:
        if kind == 'session':
            # Les actions de session sont destructrices : préfixe de la requête entière sur le nom, mot-clé explicite
            # ou identifiant, au moins 3 lettres, jamais une sous-chaîne.
            if len(needle) < 3 or not any(
                c.startswith(needle) for c in [fold(name), key, *SESSION_KEYWORDS.get(key, ())]
            ):
                continue
        elif not all(w in fold(name + ' ' + detail) for w in words):
            continue
        results.append(
            {'id': key, 'name': tr(name, language), 'comment': tr(detail, language), 'icon': icon, 'kind': kind}
        )
    if raw and not raw.startswith('>'):
        results.extend(settings_results(words, language))
    if raw and not results:
        results.append(
            {
                'id': raw,
                'name': tr('Rechercher « %1 »', language).replace('%1', raw),
                'comment': tr('Ouvrir dans le navigateur', language),
                'kind': 'web',
                'icon': 'travel_explore',
            }
        )
    return results[:12]


_emoji_index = None


def emoji_choices(query):
    # Table embarquée lue à la première utilisation seulement ; chaque mot saisi est comparé aux mots-clés français et
    # anglais (sans tenir compte des accents).
    global _emoji_index
    if _emoji_index is None:
        from emoji_data import emoji_rows

        _emoji_index = [(glyph, fold(fr + ' ' + en), fr.split(' ')[0]) for glyph, fr, en in emoji_rows()]
    words = fold(query).split()
    return [
        {'id': glyph, 'name': glyph, 'comment': label, 'kind': 'emoji'}
        for glyph, hay, label in _emoji_index
        if all(w in hay for w in words)
    ]


# Données du sélecteur de fonds : balayées seulement quand le mode fond d'écran du lanceur le demande, sur un thread
# de travail au plus toutes les 30 s, en nombre borné.
WALL_EXT = {'.jpg', '.jpeg', '.png', '.bmp', '.webp'}
_walls = []
_walls_at = -100.0
_scanning = False
_rescan = False


def user_wallpaper_dir():
    return (
        Path(
            os.environ.get('WINLESTIA_DATA_DIR')
            or (Path(os.environ.get('LOCALAPPDATA', str(Path.home() / 'AppData' / 'Local'))) / 'Panneaux')
        )
        / 'Wallpapers'
    )


def bundled_wallpaper_dir():
    # Installé : <app>\wallpapers à côté de <app>\Caelestia-Windows ; depuis les sources : <repo>\assets\wallpapers.
    # Lecture seule.
    here = Path(__file__).resolve().parent
    for candidate in (here.parent / 'wallpapers', here.parent.parent / 'assets' / 'wallpapers'):
        if candidate.is_dir():
            return candidate
    return here.parent / 'wallpapers'


def wallpaper_dirs():
    home = Path(os.environ.get('USERPROFILE', str(Path.home())))
    windir = Path(os.environ.get('WINDIR', 'C:/Windows'))
    roots = [
        (user_wallpaper_dir(), 2),
        (bundled_wallpaper_dir(), 2),
        (home / 'Pictures' / 'Wallpapers', 2),
        (Path(os.environ.get('OneDrive', home / 'OneDrive')) / 'Pictures' / 'Wallpapers', 2),
        (home / 'Pictures', 0),
        (windir / 'Web' / 'Wallpaper', 2),
        (windir / 'Web' / '4K' / 'Wallpaper', 2),
    ]
    return roots


def _under(path, folder):
    try:
        p = os.path.normcase(os.path.abspath(path))
        f = os.path.normcase(os.path.abspath(str(folder)))
        return p.startswith(f + os.sep)
    except (OSError, ValueError):
        return False


def wallpaper_deletable(path):
    # Uniquement les fichiers du dossier utilisateur ; jamais les images embarquées ni celles de Images.
    return _under(path, user_wallpaper_dir()) and os.path.splitext(path)[1].lower() in WALL_EXT and os.path.isfile(path)


def wallpaper_import(paths):
    # Copie les images choisies dans le dossier utilisateur (nom unique en cas de collision) ; renvoie les nouveaux
    # chemins dans l'ordre.
    dest = user_wallpaper_dir()
    added = []
    try:
        dest.mkdir(parents=True, exist_ok=True)
    except OSError:
        return added
    for source in paths:
        source = str(source)
        if source.startswith('file:///'):
            source = source[8:].replace('/', os.sep)
        stem, ext = os.path.splitext(os.path.basename(source))
        if ext.lower() not in WALL_EXT or not os.path.isfile(source):
            continue
        target = dest / (stem + ext)
        n = 2
        while target.exists():
            target = dest / f'{stem} ({n}){ext}'
            n += 1
        try:
            shutil.copy2(source, target)
            added.append(str(target))
        except OSError:
            # Copie impossible (fichier verrouillé ou disparu) : ignorée.
            pass
    return added


def wallpaper_remove(path):
    if not wallpaper_deletable(path):
        return False
    try:
        os.remove(path)
    except OSError:
        return False
    return True


def hidden_wallpaper_file():
    return user_wallpaper_dir().parent / 'wallpapers-hidden.json'


def wallpaper_hidden():
    # Noms de fichiers (en minuscules) des fonds embarqués que l'utilisateur a masqués ; le dossier d'installation
    # n'est jamais touché.
    try:
        data = json.loads(hidden_wallpaper_file().read_text(encoding='utf-8'))
    except (OSError, ValueError):
        return set()
    return {str(n).lower() for n in data} if isinstance(data, list) else set()


def _hidden_write(names):
    f = hidden_wallpaper_file()
    try:
        if names:
            f.parent.mkdir(parents=True, exist_ok=True)
            atomic_write_text(f, json.dumps(sorted(names), ensure_ascii=False))
        elif f.exists():
            f.unlink()
    except OSError:
        return False
    return True


def wallpaper_hide(path):
    # Seules les images embarquées peuvent être masquées.
    if not _under(path, bundled_wallpaper_dir()) or os.path.splitext(path)[1].lower() not in WALL_EXT:
        return False
    return _hidden_write(wallpaper_hidden() | {os.path.basename(path).lower()})


def wallpaper_restore_hidden():
    return _hidden_write(set())


def wallpaper_scan(limit=200):
    found = []
    seen = set()
    names = set()
    user = user_wallpaper_dir()
    bundled = bundled_wallpaper_dir()

    def walk(folder, depth):
        try:
            with os.scandir(folder) as it:
                for e in it:
                    if len(found) >= limit:
                        return
                    if e.is_dir(follow_symlinks=False) and depth > 0:
                        walk(e.path, depth - 1)
                    elif (
                        e.is_file()
                        and os.path.splitext(e.name)[1].lower() in WALL_EXT
                        and e.path.lower() not in seen
                        and e.name.lower() not in names
                        and os.path.isfile(e.path)
                    ):
                        seen.add(e.path.lower())
                        names.add(e.name.lower())
                        origin = 'user' if _under(e.path, user) else 'bundled' if _under(e.path, bundled) else 'other'
                        found.append(
                            {
                                'id': e.path,
                                'path': e.path,
                                'name': os.path.splitext(e.name)[0],
                                'url': Path(e.path).as_uri(),
                                'kind': 'wallpaper',
                                'origin': origin,
                            }
                        )
        except OSError:
            # Dossier illisible : ignoré dans l'indexation.
            pass

    for folder, depth in wallpaper_dirs():
        walk(str(folder), depth)
    return sorted(found, key=lambda w: fold(w['name']))


def wallpaper_refresh(done, force=False):
    # Balayage sur thread de travail, au plus une fois par 30 s ; `done` est appelé depuis le worker quand le cache a
    # été remplacé.
    global _walls_at, _scanning, _rescan
    now = time.monotonic()
    if _scanning:
        if force:
            _rescan = True
        return
    if now - _walls_at < 30 and not force:
        return
    _scanning = True
    _walls_at = now

    def run():
        global _walls, _scanning, _rescan
        try:
            _walls = wallpaper_scan()
            while _rescan:
                _rescan = False
                _walls = wallpaper_scan()
        finally:
            _scanning = False
        done()

    threading.Thread(target=run, daemon=True).start()


def wallpaper_choices(query=''):
    words = fold(query).split()
    hidden = wallpaper_hidden()
    return [
        w
        for w in _walls
        if os.path.isfile(w['path'])
        and not (w['origin'] == 'bundled' and os.path.basename(w['path']).lower() in hidden)
        and all(x in fold(w['name']) for x in words)
    ]


def wallpaper_known(path):
    return os.path.isfile(path) and any(w['path'] == path for w in _walls)
