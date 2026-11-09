pragma Singleton
import QtQuick

QtObject {
    property date date: new Date()
    // Les consommateurs n'affichent que hh:mm / dd MMM : on se réveille une fois par minute, à la minute pile, au lieu de chaque seconde.
    function untilNextMinute() {
        var n = new Date();
        return 60050 - (n.getSeconds() * 1000 + n.getMilliseconds());
    }
    property Timer ticker: Timer {
        interval: Time.untilNextMinute()
        running: true
        repeat: true
        onTriggered: {
            Time.date = new Date();
            interval = Time.untilNextMinute();
        }
    }
    function format(pattern) {
        return Qt.formatDateTime(date, pattern);
    }
}
