# Resolver unit tests on a fake tree in %TEMP%\WinlestiaWallpaperTest (no real wallpaper app is touched).
import json,os,shutil,sys,tempfile,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent.parent))
import wallpaper_sources as w
ROOT=Path(tempfile.gettempdir())/'WinlestiaWallpaperTest'
MON=[{'name':'D1','path':'','background':'#000000'},{'name':'D2','path':'','background':'#000000'}]
def touch(p,data=b'x'):p=Path(p);p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(data);return str(p)
def wr(p,obj):p=Path(p);p.parent.mkdir(parents=True,exist_ok=True);p.write_text(json.dumps(obj),encoding='utf-8')
class Resolver(unittest.TestCase):
    def setUp(self):
        shutil.rmtree(ROOT,ignore_errors=True);ROOT.mkdir(parents=True)
        self.b={k:str(ROOT/k) for k in ('localappdata','appdata','programdata','steam')}
        self.win=touch(ROOT/'win.jpg')
        self.spi=lambda:self.win
    def hut(self,active):
        d=ROOT/'localappdata/Packages/Desktophut.Test_abc/LocalCache/Roaming/motion-photo-editor'
        touch(d/'videos/a.mp4');touch(d/'thumbnails/a.png');touch(d/'videos/b.mp4')
        wr(d/'config.json',{'settings':{'engine':'mpv'},'activeWallpapers':active,'wallpaperLibrary':[{'id':'A','title':'A','path':'C:/nowhere/a.mp4','thumbnail':'C:/nowhere/a.png'},{'id':'B','title':'B','path':str(d/'videos/b.mp4'),'thumbnail':''}]})
    def run_(self,names,**k):return w.resolve(MON,names=set(names),bases=self.b,live=k.pop('live',False),kind=k.pop('kind',0),spi=self.spi)
    def test_hut_span(self):
        self.hut({'span':'A'});r=self.run_({'desktophut studio.exe','mpv.exe'})
        for s in r.values():self.assertEqual((s.provider,s.key),('DesktopHut','desktophut:A'));self.assertTrue(s.video.endswith('a.mp4'));self.assertTrue(s.thumbnail.endswith('a.png'))
    def test_hut_per_monitor(self):
        self.hut({'D1':'A','D2':'B'});r=self.run_({'desktophut studio.exe','mpv.exe'})
        self.assertEqual((r['D1'].key,r['D2'].key),('desktophut:A','desktophut:B'))
    def test_hut_not_running(self):
        self.hut({'span':'A'});r=self.run_({'explorer.exe'});self.assertEqual(r['D1'].provider,'Windows')
    def test_we(self):
        lib=ROOT/'steam';cfg=lib/'steamapps/common/wallpaper_engine/config.json';proj=ROOT/'ws/123'
        touch(proj/'preview.jpg');touch(proj/'v.mp4');wr(proj/'project.json',{'title':'T','file':'v.mp4','preview':'preview.jpg','type':'video'})
        wr(cfg,{'user':{'wallpaperconfig':{'selectedwallpapers':{'Monitor0':{'file':str(proj/'project.json')},'Monitor1':{'file':str(proj/'project.json')}}}}})
        r=self.run_({'wallpaper64.exe'});s=r['D2'];self.assertEqual(s.provider,'WallpaperEngine');self.assertTrue(s.video.endswith('v.mp4'));self.assertTrue(s.images[0].endswith('preview.jpg'))
    def test_lively(self):
        base=ROOT/'localappdata/Lively Wallpaper';f=base/'Library/wallpapers/x1';touch(f/'t.jpg');touch(f/'v.mp4');wr(f/'LivelyInfo.json',{'Title':'L','Thumbnail':'t.jpg','FileName':'v.mp4'})
        wr(base/'WallpaperLayout.json',[{'LivelyInfoFolderPath':str(f),'Monitor':{'DeviceName':'D2'}}]);wr(base/'Settings.json',{'WallpaperArrangement':0})
        r=self.run_({'livelywpf.exe'});self.assertEqual(r['D2'].provider,'Lively');self.assertEqual(r['D1'].provider,'Windows')
    def test_windows_fallback_and_kinds(self):
        self.assertEqual(self.run_(set())['D1'].images,[self.win])
        self.assertEqual(self.run_(set(),kind=2)['D1'].provider,'Slideshow');self.assertEqual(self.run_(set(),kind=3)['D1'].provider,'Spotlight')
    def test_transcoded_fallback(self):
        t=touch(ROOT/'appdata/Microsoft/Windows/Themes/TranscodedWallpaper');r=w.resolve(MON,names=set(),bases=self.b,live=False,kind=0,spi=lambda:'')
        self.assertEqual(r['D1'].images,[t])
    def test_generic_live_and_missing(self):
        r=self.run_({'something.exe'},live=True);self.assertTrue(r['D1'].needs_capture);self.assertEqual(r['D1'].provider,'LiveGeneric')
        r=w.resolve(MON,names=set(),bases=self.b,live=False,kind=0,spi=lambda:'');self.assertEqual(r['D1'].images,[])  # nothing anywhere: empty, no crash
    def test_missing_files_hut(self):
        d=ROOT/'localappdata/Packages/Desktophut.Test_abc/LocalCache/Roaming/motion-photo-editor';wr(d/'config.json',{'activeWallpapers':{'span':'Z'},'wallpaperLibrary':[]})
        self.assertEqual(self.run_({'desktophut studio.exe','mpv.exe'})['D1'].provider,'Windows')
    def test_cache_prune_and_images(self):
        root=ROOT/'cache'
        for i in range(35):(root/f'e{i}').mkdir(parents=True);os.utime(root/f'e{i}',(1000+i,1000+i))
        w.prune_cache(root);left=sorted(p.name for p in root.iterdir());self.assertEqual(len(left),30);self.assertNotIn('e0',left)
        s=w.WallpaperSource('Windows','k',[self.win]);self.assertEqual(w.representative_images(s,self.b),[self.win])
if __name__=='__main__':unittest.main()
