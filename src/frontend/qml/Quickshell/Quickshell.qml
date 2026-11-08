pragma Singleton
import QtQuick

QtObject {
    function iconPath(name) {
        return Bridge.iconPath(name);
    }
    // Le serveur n'a jamais traité « launchKnown » : l'appel reste accepté mais ne fait rien.
    function execDetached(args) {
    }
}
