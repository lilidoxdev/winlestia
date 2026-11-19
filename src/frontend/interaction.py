# Python 3.12 / Windows 11. Politique pure de bord et de plein écran, séparée du rendu.
def is_fullscreen(style, maximized, client, screen):
    return all(abs(a - b) <= 2 for a, b in zip(client, screen))


class EdgePolicy:
    def __init__(self):
        self.away_since = 0

    def update(self, now, edge, inside, fullscreen, forced=False):
        # Viser volontairement le bord l'emporte ; une application plein écran seule ne le révèle jamais.
        if edge or forced:
            self.away_since = 0
            return True
        # Une fois révélé, le pointeur dans le dock le garde ouvert. Sans cet ordre, une application plein écran
        # refermerait le dock dès que le pointeur quitte le bord de sept pixels, et aucune icône ne serait cliquable.
        if inside:
            self.away_since = 0
            return True
        if fullscreen:
            self.away_since = 0
            return False
        if not self.away_since:
            self.away_since = now
        return False if now - self.away_since >= 0.45 else None
