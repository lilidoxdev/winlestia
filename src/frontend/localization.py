# Python 3.12 / localization.py / Qt application translations and Windows UI language detection.
import ctypes, json
from pathlib import Path
from atomicfile import write_text as atomic_write_text
from PySide6.QtCore import QTranslator

ROWS = """Paramètres|Settings|Ajustes
Personnaliser|Customize|Personalizar
Personnalisation|Customization|Personalización
Apparence|Appearance|Apariencia
Thème|Theme|Tema
Automatique|Automatic|Automático
Applications|Applications|Aplicaciones
Jeux|Games|Juegos
Projets|Projects|Proyectos
Documents|Documents|Documentos
Tableau de bord|Dashboard|Panel
Média|Media|Multimedia
Performances|Performance|Rendimiento
Espaces de travail|Workspaces|Espacios de trabajo
Commande rapide|Quick command|Comando rápido
Presse-papiers|Clipboard|Portapapeles
Note|Note|Nota
Minuteur|Timer|Temporizador
Atelier|Tools|Herramientas
Tout afficher|Show all|Mostrar todo
Tes favoris|Your favorites|Tus favoritos
Récents|Recent|Recientes
Reprendre ton espace|Resume your workspace|Retomar tu espacio
Ajouter un site|Add website|Añadir sitio
En cours|Running|En ejecución
Arrière-plan|Background|Segundo plano
Alimentation|Power|Energía
Mettre en veille|Sleep|Suspender
Verrouiller|Lock|Bloquear
Se déconnecter|Log out|Cerrar sesión
Hibernation|Hibernate|Hibernar
Redémarrer|Restart|Reiniciar
Éteindre|Shut down|Apagar
Confirmer|Confirm|Confirmar
Annuler|Cancel|Cancelar
Fermer|Close|Cerrar
Retour|Back|Volver
Enregistrer|Save|Guardar
Modifier|Edit|Editar
Supprimer|Delete|Eliminar
Volume général|Master volume|Volumen general
Sortie audio|Audio output|Salida de audio
Microphones|Microphones|Micrófonos
Sorties|Outputs|Salidas
Mixeur|Mixer|Mezclador
Mixeur d’applications|Application mixer|Mezclador de aplicaciones
Chaque application, à son niveau|Each application at its own level|Cada aplicación a su nivel
Commandes rapides|Quick controls|Controles rápidos
Luminosité|Brightness|Brillo
Non pris en charge par cet écran|Not supported by this screen|No compatible con esta pantalla
Transparence du verre|Glass transparency|Transparencia del vidrio
Épaisseur des contours|Frame thickness|Grosor del marco
Zone de detection en haut|Top activation area|Zona de activación superior
Calibrer|Calibrate|Calibrar
Animations|Animations|Animaciones
Sans|Off|Desactivadas
Rapides|Fast|Rápidas
Taille des icônes|Icon size|Tamaño de los iconos
Langue|Language|Idioma
Langue de Windows|Windows language|Idioma de Windows
Choisir une ville|Choose a city|Elegir una ciudad
Ville automatique|Automatic city|Ciudad automática
Météo|Weather|Tiempo
Aucune application détectée|No applications detected|No se detectaron aplicaciones
Ouvrir une nouvelle fenêtre|Open new window|Abrir nueva ventana
Exécuter en administrateur|Run as administrator|Ejecutar como administrador
Épingler à la barre des tâches|Pin to taskbar|Anclar a la barra de tareas
Détacher de la barre des tâches|Unpin from taskbar|Desanclar de la barra de tareas
Ajouter aux favoris et au dock|Add to favorites and dock|Añadir a favoritos y al dock
Retirer des favoris|Remove from favorites|Quitar de favoritos
Afficher dans l’Explorateur|Show in Explorer|Mostrar en el Explorador
Réduire la fenêtre|Minimize window|Minimizar ventana
Agrandir la fenêtre|Maximize window|Maximizar ventana
Fermer cette fenêtre|Close this window|Cerrar esta ventana
Applications installées|Installed applications|Aplicaciones instaladas
Alimentation et veille|Power and sleep|Energía y suspensión
Observateur d’événements|Event Viewer|Visor de eventos
Informations système|System information|Información del sistema
Gestionnaire de périphériques|Device Manager|Administrador de dispositivos
Connexions réseau|Network connections|Conexiones de red
Gestion des disques|Disk Management|Administración de discos
Gestion de l’ordinateur|Computer Management|Administración de equipos
Gestionnaire des tâches|Task Manager|Administrador de tareas
Paramètres Windows|Windows settings|Configuración de Windows
Panneau de configuration|Control Panel|Panel de control
Explorateur de fichiers|File Explorer|Explorador de archivos
Afficher le bureau|Show desktop|Mostrar escritorio
Démarrer avec Windows|Start with Windows|Iniciar con Windows
Barre des tâches|Taskbar|Barra de tareas
Contour de l’écran|Screen frame|Marco de pantalla
No media|No media|Sin multimedia
Unknown title|Unknown title|Título desconocido
Unknown album|Unknown album|Álbum desconocido
Unknown artist|Unknown artist|Artista desconocido
No players|No players|Sin reproductores
Play some music for stuff to show up here!|Play music to see it here|Reproduce música para verla aquí
Search|Search|Buscar
Today|Today|Hoy
Weather|Weather|Tiempo
Performance|Performance|Rendimiento
Workspaces|Workspaces|Espacios de trabajo"""
ROWS += (
    "\n"
    + r"""Arrêter l’application|Stop application|Detener aplicación
Garder les messageries actives en arrière-plan (compteurs de messages, plus de mémoire)|Keep messaging apps active in the background (message counters, more memory)|Mantener las mensajerías activas en segundo plano (contadores de mensajes, más memoria)
Arrêter l’application…|Stop application…|Detener aplicación…
Barre des tâches Panneaux|Winlestia taskbar|Barra de tareas Winlestia
Cadre de l’écran|Screen frame|Marco de pantalla
Capture d’écran|Screenshot|Captura de pantalla
Choisir ma ville météo|Choose weather city|Elegir ciudad del tiempo
Choisir une icône|Choose an icon|Elegir un icono
Clique sur un élément pour le recopier|Click an item to copy it|Haz clic en un elemento para copiarlo
Des outils à portée de main|Tools at your fingertips|Herramientas a tu alcance
Exécuter / rechercher|Run / search|Ejecutar / buscar
Fermer toutes les fenêtres (|Close all windows (|Cerrar todas las ventanas (
Historique local · aucun envoi en ligne|Local history · never uploaded|Historial local · sin envíos
L’atelier|Tools|Herramientas
Personnaliser cet accès|Customize this shortcut|Personalizar este acceso
Renommer et catégoriser|Rename and categorize|Renombrar y categorizar
Tes fichiers restent à leur emplacement d’origine|Your files stay in their original location|Tus archivos permanecen en su ubicación original
À ta façon|Your way|A tu manera
Même verre partout. Un fin liseré garde les formes visibles, même très transparentes.|Shared glass throughout. A fine rim keeps the shapes visible.|Vidrio uniforme. Un borde fino mantiene las formas visibles.
Survole le bord inférieur pour afficher le dock. La touche Windows seule le révèle aussi. Les commandes restent ouvertes jusqu’au clic extérieur.|Hover at the bottom edge or press Windows to reveal the dock. Click outside to close controls.|Pasa el ratón por el borde inferior o pulsa Windows para mostrar el dock. Haz clic fuera para cerrar.
Réglages Windows en direct · volume conservé par Windows|Live Windows controls · Windows saves the volume|Controles de Windows · Windows guarda el volumen
Sélectionne, annote et partage ce qui compte.|Select, annotate and share what matters.|Selecciona, anota y comparte lo importante.
Ctrl + F3 · capture rapide d’une zone|Ctrl + F3 · quick region capture|Ctrl + F3 · captura rápida de una zona
↑ ↓ naviguer · Entrée ouvrir · Échap fermer|↑ ↓ navigate · Enter open · Esc close|↑ ↓ navegar · Intro abrir · Esc cerrar
> commandes · 24 × 7 calculer|> commands · 24 × 7 calculate|> comandos · 24 × 7 calcular
Terminal Winlestia — administrateur|Winlestia Terminal — administrator|Terminal Winlestia — administrador"""
)
ROWS += (
    "\n"
    + r"""Temp. GPU|GPU temp|Temp. GPU
Temp. CPU|CPU temp|Temp. CPU
Utilisation|Usage|Uso
Mémoire|Memory|Memoria
Stockage|Storage|Almacenamiento
N/D|N/A|N/D
en marche depuis %1|up %1|activo desde hace %1
Aucun média en lecture|No media playing|No se está reproduciendo nada
Fichiers|Files|Archivos
Tout|All|Todo
Écran|Screen|Pantalla
Audio|Audio|Audio
Confort|Comfort|Comodidad
Capturer et annoter|Capture and annotate|Capturar y anotar
Sélection, annotation et export|Selection, annotation and export|Selección, anotación y exportación
Texte d’une image|Text from an image|Texto de una imagen
Reconnaissance de texte Windows|Windows text recognition|Reconocimiento de texto de Windows
Pipette|Colour picker|Cuentagotas
Copier une couleur de l’écran|Copy a colour from the screen|Copiar un color de la pantalla
Règle pixel|Pixel ruler|Regla de píxeles
Mesurer une distance à l’écran|Measure a distance on screen|Medir una distancia en pantalla
Casque, enceintes et périphériques|Headphones, speakers and devices|Auriculares, altavoces y dispositivos
Entrée, niveau et bouton muet|Input, level and mute button|Entrada, nivel y botón de silencio
Un volume pour chaque application|A volume for each app|Un volumen para cada aplicación
Historique|History|Historial
Retrouver les éléments copiés|Find copied items again|Recuperar los elementos copiados
Toujours au premier plan|Always on top|Siempre visible
Garder la fenêtre active devant|Keep the active window in front|Mantener delante la ventana activa
Papier pense-bête|Sticky note|Nota adhesiva
Écrire et épingler sur le bureau|Write and pin to the desktop|Escribir y fijar en el escritorio
Notifications|Notifications|Notificaciones
Activer ou couper les notifications|Turn notifications on or off|Activar o desactivar las notificaciones
Gros fichiers|Large files|Archivos grandes
Comprendre ce qui remplit le disque|See what fills the disk|Ver qué llena el disco
Profils d’espace|Workspace profiles|Perfiles de espacio
Retrouver une disposition de travail|Restore a work layout|Recuperar una disposición de trabajo
Aide et raccourcis|Help and shortcuts|Ayuda y atajos
Les gestes et touches de Panneaux|Winlestia gestures and keys|Gestos y teclas de Winlestia
Corbeille|Recycle Bin|Papelera
Déposer pour supprimer|Drop to delete|Soltar para eliminar
Copier les fichiers|Copy the files|Copiar los archivos
Compresser|Compress|Comprimir
Créer une archive ZIP|Create a ZIP archive|Crear un archivo ZIP
Lever du soleil|Sunrise|Amanecer
Coucher du soleil|Sunset|Atardecer
Humidité|Humidity|Humedad
Ressenti|Feels Like|Sensación térmica
Vent|Wind|Viento"""
)
ROWS += (
    "\n"
    + r"""Nouvelle sélection|New selection|Nueva selección
Crayon|Pen|Lápiz
Surligneur|Highlighter|Resaltador
Changer de couleur|Change colour|Cambiar de color
Annuler le dernier trait · Ctrl Z|Undo last stroke · Ctrl Z|Deshacer el último trazo · Ctrl Z
Extraire le texte|Extract text|Extraer texto
Épingler sur le bureau|Pin to desktop|Fijar en el escritorio
Enregistrer en PNG · Ctrl S|Save as PNG · Ctrl S|Guardar como PNG · Ctrl S
Fermer · Échap|Close · Esc|Cerrar · Esc
Glisse pour sélectionner|Drag to select|Arrastra para seleccionar
Sélectionner une zone|Select an area|Seleccionar una zona
Extraire du texte|Extract text from screen|Extraer texto de la pantalla
Copier cet écran|Copy this screen|Copiar esta pantalla
Dessiner, surligner, copier, enregistrer ou épingler|Draw, highlight, copy, save or pin|Dibujar, resaltar, copiar, guardar o fijar
Le texte reconnu rejoint le presse-papiers|Recognised text goes to the clipboard|El texto reconocido pasa al portapapeles
Une image complète de l’écran actuel|A full image of the current screen|Una imagen completa de la pantalla actual
Rechercher dans l’historique|Search history|Buscar en el historial
Aucun enregistrement · réglage du niveau d’entrée uniquement|No recording · input level adjustment only|Sin grabación · solo ajuste del nivel de entrada
Le changement s’applique aux nouvelles lectures Windows|The change applies to new Windows playback|El cambio se aplica a las nuevas reproducciones de Windows
Aucun fond d’écran trouvé|No wallpapers found|No se encontraron fondos de pantalla
Ajoute des images dans ton dossier Images|Try putting some images in your Pictures folder|Añade imágenes en tu carpeta Imágenes
Aucun emoji trouvé|No emoji found|No se encontró ningún emoji
Index Windows indisponible|Windows index unavailable|Índice de Windows no disponible
Le service Windows Search est arrêté ou désactivé|The Windows Search service is stopped or disabled|El servicio Windows Search está detenido o desactivado
La recherche a pris trop de temps|The search took too long|La búsqueda tardó demasiado
Tape au moins 2 caractères|Type at least 2 characters|Escribe al menos 2 caracteres
Aucun fichier trouvé|No file found|No se encontró ningún archivo
Recherche…|Searching…|Buscando…
Rechercher un réglage|Search settings|Buscar un ajuste
Aucun réglage trouvé|No setting found|No se encontró ningún ajuste
Essaie un autre mot|Try another word|Prueba con otra palabra
Effets allégés|Reduced effects|Efectos reducidos"""
)
ROWS += (
    "\n"
    + r"""Le Wi-Fi est désactivé.|Wi-Fi is turned off.|El Wi-Fi está desactivado.
Aucune carte Wi-Fi détectée sur cet ordinateur.|No Wi-Fi adapter found on this computer.|No se detectó ningún adaptador Wi-Fi en este equipo.
Le service Wi-Fi de Windows est arrêté.|The Windows Wi-Fi service is stopped.|El servicio Wi-Fi de Windows está detenido.
Windows demande l’autorisation de localisation pour afficher les réseaux Wi-Fi.|Windows needs location permission to show Wi-Fi networks.|Windows necesita permiso de ubicación para mostrar las redes Wi-Fi.
Aucun réseau trouvé pour le moment.|No network found for now.|No se encontró ninguna red por ahora.
La liste des réseaux est indisponible pour le moment.|The network list is unavailable for now.|La lista de redes no está disponible por ahora.
Recherche des réseaux…|Looking for networks…|Buscando redes…
Connexion impossible. Vérifiez le mot de passe.|Could not connect. Check the password.|No se pudo conectar. Comprueba la contraseña.
Le réseau ne répond pas. Réessayez.|The network is not responding. Try again.|La red no responde. Inténtalo de nuevo.
Ce réseau n’est plus à portée.|This network is no longer in range.|Esta red ya no está al alcance.
Windows a refusé l’opération.|Windows refused the operation.|Windows rechazó la operación.
Ce type de réseau n’est pas pris en charge ici.|This network type is not supported here.|Este tipo de red no es compatible aquí.
Le mot de passe doit contenir au moins 8 caractères.|The password must have at least 8 characters.|La contraseña debe tener al menos 8 caracteres.
Le mot de passe doit contenir 63 caractères au plus.|The password must have 63 characters at most.|La contraseña debe tener 63 caracteres como máximo.
Le mot de passe contient des caractères non pris en charge.|The password contains unsupported characters.|La contraseña contiene caracteres no compatibles.
La connexion a échoué.|The connection failed.|La conexión falló.
Réseaux Wi-Fi|Wi-Fi networks|Redes Wi-Fi
Choisissez un réseau pour vous connecter|Choose a network to connect|Elige una red para conectarte
Ce réseau demande une page de connexion.|This network needs a sign-in page.|Esta red requiere una página de inicio de sesión.
Ouvrir la page|Open the page|Abrir la página
Activer le Wi-Fi|Turn on Wi-Fi|Activar el Wi-Fi
Ouvrir les réglages de localisation|Open location settings|Abrir ajustes de ubicación
Connecté|Connected|Conectado
Mémorisé|Remembered|Recordada
Réseau d’entreprise|Enterprise network|Red empresarial
Non pris en charge|Not supported|No compatible
Mot de passe|Password|Contraseña
Ce réseau demande une authentification d’entreprise (802.1X). Connectez-vous depuis Windows.|This network needs enterprise (802.1X) sign-in. Connect from Windows.|Esta red requiere autenticación empresarial (802.1X). Conéctate desde Windows.
Connexion…|Connecting…|Conectando…
Ouvrir dans Windows|Open in Windows|Abrir en Windows
Oublier|Forget|Olvidar
Se connecter|Connect|Conectar"""
)
TABLE = {}
for row in ROWS.splitlines():
    fr, en, es = row.split("|")
    value = {"fr": fr, "en": en, "es": es}
    TABLE[fr] = value
    TABLE.setdefault(en, value)


