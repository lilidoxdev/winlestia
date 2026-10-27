import QtQuick
Image {property string path:""; source: Bridge.localUrl(path);asynchronous:true;fillMode:Image.PreserveAspectCrop}
