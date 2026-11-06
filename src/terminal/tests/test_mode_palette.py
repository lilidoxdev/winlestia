# Terminal scheme must follow the shell theme mode. Run: python -m unittest discover -s src/terminal/tests
import sys,json,tempfile,unittest
from pathlib import Path
TERM=Path(__file__).resolve().parents[1];sys.path.insert(0,str(TERM))
import sync_theme as sync
CUSTOM={'background':'#1b1030','text':'#f0e6ff','primary':'#c084fc','secondary':'#7dd3fc','tertiary':'#fbbf24'}
AUTO={'palette':{'m3surface':'#202830','m3primary':'#66aa88','m3onSurface':'#eef2f0'}}
class ModePalette(unittest.TestCase):
    def scheme(self,prefs):
        with tempfile.TemporaryDirectory() as d:
            d=Path(d);(d/'state.json').write_text('{}');settings=d/'LocalState'/'settings.json';settings.parent.mkdir();settings.write_text('{}')
            old=(sync.STATE,sync.note_written,sync.shell_palette,sync.generate_palette)
            sync.STATE=d;sync.note_written=lambda *a,**k:None;sync.shell_palette=lambda:AUTO;sync.generate_palette=lambda *a:AUTO
            try:
                palette,mode=sync.resolve_palette(prefs,'wall.jpg','#000000')
                values=sync.console_values(sync.theme_values(palette,mode),palette,mode)
                sync.update_terminal(settings,values)
            finally:sync.STATE,sync.note_written,sync.shell_palette,sync.generate_palette=old
            c=json.loads(settings.read_text(encoding='utf-8'))
            return next(s for s in c['schemes'] if s['name']=='Winlestia Glass')
    def check(self,prefs,bg,fg,accent):
        s=self.scheme(prefs);self.assertEqual((s['background'],s['foreground'],s['blue']),(bg,fg,accent))
    def test_porcelaine(self):self.check({'Theme':'shell'},'#fff8f6','#261916','#895143')
    def test_nuit(self):self.check({'Theme':'nuit'},'#101518','#e5ebef','#afd4f0')
    def test_custom(self):self.check({'Theme':'custom','CustomPalette':CUSTOM},'#1b1030','#f0e6ff','#c084fc')
    def test_auto_wallpaper(self):
        s=self.scheme({'Theme':'nuit','AutoTheme':True});self.assertNotEqual(s['background'],'#101518');self.assertEqual(s['blue'],sync.lighten('#66aa88',s['background'],4.5,.45))
    def test_auto_beats_stale_custom_theme(self):
        s=self.scheme({'Theme':'custom','AutoTheme':True,'CustomPalette':CUSTOM});self.assertNotEqual(s['background'],'#1b1030')
    def test_ignores_stale_wallpaper_file_when_not_auto(self):self.check({'Theme':'nuit','AutoTheme':False},'#101518','#e5ebef','#afd4f0')
if __name__=='__main__':unittest.main()
