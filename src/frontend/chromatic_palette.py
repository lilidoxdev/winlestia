# Python 3.12 / chromatic_palette.py / Windows. Wallpaper -> seeds (pooled quantiser + HCT scoring) -> Material You palette (light or dark).
from pathlib import Path
import math
import numpy as np

HEX='#{:02x}{:02x}{:02x}'
MONO_CHROMA=8.0        # HCT chroma under which a wallpaper is treated as colourless
TO_LIGHT=62.0;TO_DARK=50.0   # hysteresis on area-weighted L*
_cache={};_dark_state={}  # _dark_state: bounded (<=16) light/dark hysteresis memory, keyed by monitor slot

def _oklab(rgb):
    c=rgb/255.0;c=np.where(c<=.04045,c/12.92,((c+.055)/1.055)**2.4)
    l=.4122214708*c[:,0]+.5363325363*c[:,1]+.0514459929*c[:,2]
    m=.2119034982*c[:,0]+.6806995451*c[:,1]+.1073969566*c[:,2]
    s=.0883024619*c[:,0]+.2817188376*c[:,1]+.6299787005*c[:,2]
    l,m,s=np.cbrt(l),np.cbrt(m),np.cbrt(s)
    return np.stack([.2104542553*l+.7936177850*m-.0040720468*s,1.9779984951*l-2.4285922050*m+.4505937099*s,.0259040371*l+.7827717662*m-.8086757660*s],1)

def _lstar(rgb):
    c=rgb/255.0;c=np.where(c<=.04045,c/12.92,((c+.055)/1.055)**2.4)
    y=c[:,0]*.2126+c[:,1]*.7152+c[:,2]*.0722
    return np.where(y>216/24389,116*np.cbrt(y)-16,y*24389/27)

def _pixels(paths,background):
    from PIL import Image
    chunks=[]
    for path in paths:
        try:
            if not path or not Path(path).is_file():continue
            with Image.open(path) as original:
                original.draft('RGB',(192,192));original.thumbnail((96,96));chunks.append(np.asarray(original.convert('RGB'),dtype=np.uint8).reshape(-1,3))
        except Exception:continue
    if not chunks:
        value=background if isinstance(background,str) and len(background)==7 else '#182025'
        chunks.append(np.array([[int(value[i:i+2],16) for i in (1,3,5)]],dtype=np.uint8))
    return np.concatenate(chunks)

def _quantize(pixels,clusters=24):
    """5-bit histogram then deterministic weighted k-means in OKLab (Celebi-style: Wu bins refined by WSMeans)."""
    p=pixels.astype(np.int64);index=((p[:,0]>>3)<<10)|((p[:,1]>>3)<<5)|(p[:,2]>>3)
    counts=np.bincount(index,minlength=32768);used=np.nonzero(counts)[0];w=counts[used].astype(np.float64)
    mean=np.stack([np.bincount(index,weights=p[:,c],minlength=32768)[used]/w for c in range(3)],1)
    lab=_oklab(mean);k=min(clusters,len(used))
    chosen=[int(np.argmax(w))];nearest=((lab-lab[chosen[0]])**2).sum(1)
    while len(chosen)<k:
        i=int(np.argmax(np.sqrt(w)*nearest));chosen.append(i);nearest=np.minimum(nearest,((lab-lab[i])**2).sum(1))
    centers=lab[chosen]
    for _ in range(7):
        label=((lab[:,None,:]-centers[None,:,:])**2).sum(2).argmin(1)
        for c in range(k):
            m=label==c
            if m.any():centers[c]=(lab[m]*w[m,None]).sum(0)/w[m].sum()
    label=((lab[:,None,:]-centers[None,:,:])**2).sum(2).argmin(1)
    out=[]
    for c in range(k):
        m=label==c
        if m.any():out.append((w[m].sum()/w.sum(),(mean[m]*w[m,None]).sum(0)/w[m].sum()))
    return out,float((_lstar(mean)*w).sum()/w.sum())

def _hex(rgb):return HEX.format(*(int(round(min(255,max(0,v)))) for v in rgb))

