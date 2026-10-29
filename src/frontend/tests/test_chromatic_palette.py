# Python 3.12 / test_chromatic_palette.py / Synthetic wallpapers -> seeds, light/dark decision, contrast, determinism.
import sys,tempfile,unittest
from pathlib import Path
import numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from PIL import Image
from material_color_utilities import get_contrast_ratio,Hct
import chromatic_palette as cp

def grad(w,h,stops,vertical=True):
    t=np.linspace(0,1,h if vertical else w);cols=np.stack([np.interp(t,[s[0] for s in stops],[s[1][c] for s in stops]) for c in range(3)],1)
    return np.repeat(cols[:,None,:],w,1) if vertical else np.repeat(cols[None,:,:],h,0)
def blob(a,cx,cy,r,col,amt=1.0):
    h,w,_=a.shape;y,x=np.mgrid[0:h,0:w];d=np.exp(-(((x-cx*w)**2+(y-cy*h)**2)/(2*(r*w)**2)))[...,None];return a*(1-d*amt)+np.array(col)*d*amt
def save(a,path,seed=0):
    a=a+np.random.default_rng(seed).normal(0,2.0,a.shape);Image.fromarray(np.clip(a,0,255).astype('uint8')).save(path,quality=88);return str(path)
def hue_of(hex_):return Hct(hex_).dict()['hue']
def dh(a,b):return min((a-b)%360,(b-a)%360)

class Palette(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp=tempfile.TemporaryDirectory();d=Path(cls.tmp.name);w,h=320,180;p={}
        p['navy']=save(blob(grad(w,h,[(0,(8,14,34)),(1,(14,28,64))]),.7,.4,.18,(40,90,170),.6),d/'navy.jpg')
        p['red']=save(blob(blob(grad(w,h,[(0,(12,4,6)),(1,(30,6,10))]),.3,.5,.2,(140,16,28),.7),.75,.3,.06,(220,40,40),.6),d/'red.jpg')
        p['neon']=save(blob(blob(np.full((h,w,3),6.0),.5,.5,.22,(150,40,230),.8),.5,.5,.06,(210,120,255),.7),d/'neon.jpg')
        p['pastel']=save(grad(w,h,[(0,(255,225,236)),(.5,(220,236,255)),(1,(236,255,228))],False),d/'pastel.jpg')
        p['grey']=save(grad(w,h,[(0,(70,70,70)),(1,(120,120,120))]),d/'grey.jpg')
        p['sunset']=save(grad(w,h,[(0,(40,30,90)),(.45,(200,70,100)),(.7,(250,150,60)),(1,(30,20,40))]),d/'sunset.jpg')
        base=blob(grad(w,h,[(0,(8,14,34)),(1,(14,28,64))]),.7,.4,.18,(40,90,170),.6)
        p['frames']=[save(np.roll(base,s*7,axis=1),d/f'f{s}.jpg',seed=s) for s in range(8)]
        cls.p=p
    @classmethod
    def tearDownClass(cls):cls.tmp.cleanup()
    def theme(self,name,key=None):
        paths=self.p[name] if isinstance(self.p[name],list) else [self.p[name]];return cp.palette_from_images(paths,key=key or name+'-t',previous_dark=None)
    def test_hue_families(self):
        for name,lo,hi in (('navy',235,285),('red',350,40),('neon',285,335)):
            seed=self.theme(name)['seeds'][0];hue=seed['hue'];centre=(lo+hi)/2 if lo<hi else ((lo+hi+360)/2)%360;span=((hi-lo)%360)/2
            self.assertLessEqual(dh(hue,centre),span+1,(name,hue))
    def test_dark_images_are_confident_not_monochrome(self):
        for name in ('navy','red','neon'):
            t=self.theme(name);self.assertFalse(t['monochrome'],name);self.assertTrue(t['dark'],name)
            self.assertGreaterEqual(Hct(t['palette']['m3primary']).dict()['chroma'],30,name)  # not muddy grey
    def test_light_dark_decision(self):
        self.assertFalse(self.theme('pastel')['dark']);self.assertTrue(self.theme('navy')['dark'])
    def test_hysteresis(self):
        self.assertTrue(cp.decide_dark(56,True));self.assertFalse(cp.decide_dark(56,False));self.assertTrue(cp.decide_dark(40,False));self.assertFalse(cp.decide_dark(70,True))
    def test_grey_is_monochrome_but_tinted_neutral(self):
        t=self.theme('grey');self.assertTrue(t['monochrome']);self.assertLess(Hct(t['palette']['m3primary']).dict()['chroma'],12)
    def test_primary_secondary_tertiary_distinct(self):
        for name in ('navy','red','neon','sunset'):
            pal=self.theme(name)['palette'];self.assertEqual(len({pal['m3primary'],pal['m3secondary'],pal['m3tertiary']}),3,name)
            self.assertGreaterEqual(dh(hue_of(pal['m3primary']),hue_of(pal['m3tertiary'])),30,name)
    def test_sunset_tertiary_uses_second_seed(self):
        t=self.theme('sunset');seeds=t['seeds'];self.assertGreaterEqual(len(seeds),2)
        self.assertLessEqual(dh(hue_of(t['palette']['m3tertiary']),next(s['hue'] for s in seeds[1:] if dh(s['hue'],seeds[0]['hue'])>=40)),12)
    def test_contrast(self):
        for name in ('navy','red','neon','pastel','grey','sunset','frames'):
            p=self.theme(name)['palette']
            for fg,bg,r in (('onPrimary','primary',4.5),('onSecondary','secondary',4.5),('onTertiary','tertiary',4.5),('onSurface','surface',7),('onSurfaceVariant','surfaceContainer',4.5),('outline','surface',3),('onPrimaryContainer','primaryContainer',4.5)):
                self.assertGreaterEqual(get_contrast_ratio(p['m3'+fg],p['m3'+bg]),r,(name,fg))
    def test_shell_keys_present(self):
        need='background surface onSurface surfaceContainer surfaceContainerLow surfaceContainerHigh surfaceContainerHighest primary secondary tertiary primaryContainer secondaryContainer tertiaryContainer onPrimary onSecondary onTertiary onPrimaryContainer onSecondaryContainer onTertiaryContainer onSurfaceVariant outline outlineVariant error onError scrim shadow inverseSurface inverseOnSurface inversePrimary'.split()
        p=self.theme('navy')['palette'];self.assertEqual([k for k in need if 'm3'+k not in p],[])
        legacy=self.theme('navy');self.assertTrue(all(k in legacy for k in ('palette','dark','colors','sourceColor','source','luminance','monochrome','path','provider')))
    def test_deterministic_and_pooled_frames_stable(self):
        a=cp.palette_from_images(self.p['navy'] and [self.p['navy']],key='d1');cp._cache.clear();b=cp.palette_from_images([self.p['navy']],key='d1');self.assertEqual(a['palette'],b['palette'])
        frames=self.p['frames'];one=cp.palette_from_images(frames[:4],key='fa');two=cp.palette_from_images(frames[4:],key='fb');self.assertEqual(one['palette']['m3primary'],two['palette']['m3primary'])
    def test_old_wrapper(self):
        t=cp.generate_palette(self.p['red']);self.assertIn('m3primary',t['palette']);t=cp.generate_palette('');self.assertIn('m3surface',t['palette'])
if __name__=='__main__':unittest.main()
