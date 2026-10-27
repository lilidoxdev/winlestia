# Python 3.12 / Windows. Regression checks for dock activation, fullscreen and local launcher queries.
import sys, unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from interaction import EdgePolicy,is_fullscreen
from search import calculate,palette_search

class DockTests(unittest.TestCase):
    def test_maximized_desktop_window_is_not_fullscreen(self):
        self.assertFalse(is_fullscreen(0x00C00000,True,(-8,-8,3448,1448),(0,0,3440,1440)))
    def test_titlebar_client_area_is_not_fullscreen(self):
        self.assertFalse(is_fullscreen(0,False,(0,32,3440,1438),(0,0,3440,1440)))
    def test_borderless_fullscreen(self):
        self.assertTrue(is_fullscreen(0,False,(0,0,3440,1440),(0,0,3440,1440)))
    def test_negative_monitor_coordinates(self):
        self.assertTrue(is_fullscreen(0,False,(-1920,0,0,1080),(-1920,0,0,1080)))
    def test_intentional_bottom_edge_reveals_during_fullscreen(self):
        self.assertTrue(EdgePolicy().update(1,True,False,True))
    def test_fullscreen_alone_does_not_reveal(self):
        self.assertFalse(EdgePolicy().update(1,False,False,True))
    def test_pointer_inside_keeps_dock_open(self):
        self.assertTrue(EdgePolicy().update(1,False,True,False))
    def test_pointer_inside_keeps_dock_open_over_fullscreen(self):
        self.assertTrue(EdgePolicy().update(1,False,True,True))
    def test_leave_delay_and_reentry(self):
        policy=EdgePolicy();self.assertIsNone(policy.update(1,False,False,False));self.assertFalse(policy.update(1.5,False,False,False));self.assertTrue(policy.update(1.6,True,False,False))
    def test_windows_key_reveal(self):
        self.assertTrue(EdgePolicy().update(1,False,False,True,True))

class SearchTests(unittest.TestCase):
    def test_calculation_precedence(self):self.assertEqual(calculate('24*7+3'),'171')
    def test_typographic_operators(self):self.assertEqual(calculate('24×7÷2'),'84')
    def test_calculation_prefix(self):self.assertEqual(calculate('>calc (24+6)/2'),'15')
    def test_no_code_execution(self):self.assertIsNone(calculate('__import__("os").system("calc")'))
    def test_bounded_exponent(self):self.assertIsNone(calculate('9**9999999'))
    def test_division_by_zero(self):self.assertIsNone(calculate('1/0'))
    def test_accent_insensitive_app_search(self):
        results=palette_search('editeur',{'apps':[{'id':'1','name':'Éditeur','category':'Création','board':'apps'}]});self.assertEqual(results[0]['id'],'1')
    def test_command_prefix(self):self.assertTrue(any(r['id']=='controls' for r in palette_search('> volume',{})))
    def test_navigation_result_is_not_a_shell_command(self):
        result=next(r for r in palette_search('> documents',{}) if r['id']=='workspace:documents');self.assertEqual(result['kind'],'navigate')
    def test_unknown_query_becomes_browser_search(self):self.assertEqual(palette_search('unknownxyz',{})[0]['kind'],'web')
    def test_quick_tabs_remain_available(self):self.assertEqual(palette_search('',{'quickTabs':[{'id':'q','name':'Capture'}]})[0]['kind'],'quickTab')

if __name__=='__main__':unittest.main()
