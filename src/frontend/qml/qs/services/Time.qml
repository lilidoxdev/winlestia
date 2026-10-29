pragma Singleton
import QtQuick
QtObject {
 property date date: new Date()
 // Consumers only show hh:mm / dd MMM: wake once a minute, on the minute boundary, instead of every second.
 function untilNextMinute() {var n = new Date(); return 60050 - (n.getSeconds() * 1000 + n.getMilliseconds())}
 property Timer ticker: Timer {interval:Time.untilNextMinute();running:true;repeat:true;onTriggered: {Time.date = new Date(); interval = Time.untilNextMinute()}}
 function format(pattern) {return Qt.formatDateTime(date,pattern)}
}
