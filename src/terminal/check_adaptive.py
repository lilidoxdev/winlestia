from pathlib import Path
import tempfile,json
from PIL import Image
import sync_theme as sync
root=sync.ROOT;reports=[]
with tempfile.TemporaryDirectory() as folder:
    temp=Path(folder);config=json.loads(sync.SETTINGS.read_text(encoding='utf-8'));sandbox=temp/'sandbox';(sandbox/'clink-profile').mkdir(parents=True)
    (sandbox/'clink-profile/clink_settings').write_text('color.cmd = sgr 38;2;0;0;0\n',encoding='utf-8')
    (temp/'settings.json').write_text(json.dumps(config),encoding='utf-8');sync.ROOT=sandbox;sync.SETTINGS=temp/'settings.json'
    for name,color in [('rouge','#b71925'),('bleu','#173ba8')]:
        path=temp/(name+'.png');Image.new('RGB',(40,40),color).save(path);palette=sync.generate_palette(str(path));values=sync.theme_values(palette,name);sync.apply(values)
        result=json.loads(sync.SETTINGS.read_text(encoding='utf-8'));scheme=next(s for s in result['schemes'] if s['name']=='Winlestia Glass')
        reports.append({'case':name,'accent':values['accent'],'background':values['background'],'samePaletteAsWinlestia':values['accent']==palette['palette']['m3primary'] and values['background']==palette['palette']['m3surface'],'opacityPreserved':result['profiles']['defaults']['opacity']==68,'profilesPreserved':result['profiles']==config['profiles'],'schemeUpdated':scheme['blue']==values['accent']})
result={'paletteCases':reports,'allPassed':all(all(v for k,v in report.items() if k.endswith('Preserved') or k in ('samePaletteAsWinlestia','schemeUpdated')) for report in reports)}
(root/'validation/adaptive-tests.json').write_text(json.dumps(result,indent=2),encoding='utf-8');print(json.dumps(result));raise SystemExit(0 if result['allPassed'] else 1)
