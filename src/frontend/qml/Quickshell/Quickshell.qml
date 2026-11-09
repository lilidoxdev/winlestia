pragma Singleton
import QtQuick

QtObject {
    function iconPath(name) {
        return Bridge.iconPath(name);
    }
    // Appel accepté mais volontairement sans effet (Wrapper.qml l'appelle encore).
    function execDetached(args) {
    }
}
