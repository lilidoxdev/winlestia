# Python 3.12 / palette_child.py / Windows. Disposable palette worker: numpy/PIL/material_color_utilities live and die in this child, never in the resident frontend.
# Usage: python palette_child.py '<json request>' -> prints the palette dict as JSON on stdout.
import sys,json
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
import chromatic_palette
def main(argv):
    request=json.loads(argv[1])
    if request.get('op')=='images':
        result=chromatic_palette.palette_from_images(list(request['images']),background=request['background'],previous_dark=request.get('previous_dark'),slot=request.get('slot'),source=request.get('source',''))
    else:
        result=chromatic_palette.generate_palette(request.get('path',''),request['background'])
    sys.stdout.write(json.dumps(result))
if __name__=='__main__':main(sys.argv)