def windows_language():
    try:
        buffer = ctypes.create_unicode_buffer(85)
        ctypes.windll.kernel32.GetUserDefaultUILanguage.restype = ctypes.c_ushort
        ident = ctypes.windll.kernel32.GetUserDefaultUILanguage()
        primary = ident & 0x3FF
        return {12: "fr", 9: "en", 10: "es"}.get(primary, "en")
    except Exception:
        return "en"


class Translator(QTranslator):
    def __init__(self, config):
        super().__init__()
        self.path = Path(config) / "language.json"
        try:
            self.mode = json.loads(self.path.read_text(encoding="utf-8"))["language"]
        except (OSError, ValueError, KeyError):
            self.mode = "auto"

    @property
    def language(self):
        return windows_language() if self.mode == "auto" else self.mode

    def isEmpty(self):
        return False

    def translate(self, context, source, disambiguation=None, n=-1):
        return TABLE.get(source, {}).get(self.language, source)

    def select(self, mode):
        self.mode = mode if mode in ("auto", "fr", "en", "es") else "auto"
        atomic_write_text(self.path, json.dumps({"language": self.mode}))


for row in """Un espace qui te ressemble|A space of your own|Un espacio a tu medida
Couleurs et transparence|Colours and transparency|Colores y transparencia
Bureau|Desktop|Escritorio
Cadre, dock et accès rapide|Frame, dock and quick access|Marco, dock y acceso rápido
Zone de détection|Activation area|Zona de activación
Ajuster avec le repère à l’écran|Adjust using the on-screen guide|Ajustar con la guía en pantalla
Langue et raccourcis|Language and shortcuts|Idioma y atajos
Un rendu plus léger pour les petites configurations|Lighter rendering for modest hardware|Renderizado ligero para equipos modestos
Afficher l’OSD|Show the OSD|Mostrar el OSD
Masquer le dock et l’OSD en plein écran|Hide the dock and OSD in fullscreen|Ocultar el dock y el OSD en pantalla completa
L’OSD, le visualiseur et les animations sont coupés sur tous les écrans dès qu’un écran est en plein écran|The OSD, visualizer and animations are stopped on all screens as soon as one screen is fullscreen|El OSD, el visualizador y las animaciones se detienen en todas las pantallas en cuanto una está a pantalla completa
Indicateur de bureaux virtuels|Virtual desktop indicator|Indicador de escritorios virtuales
Une pastille par bureau Windows dans le dock|One dot per Windows desktop in the dock|Un punto por escritorio de Windows en el dock
Bureaux virtuels|Virtual desktops|Escritorios virtuales
Bureau %1|Desktop %1|Escritorio %1
Nouveau bureau|New desktop|Nuevo escritorio
Organisation automatique : activée|Automatic tiling: on|Organización automática: activada
Organisation automatique : désactivée|Automatic tiling: off|Organización automática: desactivada
Ctrl+Alt+flèches : changer de fenêtre|Ctrl+Alt+arrows: move focus|Ctrl+Alt+flechas: mover el foco
Afficher plus|Show more|Mostrar más
Ranger|Hide|Ocultar
Fermer le bureau|Close desktop|Cerrar escritorio
Les fenêtres passent sur le bureau voisin|Windows move to the neighbouring desktop|Las ventanas pasan al escritorio vecino
Indicateur de volume, luminosité et micro à l’écran|On-screen volume, brightness and microphone indicator|Indicador en pantalla de volumen, brillo y micrófono
Sélecteur de fonds d’écran|Wallpaper switcher|Selector de fondos de pantalla
Choisis un fond en plein écran avec aperçu|Pick a wallpaper full screen with a live preview|Elige un fondo a pantalla completa con vista previa
Aucun fond d’écran trouvé|No wallpaper found|No se encontró ningún fondo
Ajouter des images|Add images|Añadir imágenes
Ouvrir le dossier|Open folder|Abrir carpeta
Masquer ce fond par défaut ? Entrée pour confirmer · Échap annuler|Hide this default wallpaper? Enter to confirm · Esc cancel|¿Ocultar este fondo predeterminado? Intro para confirmar · Esc cancelar
Les images du dossier Images ne sont pas supprimées ici|Images from the Pictures folder are not deleted here|Las imágenes de la carpeta Imágenes no se eliminan aquí
Restaurer les fonds par défaut|Restore default wallpapers|Restaurar los fondos predeterminados
Tous les écrans|All displays|Todas las pantallas
Écran %1 · %2×%3|Display %1 · %2×%3|Pantalla %1 · %2×%3
Écran %1 (principal) · %2×%3|Display %1 (primary) · %2×%3|Pantalla %1 (principal) · %2×%3
%1 masque le fond choisi tant qu’il tourne|%1 hides the chosen wallpaper while it is running|%1 oculta el fondo elegido mientras se ejecuta
Tab changer d’écran|Tab switch display|Tab cambiar de pantalla
Supprimer ce fond de votre dossier ? Entrée confirmer · Échap annuler|Delete this wallpaper from your folder? Enter confirm · Esc cancel|¿Eliminar este fondo de tu carpeta? Intro confirmar · Esc cancelar
← → choisir · Entrée appliquer · Ctrl+O ajouter · Suppr retirer · Échap fermer|← → choose · Enter apply · Ctrl+O add · Del remove · Esc close|← → elegir · Intro aplicar · Ctrl+O añadir · Supr quitar · Esc cerrar
Organisation automatique|Automatic tiling|Organización automática
Les fenêtres se partagent chaque écran en tuiles, dans un ordre stable, avec des raccourcis clavier|Windows share each screen as tiles, in a stable order, with keyboard shortcuts|Las ventanas se reparten cada pantalla en mosaicos, en un orden estable, con atajos de teclado
Raccourcis du tuilage|Tiling shortcuts|Atajos del mosaico
Actifs seulement quand l’organisation automatique est activée|Active only while automatic tiling is on|Activos solo cuando la organización automática está activada
Passer à la tuile voisine|Move to the neighbouring tile|Pasar al mosaico vecino
Échanger avec la tuile voisine, y compris sur l’écran d’à côté|Swap with the neighbouring tile, including on the next screen|Intercambiar con el mosaico vecino, también en la pantalla contigua
Sortir la fenêtre du tuilage, ou l’y remettre|Float the window out of the tiling, or put it back|Sacar la ventana del mosaico, o volver a ponerla
Agrandir ou réduire la tuile active (20 à 80 %)|Grow or shrink the active tile (20 to 80 %)|Agrandar o reducir el mosaico activo (20 a 80 %)
Exclure l’application de la fenêtre active, ou la réintégrer|Exclude the active window's application, or include it again|Excluir la aplicación de la ventana activa, o reincorporarla
Souris|Mouse|Ratón
Lâcher une fenêtre sur une autre tuile les échange|Dropping a window on another tile swaps them|Soltar una ventana sobre otro mosaico las intercambia
Applications exclues|Excluded applications|Aplicaciones excluidas
Aucune application exclue|No excluded application|Ninguna aplicación excluida
Seulement la fenêtre active|Active window only|Solo la ventana activa
Le liseré suit la fenêtre au premier plan|The border follows the foreground window|El borde sigue a la ventana en primer plano
Aucun résultat|No results|Sin resultados
Recherche des périphériques…|Searching for devices…|Buscando dispositivos…
Aucun microphone connecté|No microphone connected|Ningún micrófono conectado
Aucune sortie audio détectée|No audio output detected|No se detectó ninguna salida de audio
Recherche des sessions audio…|Searching for audio sessions…|Buscando sesiones de audio…
Aucune application ne joue de son|No application is playing sound|Ninguna aplicación reproduce sonido
Lance une musique, une vidéo ou un jeu.|Start some music, a video or a game.|Inicia música, un vídeo o un juego.
Aucun élément dans cet historique|No item in this history|Ningún elemento en este historial
L’historique est désactivé|History is turned off|El historial está desactivado
Active-le dans les paramètres pour retrouver ce que tu copies.|Turn it on in settings to find what you copy.|Actívalo en los ajustes para recuperar lo que copias.
Les modifications non enregistrées seront perdues.|Unsaved changes will be lost.|Se perderán los cambios sin guardar.
Ta session sera verrouillée.|Your session will be locked.|Tu sesión se bloqueará.
Ses commandes audio apparaîtront ici.|Its audio controls will appear here.|Sus controles de audio aparecerán aquí.
%1 ?|%1?|¿%1?""".splitlines():
    fr, en, es = row.split("|")
    TABLE[fr] = {"fr": fr, "en": en, "es": es}
