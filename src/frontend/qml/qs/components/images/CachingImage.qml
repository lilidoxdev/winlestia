import QtQuick
Image {property string path:""; source: Bridge.localUrl(path);asynchronous:true;sourceSize.width:width>0?Math.ceil(width*Screen.devicePixelRatio):0;sourceSize.height:height>0?Math.ceil(height*Screen.devicePixelRatio):0;fillMode:Image.PreserveAspectCrop}
