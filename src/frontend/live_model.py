# Python 3.12 / live_model.py / Qt 6. Stable delegates while native audio values change.
from PySide6.QtCore import QAbstractListModel, QModelIndex, Qt, QByteArray

class LiveListModel(QAbstractListModel):
    role = int(Qt.UserRole) + 1
    def __init__(self, parent=None):
        super().__init__(parent); self.rows = []
    def roleNames(self): return {self.role: QByteArray(b'modelData')}
    def rowCount(self, parent=QModelIndex()): return 0 if parent.isValid() else len(self.rows)
    def data(self, index, role):
        return self.rows[index.row()] if index.isValid() and role == self.role else None
    def sync(self, rows):
        keys = {r['id'] for r in rows}
        for i in range(len(self.rows)-1, -1, -1):
            if self.rows[i]['id'] not in keys:
                self.beginRemoveRows(QModelIndex(), i, i); self.rows.pop(i); self.endRemoveRows()
        for i, row in enumerate(rows):
            old = next((j for j,r in enumerate(self.rows) if r['id']==row['id']), -1)
            if old < 0:
                self.beginInsertRows(QModelIndex(), i, i); self.rows.insert(i,row); self.endInsertRows()
            elif old != i:
                self.beginMoveRows(QModelIndex(),old,old,QModelIndex(),i if old>i else i+1)
                self.rows.insert(i,self.rows.pop(old)); self.endMoveRows()
            if self.rows[i] != row:
                self.rows[i]=row; self.dataChanged.emit(self.index(i),self.index(i),[self.role])
