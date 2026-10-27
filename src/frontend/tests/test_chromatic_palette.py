# Python 3.12 / tests/test_chromatic_palette.py / Regression tests for wallpaper-driven accents.
from pathlib import Path
import sys,tempfile,unittest,colorsys
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from chromatic_palette import generate_palette
from PIL import Image,ImageDraw
from material_color_utilities import get_contrast_ratio

class PaletteTests(unittest.TestCase):
    def palette(self,base,accent=None):
        with tempfile.TemporaryDirectory() as folder:
            image=Image.new('RGB',(300,200),base)
            if accent:ImageDraw.Draw(image).rectangle((0,0,119,199),fill=accent)
            path=Path(folder)/'wallpaper.png';image.save(path);return generate_palette(str(path))
    def hue(self,theme):return colorsys.rgb_to_hsv(*(int(theme['palette']['m3primary'][i:i+2],16)/255 for i in (1,3,5)))[0]
    def test_red_flowers_beat_dark_blue_shadows(self):
        hue=self.hue(self.palette('#081d29','#75001b'));self.assertTrue(hue>.94 or hue<.05)
    def test_blue_remains_blue(self):self.assertTrue(.54<self.hue(self.palette('#085bd0'))<.72)
    def test_green_remains_green(self):self.assertTrue(.25<self.hue(self.palette('#09a932'))<.43)
    def test_yellow_remains_yellow_and_stays_dark(self):
        theme=self.palette('#f5dc42');self.assertTrue(.10<self.hue(theme)<.20);self.assertTrue(theme['dark'])
    def test_monochrome_has_no_invented_hue(self):
        theme=self.palette('#eeeeee');self.assertTrue(theme['monochrome']);self.assertEqual(theme['palette']['m3primary'],'#d0d0d0')
    def test_text_and_accents_keep_contrast(self):
        for color in ('#e00021','#f9de41','#12a63b','#1746ed','#a421cb'):
            p=self.palette(color)['palette'];self.assertGreaterEqual(get_contrast_ratio(p['m3primary'],p['m3onPrimary']),4.5);self.assertGreaterEqual(get_contrast_ratio(p['m3surface'],p['m3onSurface']),7)
    def test_fallback_without_wallpaper_file(self):
        theme=generate_palette('Z:/missing-wallpaper.png','#2874bb');self.assertTrue(theme['dark']);self.assertTrue(.5<self.hue(theme)<.7)

if __name__=='__main__':unittest.main()