def analyze_images(paths,key=None,background='#182025'):
    """Seeds + lightness for 1..N small images pooled together. Deterministic; cached by `key` (or by paths+mtimes)."""
    from material_color_utilities import Hct
    paths=[str(p) for p in paths or []]
    if key is None:
        try:key=tuple((p,Path(p).stat().st_mtime_ns) for p in paths if p)+(background,)
        except OSError:key=(tuple(paths),background)
    if key in _cache:return _cache[key]
    pixels=_pixels(paths,background);clusters,lightness=_quantize(pixels)
    items=[]
    for proportion,rgb in clusters:
        h=Hct(_hex(rgb)).dict();items.append({'hex':_hex(rgb),'proportion':float(proportion),'chroma':float(h['chroma']),'hue':float(h['hue']),'tone':float(h['tone'])})
    # Hue-smoothed "excited" proportion (+-15 deg) as in Material's Score, then chroma-aware rank.
    for it in items:
        it['excited']=sum(o['proportion'] for o in items if o['chroma']>=5 and min((o['hue']-it['hue'])%360,(it['hue']-o['hue'])%360)<=15)
    def score(it):
        bonus=(it['chroma']-48)*(.1 if it['chroma']<48 else .3)
        return it['excited']*100*.7+bonus+math.sqrt(it['proportion'])*it['chroma']*.35
    ranked=[it for it in sorted(items,key=lambda i:(-score(i),i['hex'])) if it['chroma']>=5 and it['excited']>=.01 and it['proportion']>=.004]
    seeds=[]
    for it in ranked:
        if all(min((it['hue']-s['hue'])%360,(s['hue']-it['hue'])%360)>=15 for s in seeds):seeds.append(it)
        if len(seeds)==4:break
    for s in seeds:  # a dark image's bulk is low-chroma: a seed inherits the vivid accents of its own hue family
        family=[o['chroma'] for o in items if o['proportion']>=.01 and min((o['hue']-s['hue'])%360,(s['hue']-o['hue'])%360)<=20]
        s['familyChroma']=max([s['chroma']]+[c*.9 for c in family])
    top=max([o['chroma'] for o in items if o['proportion']>.01]+[0.0])
    mono=top<MONO_CHROMA or not seeds
    if mono and not seeds:
        tint=max(items,key=lambda i:(i['chroma']*math.sqrt(i['proportion']),i['hex']));seeds=[dict(tint,familyChroma=tint['chroma'],excited=tint['proportion'])]
    colourfulness=min(1.0,sum(it['proportion']*it['chroma'] for it in items)/40.0)
    result={'seeds':[{'hex':s['hex'],'proportion':round(s['proportion'],4),'chroma':round(s['chroma'],2),'hue':round(s['hue'],1),'familyChroma':round(s['familyChroma'],2)} for s in seeds],'lightness':round(lightness,2),'colorfulness':round(colourfulness,3),'monochrome':bool(mono),'maxChroma':round(top,2)}
    if len(_cache)>48:_cache.clear()
    _cache[key]=result;return result

def decide_dark(lightness,previous=None):
    """Dark wallpaper -> dark scheme, bright -> light, with a dead band so near-threshold images do not flip."""
    if lightness>=TO_LIGHT:return False
    if lightness<=TO_DARK:return True
    return True if previous is None else bool(previous)

def _boost(chroma):
    """Seed chroma -> scheme chroma. Dark images have low HCT chroma at low tone; lift them so the hue still reads."""
    if chroma<MONO_CHROMA:return 8+chroma*.5
    if chroma<16:return 12+(chroma-MONO_CHROMA)*2.25
    return min(78.0,30+(chroma-16)*.95)

def _dh(a,b):return min((a-b)%360,(b-a)%360)

