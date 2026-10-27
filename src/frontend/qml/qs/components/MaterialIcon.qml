import qs.services
import qs.config

StyledText {
    color:Bridge.themeMode==="auto"?Colours.palette.m3primary:Colours.palette.m3onSurface
    property real fill
    property int grade: Colours.light ? 0 : -25

    font.family: Appearance.font.family.material
    font.pointSize: Appearance.font.size.larger
    font.variableAxes: ({
            FILL: fill.toFixed(1),
            GRAD: grade,
            opsz: fontInfo.pixelSize,
            wght: fontInfo.weight
        })
}
