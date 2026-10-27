import QtQuick
QtObject { required property var service; Component.onCompleted: { if(service)service.refCount++ } Component.onDestruction: { if(service)service.refCount-- } }
