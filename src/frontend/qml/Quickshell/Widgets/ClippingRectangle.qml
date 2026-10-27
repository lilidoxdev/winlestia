import QtQuick
import Qt5Compat.GraphicalEffects
Rectangle {
 id: root
 layer.enabled: true
 layer.effect: OpacityMask { maskSource: Rectangle {width:root.width;height:root.height;radius:root.radius} }
}
