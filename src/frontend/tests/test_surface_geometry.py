# Python 3.12 / test_surface_geometry.py / Silhouette regression checks independent of Windows composition.
import sys,unittest
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from PySide6.QtCore import QPointF
from surface_geometry import shell_path,path_commands

class SurfaceGeometryTests(unittest.TestCase):
    def test_top_and_bottom_connections_have_no_gap(self):
        path=shell_path(1000,700,20,(200,20,600,280,True),(200,612,600,68,True))
        for point in ((500,19.9),(500,20.1),(500,679.9),(500,680.1)):
            self.assertTrue(path.contains(QPointF(*point)))
    def test_union_never_fills_unused_desktop(self):
        path=shell_path(1000,700,20,(200,20,600,280,True),(200,612,600,68,True))
        for point in ((100,350),(500,400),(900,350)):
            self.assertFalse(path.contains(QPointF(*point)))
    def test_disabled_frame_keeps_only_open_panels(self):
        path=shell_path(1000,700,0,(200,0,600,280,False),None)
        self.assertFalse(path.contains(QPointF(1,350)));self.assertTrue(path.contains(QPointF(500,100)))
    def test_curves_survive_native_serialization(self):
        commands=path_commands(shell_path(1000,700,20),1.5)
        self.assertTrue(any(command[0]==2 for command in commands))
        self.assertLessEqual(max(abs(v) for command in commands for v in command[1:]),1500)

if __name__=='__main__':unittest.main()
