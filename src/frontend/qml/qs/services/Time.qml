pragma Singleton
import QtQuick
QtObject {
 property date date: new Date()
 property Timer ticker: Timer {interval:1000;running:true;repeat:true;onTriggered: Time.date = new Date()}
 function format(pattern) {return Qt.formatDateTime(date,pattern)}
}