def build_scheme(analysis,dark):
    from material_color_utilities import TonalPalette,get_contrast_ratio
    seeds=analysis['seeds'];first=seeds[0];hue=first['hue'];mono=analysis['monochrome']
    if mono:primary_c=min(10.0,3+analysis['maxChroma'])
    else:primary_c=_boost(first['familyChroma']);primary_c=max(primary_c,min(first['familyChroma'],78.0),48.0 if first['familyChroma']>=30 else 0)
    secondary_c=max(8.0,min(40.0,primary_c*.55)) if not mono else 6.0
    tertiary_hue=hue if mono else (hue+60)%360;tertiary_c=max(22.0,primary_c*.65) if not mono else 8.0;tertiary_hex=None
    if not mono:
        for s in seeds[1:]:
            if _dh(s['hue'],hue)>=40 and s['familyChroma']>=12 and s['proportion']>=.02:
                tertiary_hue=s['hue'];tertiary_c=max(22.0,min(60.0,_boost(s['familyChroma'])*.85));tertiary_hex=s['hex'];break
    neutral_c=3.0 if mono else min(16.0,6+primary_c*.14)
    P=TonalPalette(hue,primary_c);S=TonalPalette(hue,secondary_c);T=TonalPalette(tertiary_hue,tertiary_c)
    N=TonalPalette(hue,neutral_c);NV=TonalPalette(hue,neutral_c+(2.5 if mono else 4.0));E=TonalPalette(25.0,84.0)
    d=dark
    def tone(dt,lt):return dt if d else lt
    roles={}
    def put(name,palette,dt,lt):roles[name]=palette.get(tone(dt,lt))
    for n,p in (('primary',P),('secondary',S),('tertiary',T)):
        put(n,p,60 if n=='primary' else 72,40);put('on'+n.capitalize(),p,20,100);put(n+'Container',p,30,90);put('on'+n.capitalize()+'Container',p,90,10)
        put(n+'Fixed',p,90,90);put(n+'FixedDim',p,80,80);put('on'+n.capitalize()+'Fixed',p,10,10);put('on'+n.capitalize()+'FixedVariant',p,30,30)
    put('inversePrimary',P,40,80);put('surfaceTint',P,80,40)
    put('error',E,80,40);put('onError',E,20,100);put('errorContainer',E,30,90);put('onErrorContainer',E,90,10)
    put('background',N,6,98);put('onBackground',N,90,10);put('surface',N,6,98);put('onSurface',N,90,10)
    put('surfaceDim',N,6,87);put('surfaceBright',N,24,98);put('surfaceContainerLowest',N,4,100);put('surfaceContainerLow',N,10,96)
    put('surfaceContainer',N,12,94);put('surfaceContainerHigh',N,17,92);put('surfaceContainerHighest',N,22,90)
    put('surfaceVariant',NV,30,90);put('onSurfaceVariant',NV,80,30);put('outline',NV,60,50);put('outlineVariant',NV,30,80)
    put('inverseSurface',N,90,20);put('inverseOnSurface',N,20,95)
    roles['shadow']='#000000';roles['scrim']='#000000'
    palette={'m3'+k:v for k,v in roles.items()}
    def fix(fg,bg,ratio,pal,start,step):  # walk the foreground along its own tonal palette until the contrast target holds
        t=start
        while get_contrast_ratio(palette[fg],palette[bg])<ratio and 0<t<100:t+=step;palette[fg]=pal.get(t)
    s=1 if d else -1
    fix('m3onSurface','m3surface',9,N,90 if d else 10,s);fix('m3onSurfaceVariant','m3surfaceContainer',6,NV,80 if d else 30,s);fix('m3outline','m3surface',3.2,NV,60 if d else 50,s)
    for n,p,bg in (('Primary',P,'m3primary'),('Secondary',S,'m3secondary'),('Tertiary',T,'m3tertiary')):fix('m3on'+n,bg,4.5,p,20 if d else 100,-s)
    return palette,{'primary':palette['m3primary'],'primaryHue':round(hue,1),'primaryChroma':round(primary_c,1),'tertiaryHue':round(tertiary_hue,1),'tertiaryHex':tertiary_hex}

def variant_name(analysis):
    if analysis['monochrome']:return 'neutral'
    c=analysis['seeds'][0]['familyChroma']
    return 'faithful' if c<24 else 'vibrant' if c>=60 and analysis['colorfulness']>=.6 else 'tonalspot'

def palette_from_images(paths,background='#182025',key=None,previous_dark=None,source='',slot=None):
    """Full theme dict (same shape the shell has always consumed) from one image or several pooled frames."""
    analysis=analyze_images(paths,key,background);state_key=slot if slot is not None else key if key is not None else 'default'
    dark=decide_dark(analysis['lightness'],previous_dark if previous_dark is not None else _dark_state.get(state_key))
    _dark_state.pop(state_key,None);_dark_state[state_key]=dark
    while len(_dark_state)>16:_dark_state.pop(next(iter(_dark_state)))
    palette,info=build_scheme(analysis,dark)
    others=[]
    from material_color_utilities import TonalPalette
    for s in analysis['seeds'][1:]:others.append(TonalPalette(s['hue'],_boost(s['familyChroma'])).get(80 if dark else 40))
    colors=[palette['m3primary'],palette['m3secondary'],palette['m3tertiary']]+others
    return {'palette':palette,'dark':dark,'colors':colors[:5],'sourceColor':info['primary'],'source':source or 'Couleur du Bureau','luminance':analysis['lightness']/100,'monochrome':analysis['monochrome'],'path':paths[0] if paths else '','provider':'Windows','seeds':analysis['seeds'],'colorfulness':analysis['colorfulness'],'variant':variant_name(analysis)}

def generate_palette(path='',background='#182025'):
    """Single-image entry point kept for wallpaper_theme.py."""
    return palette_from_images([path] if path else [],background,source=Path(path).name if path else '')
