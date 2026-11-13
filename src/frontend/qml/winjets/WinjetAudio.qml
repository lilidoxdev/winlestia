pragma Singleton
import QtQuick

// Façade du compteur partagé du pont (Bridge.audioBarsRef) : le tableau de bord et les visualiseurs passent par le même
// compteur, une seule capture, libérée à 0. Aucun état propre ici.
QtObject {
    function acquire(): void {
        Bridge.audioBarsRef(true);
    }

    function release(): void {
        Bridge.audioBarsRef(false);
    }
}
