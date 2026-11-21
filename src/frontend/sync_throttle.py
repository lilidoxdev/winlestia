# Python 3.12 / sync_throttle.py. Coalescence des synchronisations pilotées par la géométrie.
from time import monotonic

INTERVAL_MS = 33


def delay_ms(last, now=None, interval=INTERVAL_MS):
    """Délai avant la prochaine synchronisation : 0 si l'intervalle est écoulé, sinon le reste (synchro finale garantie)."""
    now = monotonic() if now is None else now
    return max(0, int(round(interval - (now - last) * 1000)))
