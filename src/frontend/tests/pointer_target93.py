import sys,json
from pathlib import Path
from PySide6.QtWidgets import QApplication,QWidget
from PySide6.QtCore import Qt,QTimer
folder=Path(sys.argv[1]);app=QApplication([])
class Target(QWidget):
    count=0
    def mousePressEvent(self,event):
        self.count+=1;(folder/'click-count.txt').write_text(str(self.count))
w=Target();w.setWindowTitle('WinLestia pointer regression target');w.setWindowFlags(Qt.Window|Qt.FramelessWindowHint|Qt.WindowStaysOnTopHint);w.setGeometry(0,0,2300,1200);w.setStyleSheet('background:#38586b');w.show();(folder/'click-count.txt').write_text('0');(folder/'target-ready.json').write_text(json.dumps({'hwnd':int(w.winId())}));QTimer.singleShot(12000,app.quit);app.exec()

