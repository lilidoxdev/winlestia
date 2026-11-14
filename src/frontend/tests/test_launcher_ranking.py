# Python 3.12 / Windows. Classement frécence + fuzzy des applications et conversions d'unités du lanceur.
import sys, unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from search import MAX_USAGE_BONUS, TIER_MAX_PENALTY, app_score, calculate, convert_units, fold, palette_search, usage_bonus

APPS = [
    {'id': 'vsc', 'name': 'Visual Studio Code', 'category': 'Dev', 'board': 'apps'},
    {'id': 'vs', 'name': 'Visual Studio', 'category': 'Dev', 'board': 'apps'},
    {'id': 'fire', 'name': 'Firefox', 'category': 'Web', 'board': 'apps'},
    {'id': 'fi', 'name': 'Fichiers', 'category': 'Outils', 'board': 'apps'},
    {'id': 'calc', 'name': 'Calculatrice', 'category': 'Outils', 'board': 'apps'},
    {'id': 'ec', 'name': 'Éditeur de Code', 'category': 'Dev', 'board': 'apps'},
    {'id': 'ca', 'name': 'Caméra', 'category': 'Média', 'board': 'apps'},
]


def names(query, counts=None, apps=APPS):
    out = palette_search(query, {'apps': apps, 'launchCounts': counts or {}}, 'fr')
    return [r['name'] for r in out if r['kind'] == 'app']


def score(query, name, count=0, recent=-1):
    needle = fold(query)
    return app_score(needle, needle.split(), {'name': name, 'recent': recent}, count)


class RankingTests(unittest.TestCase):
    def test_tier_order(self):
        tiers = [
            score('code', 'Code'),
            score('code', 'Code Editor'),
            score('code', 'Visual Code'),
            score('vsc', 'Visual Studio Code'),
            score('ode', 'Code'),
            score('cde', 'Code'),
        ]
        self.assertEqual(tiers, sorted(tiers, reverse=True))
        self.assertEqual(len(set(int(t) for t in tiers)), len(tiers))

    def test_acronym(self):
        self.assertEqual(names('vsc')[0], 'Visual Studio Code')

    def test_prefix_before_substring(self):
        self.assertEqual(names('fi')[:2], ['Firefox', 'Fichiers'])  # même palier : nom le plus court d'abord
        self.assertEqual(names('ox')[0], 'Firefox')
        self.assertEqual(names('studio')[0], 'Visual Studio')

    def test_exact_first(self):
        self.assertEqual(names('visual studio')[0], 'Visual Studio')

    def test_fuzzy_subsequence_and_gap_penalty(self):
        self.assertIsNotNone(score('frfx', 'Firefox'))
        self.assertGreater(score('ffx', 'Firefox'), score('ffx', 'Fire fox is far away from x'.replace('x is', 'x i')))
        self.assertIsNone(score('xyz', 'Firefox'))
        self.assertIn('Firefox', names('frfx'))
        self.assertIsNone(score('fx', 'F' + 'a' * 30 + 'x'))

    def test_accents_and_case(self):
        self.assertEqual(names('CAMERA'), ['Caméra'])
        self.assertEqual(names('éditeur')[0], 'Éditeur de Code')
        self.assertEqual(names('editeur')[0], 'Éditeur de Code')
        self.assertEqual(names('calculatrice'), ['Calculatrice'])

    def test_category_still_matches(self):
        self.assertIn('Caméra', names('media'))

    def test_usage_reorders_within_tier(self):
        self.assertEqual(names('vis')[0], 'Visual Studio')
        self.assertEqual(names('vis', {'vsc': 30})[0], 'Visual Studio Code')

    def test_usage_lifts_frequent_fuzzy(self):
        apps = [{'id': 'a', 'name': 'Alpha Beta Gamma'}, {'id': 'b', 'name': 'Aqua Bay Garden'}]
        # « abg » est l'acronyme des deux noms ; l'usage décide.
        self.assertEqual(names('abg', {'a': 20}, apps)[0], 'Alpha Beta Gamma')
        self.assertEqual(names('abg', {'b': 20}, apps)[0], 'Aqua Bay Garden')

    def test_bonus_cap(self):
        self.assertEqual(usage_bonus(10**9, 0), MAX_USAGE_BONUS)
        self.assertLessEqual(usage_bonus(10**9, 0), MAX_USAGE_BONUS)
        self.assertEqual(usage_bonus(0, -1), 0)
        self.assertGreater(usage_bonus(5, -1), usage_bonus(1, -1))
        self.assertGreater(usage_bonus(0, 0), usage_bonus(0, 40))
        self.assertEqual(usage_bonus('x', 0), 0)

    def test_fuzzy_never_beats_exact_even_with_huge_usage(self):
        exact = score('code', 'Code', 0, -1)
        fuzzy = score('cde', 'Code Editor Pro Max Ultimate Deluxe', 10**9, 0)
        self.assertGreater(exact, fuzzy)
        apps = [{'id': 'x', 'name': 'Cobalt Dev Engine'}, {'id': 'y', 'name': 'Cde'}]
        self.assertEqual(names('cde', {'x': 10**9}, apps)[0], 'Cde')

    def test_tiers_never_cross_with_max_bonus(self):
        worst_higher = {
            'prefix': score('ab', 'Ab' + 'x' * 80),
            'word': score('ab', 'zz ' + 'ab' + 'x' * 80),
            'substring': score('ab', 'zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzab'),
        }
        best_lower = {
            'word': score('ab', 'zz ab', 10**9, 0),
            'substring': score('ab', 'zab', 10**9, 0),
            'fuzzy': score('abc', 'axbxc', 10**9, 0),
        }
        self.assertGreater(worst_higher['prefix'], best_lower['word'])
        self.assertGreater(worst_higher['word'], best_lower['substring'])
        self.assertGreater(worst_higher['substring'], best_lower['fuzzy'])

    def test_every_adjacent_tier_pair_is_separated(self):
        import search
        bases = sorted(TIER_MAX_PENALTY, reverse=True)
        self.assertEqual(len(bases), 7)
        self.assertEqual(set(bases), {getattr(search, n) for n in dir(search) if n.startswith('TIER_') and n != 'TIER_MAX_PENALTY'})
        for high, low in zip(bases, bases[1:]):
            self.assertGreater(high - TIER_MAX_PENALTY[high], low + MAX_USAGE_BONUS, (high, low))
        # Cas concrets aux frontières les plus serrées : mot (pire) contre acronyme (meilleur, usage maximal).
        word = score('ab', 'zz ' + 'ab' + 'x' * 80)
        acronym = score('abg', 'Alpha Beta Gamma', 10**9, 0)
        self.assertGreater(word, acronym)

    def test_bad_launch_counts_ignored(self):
        out = palette_search('fire', {'apps': APPS, 'launchCounts': 'oops'}, 'fr')
        self.assertEqual(out[0]['name'], 'Firefox')
        out = palette_search('fire', {'apps': APPS}, 'fr')
        self.assertEqual(out[0]['name'], 'Firefox')

    def test_limit_eight_apps(self):
        apps = [{'id': str(i), 'name': 'App %d' % i} for i in range(30)]
        self.assertEqual(len(names('app', apps=apps)), 8)


