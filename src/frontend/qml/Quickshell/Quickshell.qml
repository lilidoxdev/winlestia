pragma Singleton
import QtQuick
QtObject {
 function iconPath(name) { return Bridge.iconPath(name) }
 function execDetached(args) { Bridge.action("launchKnown", {args:args}) }
}
