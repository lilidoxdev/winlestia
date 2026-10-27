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
Un rendu plus léger pour les petites configurations|Lighter rendering for modest hardware|Renderizado ligero para equipos modestos""".splitlines():
 fr,en,es=row.split('|');TABLE[fr]={'fr':fr,'en':en,'es':es}
