pragma Singleton
import QtQuick
QtObject {function copyFile(source,target){return Bridge.copyFile(source,target)} function clamp(value,min,max){return Math.max(min,Math.min(max,value))}}

