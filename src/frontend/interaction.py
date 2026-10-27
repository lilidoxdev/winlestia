# Python 3.12 / Windows 11. Pure edge and fullscreen policy, separate from rendering.
def is_fullscreen(style, maximized, client, screen):
    return all(abs(a-b)<=2 for a,b in zip(client,screen))

class EdgePolicy:
    def __init__(self):
        self.away_since=0
    def update(self, now, edge, inside, fullscreen, forced=False):
        # Deliberately reaching the edge wins; a fullscreen app alone never reveals it.
        if edge or forced:
            self.away_since=0
            return True
        # Once revealed, the pointer being inside the dock keeps it open. Without this
        # order a fullscreen app closes the dock the moment the pointer leaves the
        # seven-pixel edge, so no icon can ever be clicked.
        if inside:
            self.away_since=0
            return True
        if fullscreen:
            self.away_since=0
            return False
        if not self.away_since:self.away_since=now
        return False if now-self.away_since>=.45 else None
