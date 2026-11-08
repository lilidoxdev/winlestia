"""Contrat QML/Bridge : chaque nom lu sur `Bridge.` dans le QML existe dans le métaobjet Qt du Bridge."""
import re
import sys
from pathlib import Path

FRONT = Path(__file__).resolve().parent.parent / 'src' / 'frontend'
sys.path.insert(0, str(FRONT))

# Signaux, slots et propriétés vivent maintenant répartis sur des mixins : seul le métaobjet de la
# classe assemblée prouve que QML les retrouve encore.
USED = re.compile(r'\bBridge\.([A-Za-z_]\w*)')


def qml_names():
    names = set()
    for path in FRONT.rglob('*.qml'):
        # Les harnais de test ont leur propre faux Bridge.
        if 'tests' in path.relative_to(FRONT).parts:
            continue
        names.update(USED.findall(path.read_text(encoding='utf-8')))
    return names


def exposed_names():
    from bridge import Bridge

    meta = Bridge.staticMetaObject
    names = {meta.property(i).name() for i in range(meta.propertyCount())}
    names |= {bytes(meta.method(i).name()).decode() for i in range(meta.methodCount())}
    # Les propriétés NOTIFY exposent aussi leur signal `<nom>Changed` au QML.
    return names | {n + 'Changed' for n in names}


def test_every_qml_bridge_name_exists():
    used = qml_names()
    assert len(used) > 80
    missing = sorted(used - exposed_names())
    assert not missing, 'noms utilisés en QML mais absents du Bridge : %s' % missing


def test_bridge_keeps_mixin_slots_and_notifying_properties():
    from bridge import Bridge

    meta = Bridge.staticMetaObject
    # Un Property déplacé hors de la classe aux signaux perdrait son signal de notification sans erreur.
    for name in ('themeMode', 'dark', 'settings', 'fileDragActive', 'frameScreens', 'paletteResults'):
        prop = meta.property(meta.indexOfProperty(name))
        assert prop.isValid() and prop.hasNotifySignal(), name
    for signature in ('navigate(QString)', 'action(QString,QVariant)', 'setWallpaper(QString)', 'refreshWeather()'):
        assert meta.indexOfMethod(signature) >= 0, signature
