pragma Singleton
import QtQuick
QtObject {
 readonly property real surfaceOpacity:Bridge.glassEnabled?Bridge.glassTintOpacity:1
 // One opaque base, one alpha: the frame, the dashboard and the dock all read surfaceColor.
 readonly property color surfaceBase:Bridge.adaptivePalette.m3surface??(Bridge.dark?"#101518":"#fff8f6")
 readonly property color surfaceColor:Qt.alpha(surfaceBase,surfaceOpacity)
 readonly property bool light: !Bridge.dark
 property QtObject palette: QtObject {property color m3background: glassLayer(Bridge.adaptivePalette.m3background ?? (Bridge.dark ? "#101518" : "#fff8f6"),1)
property color m3surface: glassLayer(Bridge.adaptivePalette.m3surface ?? (Bridge.dark ? "#101518" : "#fff8f6"),1)
property color m3onSurface: Bridge.adaptivePalette.m3onSurface ?? (Bridge.dark ? "#e5ebef" : "#261916")
property color m3surfaceContainer: glassLayer(Bridge.adaptivePalette.m3surfaceContainer ?? (Bridge.dark ? "#182025" : "#fae8e3"),0.65)
property color m3surfaceContainerLow: glassLayer(Bridge.adaptivePalette.m3surfaceContainerLow ?? (Bridge.dark ? "#151c21" : "#fff0eb"),0.5)
property color m3surfaceContainerHigh: glassLayer(Bridge.adaptivePalette.m3surfaceContainerHigh ?? (Bridge.dark ? "#242d33" : "#f5e0d9"),0.8)
property color m3surfaceContainerHighest: glassLayer(Bridge.adaptivePalette.m3surfaceContainerHighest ?? (Bridge.dark ? "#303b42" : "#eed9d2"),1)
property color m3primary: Bridge.adaptivePalette.m3primary ?? (Bridge.dark ? "#afd4f0" : "#895143")
property color m3secondary: Bridge.adaptivePalette.m3secondary ?? (Bridge.dark ? "#c3c8d0" : "#775b52")
property color m3tertiary: Bridge.adaptivePalette.m3tertiary ?? (Bridge.dark ? "#c7cea0" : "#68613c")
property color m3primaryContainer: glassLayer(Bridge.adaptivePalette.m3primaryContainer ?? (Bridge.dark ? "#304b60" : "#ffdace"),0.85)
property color m3secondaryContainer: glassLayer(Bridge.adaptivePalette.m3secondaryContainer ?? (Bridge.dark ? "#404851" : "#f5dcd3"),0.85)
property color m3onPrimary: Bridge.adaptivePalette.m3onPrimary ?? (Bridge.dark ? "#12354a" : "#ffffff")
property color m3onSecondary: Bridge.adaptivePalette.m3onSecondary ?? (Bridge.dark ? "#253039" : "#ffffff")
property color m3onPrimaryContainer: Bridge.adaptivePalette.m3onPrimaryContainer ?? (Bridge.dark ? "#d1e7fa" : "#351006")
property color m3onSecondaryContainer: Bridge.adaptivePalette.m3onSecondaryContainer ?? (Bridge.dark ? "#e3e6ed" : "#2b1811")
property color m3onSurfaceVariant: Bridge.adaptivePalette.m3onSurfaceVariant ?? (Bridge.dark ? "#bdc7cf" : "#54433e")
property color m3outline: Bridge.adaptivePalette.m3outline ?? (Bridge.dark ? "#8897a2" : "#85726a")
property color m3outlineVariant: Bridge.adaptivePalette.m3outlineVariant ?? (Bridge.dark ? "#3c4a54" : "#d6c3bc")
property color m3error: Bridge.adaptivePalette.m3error ?? (Bridge.dark ? "#ffb4ab" : "#b3261e")
property color m3onError: Bridge.adaptivePalette.m3onError ?? (Bridge.dark ? "#690005" : "#ffffff")
property color m3shadow: Bridge.adaptivePalette.m3shadow ?? (Bridge.dark ? "#000000" : "#000000")
property color m3scrim: Bridge.adaptivePalette.m3scrim ?? (Bridge.dark ? "#000000" : "#000000")
property color m3inverseSurface: Bridge.adaptivePalette.m3inverseSurface ?? (Bridge.dark ? "#e0e3e6" : "#352f2c")
property color m3inverseOnSurface: Bridge.adaptivePalette.m3inverseOnSurface ?? (Bridge.dark ? "#2e3134" : "#fff0eb")
property color m3inversePrimary: Bridge.adaptivePalette.m3inversePrimary ?? (Bridge.dark ? "#40657f" : "#ffb5a0")
property color m3onTertiary: Bridge.adaptivePalette.m3onTertiary ?? (Bridge.dark ? "#263640" : "#f5ded5")}
 readonly property var tPalette: palette
 function glassLayer(color,strength) {return Bridge.glassEnabled?Qt.alpha(color,Math.min(.06,Bridge.glassOpacity*.2*strength)):color}
 function layer(color,level) {return Qt.tint(color,Qt.alpha(palette.m3primary,Math.min(.18,level*.025)))}
 function onColour(color) {return palette.m3onSurface}
}
