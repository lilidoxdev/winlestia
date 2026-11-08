// language: C# 12, file: ShellState.cs, target: Windows 11 / .NET 8, état partagé de l'espace (ouvert, repli, dialogues)
using System;

namespace PanneauxGlass;

// Ces indicateurs étaient des champs privés du Coordinator ; le dialogue, la boucle d'horloge et la barre d'outils les lisent aussi.
// Un objet d'état unique évite de leur donner une référence au Coordinator entier.
internal sealed class ShellState
{
    internal bool Open
    {
        get; set;
    }
    internal bool Closing
    {
        get; set;
    }
    internal bool Detailed
    {
        get; set;
    }
    internal bool Interacting
    {
        get; set;
    }
    internal bool SettingsOpen
    {
        get; set;
    }
    internal bool PaletteOpen
    {
        get; set;
    }
    internal bool ControlsExpanded
    {
        get; set;
    }
    internal bool PcExpanded
    {
        get; set;
    }
    internal bool PopupOpen
    {
        get; set;
    }
    internal bool Pinned
    {
        get; set;
    }
    internal bool FavoritesOnly
    {
        get; set;
    }
    internal DateTime AwaySince { get; set; } = DateTime.MinValue;
    internal DateTime EdgeSince { get; set; } = DateTime.MinValue;
    internal DateTime RailAway { get; set; } = DateTime.MinValue;
    // Dernière fenêtre étrangère au premier plan : cible de l'épinglage quand le raccourci est pressé depuis Panneaux.
    internal IntPtr LastForeign
    {
        get; set;
    }
}
