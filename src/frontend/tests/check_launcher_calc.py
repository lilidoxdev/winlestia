# Python 3.12 / Windows. Vérifie l'analyse de la calculatrice du lanceur, l'affichage selon la locale
# et la recherche d'emojis embarquée : python -I tests/check_launcher_calc.py
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from search import calculate, emoji_choices, palette_search

cases = [
    ('25/2', '12,5'),
    ('25:2', '12,5'),
    ('25 divisé par 2', '12,5'),
    ('25 DIVISE PAR 2', '12,5'),
    ('24*7', '168'),
    ('12,5*2', '25'),
    ('3 fois 4', '12'),
    ('5 plus 3', '8'),
    ('5 moins 3', '2'),
    ('>calc 1+1', '2'),
    ('firefox', None),
    ('12:30 reunion', None),
    ('12:30', None),
    ('plus', None),
    (r'C:\Users\2', None),
    ('2 plus', None),
    ('os.system(1)', None),
    ('__import__("os")', None),
    ('10 fois', None),
]
for query, expected in cases:
    got = calculate(query)
    assert got == expected, (query, got, expected)
assert calculate('25/2', 'en') == '12.5' and calculate('25/2', 'es') == '12,5'
assert not any(
    r['kind'] == 'calc'
    for r in palette_search('firefox', {'apps': [{'name': 'Firefox', 'category': 'x', 'board': 'b'}]})
)
cat = [r['id'] for r in emoji_choices('chat')]
assert '🐱' in cat and '🐈' in cat
assert (
    '🍕' in [r['id'] for r in emoji_choices('pizza')]
    and '🔥' in [r['id'] for r in emoji_choices('feu')]
    and len(emoji_choices('')) >= 400
)
print('ok', len(emoji_choices('')))
