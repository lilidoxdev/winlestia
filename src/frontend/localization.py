# Python 3.12 / localization.py / Qt application translations and Windows UI language detection.
import ctypes,json
from pathlib import Path
from PySide6.QtCore import QTranslator
ROWS='''Paramètres|Settings|Ajustes
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
Workspaces|Workspaces|Espacios de trabajo'''
ROWS+="\n"+r"""Arrêter l’application|Stop application|Detener aplicación
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
ROWS+="\n"+r"""Temp. GPU|GPU temp|Temp. GPU
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
ROWS+="\n"+r"""Nouvelle sélection|New selection|Nueva selección
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
TABLE={}
for row in ROWS.splitlines():
 fr,en,es=row.split('|');value={'fr':fr,'en':en,'es':es};TABLE[fr]=value;TABLE.setdefault(en,value)
def windows_language():
 try:
  buffer=ctypes.create_unicode_buffer(85);ctypes.windll.kernel32.GetUserDefaultUILanguage.restype=ctypes.c_ushort
  ident=ctypes.windll.kernel32.GetUserDefaultUILanguage();primary=ident&0x3ff
  return {12:'fr',9:'en',10:'es'}.get(primary,'en')
 except Exception:return 'en'
class Translator(QTranslator):
 def __init__(self,config):
  super().__init__();self.path=Path(config)/'language.json'
  try:self.mode=json.loads(self.path.read_text(encoding='utf-8'))['language']
  except (OSError,ValueError,KeyError):self.mode='auto'
 @property
 def language(self):return windows_language() if self.mode=='auto' else self.mode
 def isEmpty(self):return False
 def translate(self,context,source,disambiguation=None,n=-1):return TABLE.get(source,{}).get(self.language,source)
 def select(self,mode):
  self.mode=mode if mode in ('auto','fr','en','es') else 'auto';self.path.write_text(json.dumps({'language':self.mode}),encoding='utf-8')

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
Le liseré suit la fenêtre au premier plan|The border follows the foreground window|El borde sigue a la ventana en primer plano""".splitlines():
 fr,en,es=row.split('|');TABLE[fr]={'fr':fr,'en':en,'es':es}
