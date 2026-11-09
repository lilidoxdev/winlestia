// language: C# 12, file: Coordinator.Dialogs.cs, target: Windows 11 / .NET 8 WPF, boîtes de dialogue des contrôles et raccourcis Windows
using System;
using System.Threading.Tasks;
using System.Windows;

namespace PanneauxGlass;

internal sealed partial class Coordinator
{
    internal void RunControlDialog(Window? parent, Action action) => dialogs.Scope(action);
    internal Task RunControlTask(Func<Task> action) => dialogs.ScopeAsync(action);
    // Fenêtre propriétaire des dialogues ouverts depuis l'espace : l'ancre quand il est ouvert, sinon aucune.
    Window? OpenOwner => state.Open ? Anchor : null;
    // Toutes les fenêtres modales suivent le même cycle : portée de dialogue, propriétaire, présence dans la barre en aperçu.
    void ShowModal(Func<Window> create, Window? owner, bool resetAway = true) => dialogs.Scope(() =>
    {
        var dialog = create();
        dialog.Owner = owner;
        dialog.ShowInTaskbar = preview;
        dialog.ShowDialog();
    }, resetAway);
    internal void EditControls(Window parent, bool add = false) => ShowModal(() => new ControlEditorWindow(this, add), parent);
    internal void LaunchWindowsShortcut(WindowsShortcut shortcut) => launcher.LaunchShortcut(shortcut);
}
