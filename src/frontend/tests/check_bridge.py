# Python 3.12 / Windows. Metadata integration tests against an isolated backend checkout.
import ctypes, io, json, os, time
from pathlib import Path
report=[]
with open(r'\\.\pipe\PanneauxCaelestia','r+b',buffering=0) as pipe:
    reader=io.BufferedReader(pipe)
    def request(command,args):
        pipe.write((json.dumps({'command':command,'args':args})+'\n').encode());return json.loads(reader.readline())
    def check(name,ok):report.append({'name':name,'pass':bool(ok)});assert ok,name
    state=request('state',{'visible':False,'page':5,'pid':os.getpid(),'epoch':9000})['state']
    backend_root=Path(state['diagnostics']['root']).resolve();expected=Path(__file__).resolve().parents[2]/'research'/os.environ.get('PANNEAUX_TEST_CANDIDATE','qt81-candidate')
    if expected.name not in ('qt81-candidate','qt82-candidate','qt83-candidate'):raise SystemExit('Only isolated test backends are allowed.')
    if backend_root!=expected.resolve():raise SystemExit('This test requires the isolated qt81-candidate backend.')
    entry=state['apps'][0];key=entry['id'];original_favorite=entry['favorite'];original_size=state['settings']['iconSize'];original_motion=state['settings']['motionScale']
    def current(response):return next(e for e in response['state']['apps'] if e['id']==key)
    changed=request('favorite',{'id':key});check('Favorite toggled',current(changed)['favorite']!=original_favorite)
    restored=request('favorite',{'id':key});check('Favorite restored',current(restored)['favorite']==original_favorite)
    changed=request('editEntry',{'id':key,'name':'Qt integration check','category':'QA'});check('Rename and category applied',current(changed)['name']=='Qt integration check' and current(changed)['category']=='QA')
    restored=request('editEntry',{'id':key,'name':entry['name'],'category':entry['category']});check('Name and category restored',current(restored)['name']==entry['name'] and current(restored)['category']==entry['category'])
    board='projects' if entry['board']!='projects' else 'apps'
    changed=request('move',{'id':key,'board':board});check('Workspace organization applied',current(changed)['board']==board)
    restored=request('move',{'id':key,'board':entry['board']});check('Original organization restored',current(restored)['board']==entry['board'])
    changed=request('preference',{'key':'iconSize','value':60});check('Icon density applied',changed['state']['settings']['iconSize']==60)
    request('preference',{'key':'iconSize','value':original_size})
    changed=request('preference',{'key':'motion','value':.65});check('Motion preference applied',changed['state']['settings']['motionScale']==.65)
    request('preference',{'key':'motion','value':original_motion})
    changed=request('captureShortcutMode',{'enabled':True});check('Shortcut recorder is armed',changed['state']['diagnostics']['capturingShortcut'])
    changed=request('captureShortcutMode',{'enabled':False});check('Shortcut recorder is disarmed',not changed['state']['diagnostics']['capturingShortcut'])
    before_shortcut=state['settings']['shortcut'];changed=request('shortcut',{'kind':'panel','modifiers':2,'key':0x71});check('Shortcut collision preserves the previous binding',changed['state']['settings']['shortcut']==before_shortcut and bool(changed.get('notice')))
    kernel=ctypes.windll.kernel32;kernel.OpenEventW.argtypes=[ctypes.c_ulong,ctypes.c_bool,ctypes.c_wchar_p];kernel.OpenEventW.restype=ctypes.c_void_p;kernel.ResetEvent.argtypes=[ctypes.c_void_p];kernel.WaitForSingleObject.argtypes=[ctypes.c_void_p,ctypes.c_ulong];kernel.CloseHandle.argtypes=[ctypes.c_void_p]
    handle=kernel.OpenEventW(0x100002,False,'Local\\PanneauxCaelestiaWake');check('Shared wake event is available',bool(handle));kernel.ResetEvent(handle)
    request('palette',{});check('Launcher event wakes the client',kernel.WaitForSingleObject(handle,0)==0);kernel.CloseHandle(handle)
Path('validation-8.1/bridge-tests.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report,indent=2))
