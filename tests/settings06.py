from pathlib import Path
import sys,tempfile
sys.path.insert(0,'src/frontend')
from localization import Translator,windows_language
from search import palette_search
with tempfile.TemporaryDirectory() as path:
 t=Translator(path)
 for mode,expected in [('en','Settings'),('fr','Paramètres'),('es','Ajustes')]:
  t.select(mode);assert t.translate('','Paramètres')==expected
 t.select('auto');assert t.language in ('en','fr','es')
 t.select('es');assert Translator(path).mode=='es'
a=palette_search('',{'systemRecent':[{'id':'recent:C','name':'Canada'}],'quickTabs':[{'id':'games','name':'Jeux'}]});assert a[0]['name']=='Canada'
print('Language selection/persistence, Windows fallback and recent-first ordering passed')