for row in """Bureau et dock|Desktop and dock|Escritorio y dock
Explorateur et fichiers|Explorer and files|Explorador y archivos
Au quotidien|Everyday|Día a día
Réseaux sociaux|Social networks|Redes sociales
Favoris|Favorites|Favoritos
Rétablir la barre Windows|Restore the Windows taskbar|Restablecer la barra de Windows
Les couleurs de tout le shell|Colours for the whole shell|Los colores de todo el shell
Porcelaine|Porcelain|Porcelana
Nuit|Night|Noche
Personnalisé|Custom|Personalizado
Couleurs tirées de ton fond d’écran|Colours taken from your wallpaper|Colores tomados de tu fondo de pantalla
Porcelaine : thème clair et chaleureux, fixe, indépendant du fond d’écran.|Porcelain: a bright, warm theme, fixed and independent of the wallpaper.|Porcelana: tema claro y cálido, fijo e independiente del fondo de pantalla.
Nuit : thème sombre et reposant, fixe, indépendant du fond d’écran.|Night: a dark, restful theme, fixed and independent of the wallpaper.|Noche: tema oscuro y relajante, fijo e independiente del fondo de pantalla.
Personnalisé : tes propres couleurs, avec option de verre transparent.|Custom: your own colours, with an optional transparent glass.|Personalizado: tus propios colores, con vidrio transparente opcional.
Automatique : les couleurs suivent ton fond d’écran, la transparence du verre est réglable.|Automatic: colours follow your wallpaper, and glass transparency is adjustable.|Automático: los colores siguen tu fondo de pantalla y la transparencia del vidrio es ajustable.
Modifier ma palette|Edit my palette|Editar mi paleta
Liseré sur les applications|Rim on applications|Borde en las aplicaciones
La couleur du thème autour des fenêtres du Bureau|Theme colour around Desktop windows|El color del tema alrededor de las ventanas del Escritorio
Rendu|Rendering|Renderizado
Transparence, animations et liseré|Transparency, animations and rim|Transparencia, animaciones y borde
Plus la valeur est haute, plus le fond transparaît|The higher the value, the more the background shows through|Cuanto más alto el valor, más se transparenta el fondo
Réglable avec le thème Automatique|Adjustable with the Automatic theme|Ajustable con el tema Automático
Vitesse des transitions du shell|Shell transition speed|Velocidad de las transiciones del shell
Rendu plus léger pour les petites configurations|Lighter rendering for modest hardware|Renderizado más ligero para equipos modestos
Fenêtres et écran|Windows and screen|Ventanas y pantalla
Organisation et cadre autour du Bureau|Layout and frame around the Desktop|Organización y marco alrededor del Escritorio
Logo Winlestia|Winlestia logo|Logotipo de Winlestia
Un repère discret dans le coin de l’écran|A discreet mark in the screen corner|Una marca discreta en la esquina de la pantalla
Un contour arrondi coloré autour du Bureau|A rounded coloured outline around the Desktop|Un contorno redondeado de color alrededor del Escritorio
Largeur du cadre de l’écran|Screen frame width|Anchura del marco de la pantalla
Dock et accès rapide|Dock and quick access|Dock y acceso rápido
Barre des tâches et zone du haut de l’écran|Taskbar and top-of-screen area|Barra de tareas y zona superior de la pantalla
Barre des tâches Winlestia|Winlestia taskbar|Barra de tareas Winlestia
Remplace la barre Windows par le dock|Replaces the Windows taskbar with the dock|Sustituye la barra de Windows por el dock
Icônes du dock et de la barre des tâches|Dock and taskbar icons|Iconos del dock y de la barra de tareas
bande en haut de l’écran qui ouvre le panneau|strip at the top of the screen that opens the panel|franja en la parte superior de la pantalla que abre el panel
Explorateur Windows|Windows Explorer|Explorador de Windows
Les dossiers prennent tes couleurs|Folders take your colours|Las carpetas adoptan tus colores
Icônes et contours assortis|Matching icons and outlines|Iconos y contornos a juego
Dossiers personnels et sous-dossiers à tes couleurs (aperçu ci-dessus)|Personal folders and subfolders in your colours (preview above)|Carpetas personales y subcarpetas con tus colores (vista previa arriba)
Icônes locales adaptatives. Le contenu et les menus restent ceux de Windows ; leur transparence ne change pas.|Adaptive local icons. Content and menus stay Windows' own; their transparency does not change.|Iconos locales adaptables. El contenido y los menús siguen siendo los de Windows; su transparencia no cambia.
Prêt à personnaliser|Ready to customize|Listo para personalizar
Ajouter un dossier|Add a folder|Añadir una carpeta
Ouvrir l’Explorateur|Open Explorer|Abrir el Explorador
Restaurer|Restore|Restaurar
Winlestia Fichiers|Winlestia Files|Winlestia Archivos
Le gestionnaire de fichiers du shell|The shell's file manager|El administrador de archivos del shell
Ouvrir les dossiers avec Winlestia Fichiers|Open folders with Winlestia Files|Abrir las carpetas con Winlestia Archivos
Explorer reste disponible ; désactivable à tout moment|Explorer stays available; can be turned off at any time|El Explorador sigue disponible; se puede desactivar en cualquier momento
Précharger Winlestia Fichiers|Preload Winlestia Files|Precargar Winlestia Archivos
Garde une instance cachée (~70 Mo de mémoire) pour ouvrir les dossiers plus vite ; l’arrêt prend effet au prochain démarrage|Keeps a hidden instance (~70 MB of memory) to open folders faster; turning it off takes effect at next start|Mantiene una instancia oculta (~70 MB de memoria) para abrir las carpetas más rápido; al desactivarlo se aplica en el próximo inicio
Raccourcis|Shortcuts|Atajos
Clique sur une touche pour la changer|Click a key to change it|Haz clic en una tecla para cambiarla
Ouvrir le panneau|Open the panel|Abrir el panel
Affiche ou masque le tableau de bord|Shows or hides the dashboard|Muestra u oculta el panel
Recherche et lance tout depuis un champ|Search and launch anything from one field|Busca y lanza todo desde un campo
Dictée vocale|Voice dictation|Dictado por voz
Appuyer pour parler, le texte est saisi là où tu écris|Press to talk, the text is typed where you write|Pulsa para hablar, el texto se escribe donde escribes
Activer la dictée|Turn on dictation|Activar el dictado
Transcription locale, sans envoyer ta voix sur Internet|Local transcription, your voice never leaves this PC|Transcripción local, sin enviar tu voz a Internet
Touche de dictée|Dictation key|Tecla de dictado
Maintenez la touche pour dicter (français, anglais, espagnol, détection auto)|Hold the key to dictate (French, English, Spanish, auto-detect)|Mantén la tecla para dictar (francés, inglés, español, detección automática)
Démarrage et données|Startup and data|Inicio y datos
Lancement et historique|Launch and history|Inicio e historial
Winlestia s’ouvre à ta connexion|Winlestia opens when you sign in|Winlestia se abre al iniciar sesión
Historique du presse-papiers|Clipboard history|Historial del portapapeles
Garde les éléments copiés pour les recoller|Keeps copied items so you can paste them again|Guarda los elementos copiados para volver a pegarlos
Langue et services|Language and services|Idioma y servicios
Langue de l’interface, météo et applications|Interface language, weather and applications|Idioma de la interfaz, tiempo y aplicaciones
Langue de l’interface de Winlestia|Winlestia interface language|Idioma de la interfaz de Winlestia
Ville de la météo|Weather city|Ciudad del tiempo
Pour la météo du tableau de bord|For the dashboard weather|Para el tiempo del panel
Liste des applications|Application list|Lista de aplicaciones
Relit les applications installées|Rescans installed applications|Vuelve a leer las aplicaciones instaladas
Actualiser|Refresh|Actualizar
Le panneau caché au bord droit de l’écran|The panel hidden at the right edge of the screen|El panel oculto en el borde derecho de la pantalla
Le panneau caché au bord gauche de l’écran|The panel hidden at the left edge of the screen|El panel oculto en el borde izquierdo de la pantalla
Chargement…|Loading…|Cargando…
Non connecté|Not connected|No conectado
Afficher|Show|Mostrar
Ouvrir|Open|Abrir
Muet|Mute|Silenciar
Déconnecter|Disconnect|Desconectar
La connexion se fait toi-même dans la fenêtre du service. Snapchat Web peut être limité.|You sign in yourself in the service window. Snapchat Web may be limited.|Inicias sesión tú mismo en la ventana del servicio. Snapchat Web puede estar limitado.
Son et sourdine|Sound and mute|Sonido y silencio
Couper toutes les notifications|Turn off all notifications|Silenciar todas las notificaciones
Plus de son ; les compteurs continuent|No more sound; counters keep running|Sin sonido; los contadores siguen funcionando
Son de notification|Notification sound|Sonido de notificación
Joue un son à chaque nouveau message|Plays a sound for each new message|Reproduce un sonido con cada mensaje nuevo
Volume|Volume|Volumen
Volume du son de notification|Notification sound volume|Volumen del sonido de notificación
Tester|Test|Probar
 (navigateur par défaut)| (default browser)| (navegador predeterminado)
Utilisé|In use|En uso
Utiliser|Use|Usar
Aucun navigateur pris en charge n’a été détecté.|No supported browser was detected.|No se detectó ningún navegador compatible.
Réafficher les favoris retirés (%1)|Show removed favorites again (%1)|Volver a mostrar los favoritos quitados (%1)
Ceux retirés de Winlestia uniquement, pas du navigateur|Only those removed from Winlestia, not from the browser|Solo los quitados de Winlestia, no del navegador
Réafficher|Show again|Volver a mostrar
Choisir|Choose|Elegir
le navigateur|the browser|el navegador
Fermez %1 pour supprimer un favori.|Close %1 to delete a favorite.|Cierra %1 para eliminar un favorito.
Suppression non prise en charge pour ce navigateur.|Deletion is not supported for this browser.|La eliminación no es compatible con este navegador.
Favori introuvable.|Favorite not found.|Favorito no encontrado.
Chargement des favoris…|Loading favorites…|Cargando favoritos…
Aucun favori|No favorites|Sin favoritos
Retirer de Winlestia|Remove from Winlestia|Quitar de Winlestia
Supprimer dans %1…|Delete in %1…|Eliminar en %1…
La synchronisation de %1 est active : la suppression sera répliquée sur vos autres appareils.|%1 sync is on: the deletion will be replicated to your other devices.|La sincronización de %1 está activa: la eliminación se replicará en tus otros dispositivos.
Supprimer ce favori dans %1 ? Une sauvegarde du fichier est faite avant. %1 doit être fermé.|Delete this favorite in %1? The file is backed up first. %1 must be closed.|¿Eliminar este favorito en %1? Antes se hace una copia de seguridad del archivo. %1 debe estar cerrado.
Supprimer quand même|Delete anyway|Eliminar de todos modos
Copier|Copy|Copiar
Fond|Background|Fondo
Texte|Text|Texto
Principal|Primary|Principal
Secondaire|Secondary|Secundario
Troisième accent|Third accent|Tercer acento
Ma palette|My palette|Mi paleta
Cinq couleurs, tout ton espace|Five colours, your whole space|Cinco colores, todo tu espacio
Ton espace|Your space|Tu espacio
Verre transparent|Transparent glass|Vidrio transparente
Appliquer ma palette|Apply my palette|Aplicar mi paleta
Échap · annuler|Esc · cancel|Esc · cancelar
Appuie sur une touche…|Press a key…|Pulsa una tecla…
Appuyez sur une touche…|Press a key…|Pulsa una tecla…
(Échap pour annuler)|(Esc to cancel)|(Esc para cancelar)
Transcription|Transcription|Transcripción
Préparation du modèle…|Preparing the model…|Preparando el modelo…
Dictée impossible|Dictation unavailable|Dictado no disponible
Terminal Winlestia|Winlestia Terminal|Terminal Winlestia
Fermer Winlestia|Close Winlestia|Cerrar Winlestia
Arrêter cette application ?|Stop this application?|¿Detener esta aplicación?
« |“|“
 » sera arrêté. Les modifications non enregistrées seront perdues.|” will be stopped. Unsaved changes will be lost.|” se detendrá. Se perderán los cambios sin guardar.
Appuie sur le nouveau raccourci|Press the new shortcut|Pulsa el nuevo atajo
Ctrl + une touche|Ctrl + a key|Ctrl + una tecla
Le raccourci est enregistré automatiquement.|The shortcut is saved automatically.|El atajo se guarda automáticamente.
Échap pour annuler|Esc to cancel|Esc para cancelar
à l’instant|Just now|Ahora mismo
il y a %1 min|%1 min ago|hace %1 min
Aucune notification depuis l’ouverture de session|No notifications since you signed in|Ninguna notificación desde que iniciaste sesión
Capture|Capture|Captura
Barre Windows|Windows taskbar|Barra de Windows
Quitter Panneaux|Quit Winlestia|Salir de Winlestia
Autres|Other|Otros
Dossiers et projets|Folders and projects|Carpetas y proyectos
Outils Windows|Windows tools|Herramientas de Windows
Ajouter aux favoris|Add to favorites|Añadir a favoritos
Ouvrir l’emplacement|Open location|Abrir ubicación
Ranger dans Applications|Move to Applications|Mover a Aplicaciones
Ranger dans Jeux|Move to Games|Mover a Juegos
Ranger dans Projets|Move to Projects|Mover a Proyectos
Ranger dans Documents|Move to Documents|Mover a Documentos
accès · Clique pour ouvrir, clic droit pour organiser|shortcuts · Click to open, right-click to organise|accesos · Haz clic para abrir, clic derecho para organizar
Aucun accès ne correspond à cette recherche|No shortcut matches this search|Ningún acceso coincide con esta búsqueda
Cet espace est prêt à accueillir tes accès|This space is ready for your shortcuts|Este espacio está listo para tus accesos
Ajoute une application, un dossier ou un document pour le retrouver ici.|Add an application, folder or document to find it here.|Añade una aplicación, una carpeta o un documento para encontrarlo aquí.
Effacer la recherche|Clear search|Borrar búsqueda
Ajouter un accès|Add a shortcut|Añadir un acceso
Bonne nuit|Good night|Buenas noches
Bonjour|Good morning|Buenos días
Bon après-midi|Good afternoon|Buenas tardes
Bonsoir|Good evening|Buenas noches
accès|shortcuts|accesos
Ajoute des favoris depuis le clic droit d’un accès|Add favorites from a shortcut's right-click menu|Añade favoritos desde el clic derecho de un acceso
Ajouter|Add|Añadir
Voir|View|Ver
Tes derniers accès apparaîtront ici|Your latest shortcuts will appear here|Tus últimos accesos aparecerán aquí""".splitlines():
    fr, en, es = row.split("|")
    TABLE[fr] = {"fr": fr, "en": en, "es": es}