class ConversionTests(unittest.TestCase):
    def test_examples_french(self):
        self.assertEqual(calculate('12 km en mi', 'fr'), '7,45645 mi')
        self.assertEqual(calculate('5 kg in lb', 'fr'), '11,0231 lb')
        self.assertEqual(calculate('1.5 h en min', 'fr'), '90 min')
        self.assertEqual(calculate('2 go en mo', 'fr'), '2000 mo')
        self.assertEqual(calculate('30 cm in in', 'fr'), '11,811 in')

    def test_english_decimal_point(self):
        self.assertEqual(convert_units('12 km to mi', 'en'), '7.45645 mi')

    def test_separators(self):
        for sep in ('en', 'in', 'to', '→'):
            self.assertEqual(convert_units('1 km %s m' % sep), '1000 m', sep)
        self.assertEqual(convert_units('1 km→m'), '1000 m')
        self.assertEqual(convert_units('1km en m'), '1000 m')
        self.assertEqual(convert_units('2,5 km EN M'), '2500 M')

    def test_large_results_stay_exact(self):
        self.assertEqual(convert_units('1 gio en o', 'en'), '1073741824 o')
        self.assertEqual(convert_units('1 tio en o', 'en'), '1099511627776 o')
        self.assertEqual(convert_units('2 go en o', 'en'), '2000000000 o')
        self.assertEqual(convert_units('1 mi en mm', 'en'), '1609344 mm')
        self.assertEqual(convert_units('0.1 km en m', 'en'), '100 m')
        self.assertEqual(convert_units('0.3 m en cm', 'en'), '30 cm')
        self.assertEqual(convert_units('1.5 mi en m', 'en'), '2414.02 m')
        self.assertEqual(convert_units('1234.5678 km en m', 'en'), '1234567.8 m')

    def test_thousands_and_decimal_separators(self):
        self.assertEqual(convert_units('1,000 km in m', 'en'), '1000000 m')
        self.assertEqual(convert_units('1,000 km en m', 'fr'), '1000 m')
        self.assertEqual(convert_units('1,5 km en m', 'fr'), '1500 m')
        self.assertEqual(convert_units('1.5 km in m', 'en'), '1500 m')
        self.assertEqual(convert_units('1 mi to km', 'en'), '1.60934 km')
        for lang in ('fr', 'es', 'de', 'it', 'pt', 'ru', 'nl', 'pl'):
            self.assertEqual(convert_units('1 mi to km', lang), '1,60934 km', lang)
        self.assertEqual(calculate('1/4', 'de'), '0,25')
        self.assertEqual(calculate('1/4', 'en'), '0.25')

    def test_ordinary_queries_fall_through(self):
        for query in ('word in excel', 'go to', '5 in', 'to do', 'paint in 3d', '5 to', '12 foo en bar', '5 km in', 'in 5 km'):
            self.assertIsNone(convert_units(query), query)
            self.assertIsNone(calculate(query), query)

    def test_temperature(self):
        self.assertEqual(convert_units('100 f to c', 'en'), '37.7778 c')
        self.assertEqual(convert_units('0 c en f'), '32 f')
        self.assertEqual(convert_units('100 °C en °F'), '212 °F')
        self.assertEqual(convert_units('0 k en c'), '-273,15 c')
        self.assertEqual(convert_units('-40 c en f'), '-40 f')
        self.assertEqual(convert_units('20 celsius en kelvin'), '293,15 kelvin')
        self.assertIsNone(convert_units('-300 c en f'))
        self.assertIsNone(convert_units('-1 k en c'))
        self.assertIsNone(convert_units('10 c en km'))

    def test_categories(self):
        self.assertEqual(convert_units('1 mph en km/h', 'en'), '1.60934 km/h')
        self.assertEqual(convert_units('36 km/h en m/s'), '10 m/s')
        self.assertEqual(convert_units('1 gal en l'), '3,78541 l')
        self.assertEqual(convert_units('1 ha en m²'), '10000 m²')
        self.assertEqual(convert_units('1 acre en m2'), '4046,86 m2')
        self.assertEqual(convert_units('1 gio en mio'), '1024 mio')
        self.assertEqual(convert_units('1 to en go'), '1000 go')
        self.assertEqual(convert_units('2 jours en h'), '48 h')
        self.assertEqual(convert_units('1 pied en cm'), '30,48 cm')
        self.assertEqual(convert_units('1 livre en g'), '453,592 g')
        self.assertEqual(convert_units('1 tonne en kg'), '1000 kg')
        self.assertEqual(convert_units('1 mile en yards'), '1760 yards')

    def test_accents_and_case(self):
        self.assertEqual(convert_units('1 Heure en Minutes'), '60 Minutes')
        self.assertEqual(convert_units('1 m3 en L'), '1000 L')

    def test_invalid(self):
        for text in ('12 km en kg', '12 foo en mi', '12 km en', 'km en mi', '12 km en foo', '', 'hello', '1 km en',
                     '12 en mi', '1e999 km en m', '--1 km en m', '1 km en m en cm'):
            self.assertIsNone(convert_units(text), text)
        self.assertIsNone(convert_units('1 km en m' + ' ' * 200))

    def test_huge_values_rejected(self):
        self.assertIsNone(convert_units('1e99 km en mm'))

    def test_small_and_large_not_exponent(self):
        self.assertEqual(convert_units('1 mm en km'), '0,000001 km')
        self.assertEqual(convert_units('1 to en o'), '1000000000000 o')

    def test_palette_entry_is_copyable_calc(self):
        out = palette_search('12 km en mi', {}, 'fr')
        self.assertEqual(out[0]['kind'], 'calc')
        self.assertEqual(out[0]['name'], '= 7,45645 mi')
        self.assertEqual(out[0]['name'][2:], '7,45645 mi')

    def test_calc_prefix(self):
        self.assertEqual(calculate('>calc 1 km en m', 'en'), '1000 m')


class CalculatorRegressionTests(unittest.TestCase):
    def test_existing_expressions(self):
        self.assertEqual(calculate('2+2', 'en'), '4')
        self.assertEqual(calculate('2+2', 'fr'), '4')
        self.assertEqual(calculate('10/4', 'fr'), '2,5')
        self.assertEqual(calculate('10/4', 'en'), '2.5')
        self.assertEqual(calculate('2^10', 'en'), '1024')
        self.assertEqual(calculate('(1+2)*3', 'en'), '9')
        self.assertEqual(calculate('3 × 4', 'en'), '12')
        self.assertEqual(calculate('>calc 7 % 4', 'en'), '3')

    def test_non_expressions_unchanged(self):
        for text in ('firefox', '12', '12:30', '12 km', '5 in', '3 to', 'to do 5'):
            self.assertIsNone(calculate(text), text)


if __name__ == '__main__':
    unittest.main()
