import QtQuick
QtObject {
 property string title:"";property string filterLabel:"";property var filters:[]
 signal accepted(string path)
 function open() { const result=Bridge.pickAvatar(); if(result)accepted(result) }
}