for row in """Choisis ton entrée et règle sa sensibilité|Pick your input and adjust its sensitivity|Elige tu entrada y ajusta su sensibilidad
Choisis où écouter tes applications|Choose where to hear your applications|Elige dónde escuchar tus aplicaciones
Périphérique par défaut|Default device|Dispositivo predeterminado
Cliquer pour utiliser|Click to use|Haz clic para usar
Actuel|Current|Actual
Image copiée|Copied image|Imagen copiada
Application|Application|Aplicación
En arrière-plan|In the background|En segundo plano
%1 fenêtre(s)|%1 window(s)|%1 ventana(s)
Documents & médias|Documents & media|Documentos y multimedia
Récemment ouverts|Recently opened|Abiertos recientemente
Tout ton espace|Your whole space|Todo tu espacio
Rechercher dans %1  (Ctrl+F)|Search in %1  (Ctrl+F)|Buscar en %1  (Ctrl+F)
Nom|Name|Nombre
Catégorie|Category|Categoría
Météo indisponible · réessayer|Weather unavailable · try again|Tiempo no disponible · reintentar
Entrée pour copier le résultat|Press Enter to copy the result|Intro para copiar el resultado
Fenêtre|Window|Ventana
Onglet rapide|Quick tab|Pestaña rápida
Site internet|Website|Sitio web
Rechercher « %1 »|Search « %1 »|Buscar « %1 »
Ouvrir dans le navigateur|Open in browser|Abrir en el navegador
Horloge, calendrier et lecture|Clock, calendar and playback|Reloj, calendario y reproducción
Commandes Windows|Windows controls|Controles de Windows
Volume, écran et connexions|Volume, display and connections|Volumen, pantalla y conexiones
Températures et activité du PC|PC temperatures and activity|Temperaturas y actividad del PC
Tous tes logiciels|All your software|Todo tu software
Ta bibliothèque de jeux|Your game library|Tu biblioteca de juegos
Reprendre tes projets|Resume your projects|Retomar tus proyectos
Dossiers et médias|Folders and media|Carpetas y multimedia
Retrouver un élément copié|Find a copied item|Encontrar un elemento copiado
Apparence et comportement|Appearance and behaviour|Apariencia y comportamiento
Capture annotée|Annotated capture|Captura anotada
Encadrer une zone de l’écran|Frame an area of the screen|Enmarcar una zona de la pantalla
Note rapide|Quick note|Nota rápida
Bloc-notes persistant|Persistent notepad|Bloc de notas persistente
Une session de concentration|A focus session|Una sesión de concentración
Son et micro|Sound and microphone|Sonido y micrófono
Périphériques et applications|Devices and applications|Dispositivos y aplicaciones
Retrouver une session de travail|Resume a work session|Retomar una sesión de trabajo
Onglets rapides|Quick tabs|Pestañas rápidas
Personnaliser les raccourcis de cette commande|Customize this command's shortcuts|Personalizar los atajos de este comando
Les outils du quotidien|Everyday tools|Las herramientas del día a día
Processus et démarrage|Processes and startup|Procesos e inicio
Calculatrice Windows|Windows Calculator|Calculadora de Windows
Ouvrir la calculatrice|Open the calculator|Abrir la calculadora
Fond d’écran|Wallpaper|Fondo de pantalla
Wallpaper · changer le fond du bureau|Wallpaper · change the desktop background|Wallpaper · cambiar el fondo del escritorio
Emojis|Emojis|Emojis
Emoji · chercher et copier un emoji|Emoji · find and copy an emoji|Emoji · buscar y copiar un emoji
File · chercher dans l’index Windows|File · search the Windows index|File · buscar en el índice de Windows
Shutdown · éteindre le PC (confirmation demandée)|Shutdown · turn off the PC (confirmation required)|Shutdown · apagar el PC (se pide confirmación)
Reboot · redémarrer le PC (confirmation demandée)|Reboot · restart the PC (confirmation required)|Reboot · reiniciar el PC (se pide confirmación)
Logout · fermer la session (confirmation demandée)|Logout · sign out (confirmation required)|Logout · cerrar la sesión (se pide confirmación)
Lock · verrouiller la session (confirmation demandée)|Lock · lock the session (confirmation required)|Lock · bloquear la sesión (se pide confirmación)
Sleep · mise en veille (confirmation demandée)|Sleep · put the PC to sleep (confirmation required)|Sleep · suspender el PC (se pide confirmación)
Hibernate · hiberner le PC (confirmation demandée)|Hibernate · hibernate the PC (confirmation required)|Hibernate · hibernar el PC (se pide confirmación)
Aucune capture disponible.|No capture available.|No hay ninguna captura disponible.
Sélectionne une zone plus grande.|Select a larger area.|Selecciona una zona más grande.
Le panneau ne s’est pas masqué. Relance la capture.|The panel did not hide. Start the capture again.|El panel no se ocultó. Vuelve a iniciar la captura.
Windows ne permet pas cette capture.|Windows does not allow this capture.|Windows no permite esta captura.
Impossible d’enregistrer la capture.|Could not save the capture.|No se pudo guardar la captura.
Impossible de préparer la capture.|Could not prepare the capture.|No se pudo preparar la captura.
Le fond d’écran n’a pas pu être appliqué.|The wallpaper could not be applied.|No se pudo aplicar el fondo de pantalla.
Fond d’écran par écran indisponible pour le moment.|Per-screen wallpaper is unavailable for now.|El fondo por pantalla no está disponible por ahora.
Fichiers copiés dans le presse-papiers.|Files copied to the clipboard.|Archivos copiados al portapapeles.
Archive ZIP créée.|ZIP archive created.|Archivo ZIP creado.
Impossible d’ouvrir ce fichier.|Could not open this file.|No se pudo abrir este archivo.
Raccourci enregistré|Shortcut saved|Atajo guardado
Choisis Ctrl, Alt ou Windows avec une autre touche.|Choose Ctrl, Alt or Windows together with another key.|Elige Ctrl, Alt o Windows junto con otra tecla.
Capture épinglée|Capture pinned|Captura fijada
Texte copié dans le presse-papiers|Text copied to the clipboard|Texto copiado al portapapeles
Maj gauche|Left Shift|Mayús izquierda
Maj droite|Right Shift|Mayús derecha
Ctrl gauche|Left Ctrl|Ctrl izquierda
Ctrl droite|Right Ctrl|Ctrl derecha
Alt gauche|Left Alt|Alt izquierda
Alt droit|Right Alt|Alt derecha
Windows gauche|Left Windows|Windows izquierda
Windows droite|Right Windows|Windows derecha
Verr. Maj|Caps Lock|Bloq Mayús
Arrêt défil.|Scroll Lock|Bloq Despl
Menu contextuel|Context menu|Menú contextual
Espace|Space|Espacio
Retour arrière|Backspace|Retroceso
Entrée|Enter|Intro
Inser|Insert|Insertar
Suppr|Delete|Supr
Début|Home|Inicio
Fin|End|Fin
Page préc.|Page Up|Re Pág
Page suiv.|Page Down|Av Pág
Enregistrer la capture|Save the capture|Guardar la captura
Ajouter des images|Add images|Añadir imágenes
Icône de cet accès|Icon for this shortcut|Icono de este acceso
Photo de profil|Profile picture|Foto de perfil
Ville, pays|City, country|Ciudad, país
Météo · Open-Meteo|Weather · Open-Meteo|Tiempo · Open-Meteo""".splitlines():
    fr, en, es = row.split("|")
    TABLE.setdefault(fr, {"fr": fr, "en": en, "es": es})
# Media player source names and audio-session messages raised by the backend.
for row in """Navigateur|Browser|Navegador
Lecteur Windows|Windows player|Reproductor de Windows
Aucune session audio pour ce lecteur.|No audio session for this player.|Ninguna sesión de audio para este reproductor.
Cette application n’a plus de session audio. Relance sa lecture.|This application no longer has an audio session. Restart its playback.|Esta aplicación ya no tiene sesión de audio. Reinicia su reproducción.""".splitlines():
    fr, en, es = row.split("|")
    TABLE.setdefault(fr, {"fr": fr, "en": en, "es": es})
