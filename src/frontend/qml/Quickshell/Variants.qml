import QtQuick
import QtQml.Models
Instantiator {
 property var instances: []
 onObjectAdded: (index, object) => {let list=instances.slice();list.splice(index,0,object);instances=list}
 onObjectRemoved: (index, object) => {let list=instances.slice();list.splice(index,1);instances=list}
}
