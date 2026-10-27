pragma Singleton
import QtQuick
QtObject {
 readonly property string icon: Bridge.weather.icon || "sunny"
 readonly property string temp: Bridge.weather.temperature || "—°C"
 readonly property string description: Bridge.weather.description || "Choisir une ville"
 function reload() {Bridge.refreshWeather()}
}
