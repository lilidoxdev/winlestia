# Python 3.12 / chromatic_palette.py / Windows. Chromatic prominence drives accents; surfaces stay dark.
from pathlib import Path
import colorsys

def generate_palette(path='',background='#182025'):
    from PIL import Image,ImageStat
    from material_color_utilities import theme_from_color,Variant,get_contrast_ratio
    image=None
    if path and Path(path).is_file():
        with Image.open(path) as original:
            original.draft('RGB',(192,192));original.thumbnail((160,160));image=original.convert('RGB')
    if image is None:image=Image.new('RGB',(16,16),background)
    luminance=ImageStat.Stat(image.convert('L')).mean[0]/255
    groups=[[0.,0.,0.,0.] for _ in range(36)];chromatic=0
    for red,green,blue in (image.get_flattened_data() if hasattr(image,'get_flattened_data') else image.getdata()):
        r,g,b=red/255,green/255,blue/255;h,s,v=colorsys.rgb_to_hsv(r,g,b);chroma=max(r,g,b)-min(r,g,b)
        if s<.16 or chroma<.035 or v<.08:continue
        weight=s*s*chroma**.7*v**.5;chromatic+=weight
        position=h*36;index=int(position);fraction=position-index
        for bucket,factor in ((index%36,1-fraction),((index+1)%36,fraction)):
            group=groups[bucket];w=weight*factor
            group[0]+=w;group[1]+=red*w;group[2]+=green*w;group[3]+=blue*w
    scores=[sum(groups[(i+j)%36][0]*(1 if j==0 else .65) for j in (-1,0,1)) for i in range(36)]
    monochrome=chromatic/(image.width*image.height)<.003
    sources=[]
    for index in sorted(range(36),key=lambda i:scores[i],reverse=True):
        if scores[index]<=0:continue
        if any(min((index-other)%36,(other-index)%36)<4 for other,_ in sources):continue
        neighbors=[groups[(index+j)%36] for j in (-1,0,1)];total=sum(g[0] for g in neighbors)
        if total:sources.append((index,'#'+''.join(f'{round(sum(g[c] for g in neighbors)/total):02x}' for c in (1,2,3))))
        if len(sources)==5:break
    source=sources[0][1] if sources and not monochrome else '#777777'
    h,s,v=colorsys.rgb_to_hsv(*(int(source[i:i+2],16)/255 for i in (1,3,5)))
    def color(saturation,value):return '#'+''.join(f'{round(c*255):02x}' for c in colorsys.hsv_to_rgb(h,saturation,value))
    scheme=theme_from_color(source,.1,Variant.MONOCHROME if monochrome else Variant.CONTENT).schemes.dark.dict()
    palette={'m3'+''.join(word[0].upper()+word[1:] for word in key.split('_')):value for key,value in scheme.items() if isinstance(value,str) and value.startswith('#')}
    palette={key[:2]+key[2].lower()+key[3:]:value for key,value in palette.items()}
    accent=color(min(.76,max(.55,s)),.98) if not monochrome else '#d0d0d0'
    accent_saturation=min(.76,max(.55,s))
    while get_contrast_ratio(accent,'#130d0e')<4.8 and accent_saturation>.15:
        accent_saturation-=.025;accent=color(accent_saturation,.98)
    palette.update(m3background=color(0 if monochrome else .28,.063),m3surface=color(0 if monochrome else .28,.063),m3surfaceContainerLow=color(0 if monochrome else .24,.095),m3surfaceContainer=color(0 if monochrome else .23,.13),m3surfaceContainerHigh=color(0 if monochrome else .22,.17),m3surfaceContainerHighest=color(0 if monochrome else .20,.22),m3primary=accent,m3secondary=accent,m3tertiary=accent,m3onPrimary='#130d0e',m3onSecondary='#130d0e',m3onTertiary='#130d0e',m3primaryContainer=color(0 if monochrome else .55,.25),m3secondaryContainer=color(0 if monochrome else .38,.21),m3onPrimaryContainer=color(0 if monochrome else .1,.98),m3onSecondaryContainer=color(0 if monochrome else .08,.95),m3tertiaryContainer=color(0 if monochrome else .38,.21),m3onTertiaryContainer=color(0 if monochrome else .08,.95),m3onSurface='#f7f3f4',m3onSurfaceVariant='#d3c9cc',m3outlineVariant=color(0 if monochrome else .2,.30))
    return {'palette':palette,'dark':True,'colors':[accent]+[c for _,c in sources[1:5]],'sourceColor':source,'source':Path(path).name if path else 'Couleur du Bureau','luminance':luminance,'monochrome':monochrome,'path':path,'provider':'Windows'}
