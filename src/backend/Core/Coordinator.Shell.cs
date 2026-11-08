// language: C# 12, file: Coordinator.Shell.cs, target: Windows 11 / .NET 8 WPF, barre latérale, barre d'outils, barre des tâches et
// menus
using System;
using System.Linq;
using System.Windows;

namespace PanneauxGlass;

internal sealed partial class Coordinator
{
    void BuildRail() => ShellChrome.BuildRail(rail, Open);
    void BuildToolbar() => paletteChip = ShellChrome.BuildToolbar(toolbar, state, Keys.PaletteLabel,
        new ToolbarCommands(TogglePalette, ShowHome, ResetLayout, RefreshBoards, () => Settings(), Rescue, Collapse));
    internal void Open()
    {
        if (!ForwardToQt("toggle"))
            OpenView(false);
    }
    internal bool TaskbarEnabled => taskbar?.Enabled == true;
    internal (bool Running, long Samples) ShellSampling => home.ShellSampling;
    internal string ShellPage => home.SelectedPage;
    internal void ToggleTaskbarPin(Entry entry)
    {
        if (!Store.State.TaskbarPins.Remove(entry.Id))
            Store.State.TaskbarPins.Add(entry.Id);
        Store.Save();
        taskbar?.Refresh();
    }
    internal void SetTaskbar(bool enabled)
    {
        bool applied = !enabled || taskbar?.Enable() == true;
        if (!enabled)
            taskbar?.Disable();
        Store.State.ReplaceTaskbar = enabled && applied;
        Store.Save();
        if (!applied)
            Message("Le secours de la barre n’a pas démarré. La barre Windows reste disponible.");
    }
    // L'arrêt du moteur rétablit la barre Windows sans réécrire la préférence de l'utilisateur :
    // un frontend planté ne doit pas décider que le dock est indésirable pour la suite.
    internal void SuspendTaskbar() => taskbar?.Disable();
    internal void ShowShellPage(string id)
    {
        if (QtShell != null)
        {
            QtShell.Send(id == "dashboard" ? "toggle" : id == "media" ? "media" : id == "performance" ? "performance" : "access");
            return;
        }
        if (id == "files")
        {
            ShowWorkspace();
            return;
        }
        if (!state.Open)
            Open();
        if (state.Detailed)
            ShowHome();
        state.ControlsExpanded = state.PcExpanded = false;
        home.SelectPage(id);
        Arrange(true);
        Native.Raise(home);
    }
    internal void LaunchTarget(string target) => launcher.LaunchTarget(target);
    internal void TaskbarMenu(Window parent, FrameworkElement anchor)
        => GlassMenu.Open(this, parent, anchor, "Ta barre des tâches", new[]
        {
            new MenuAction("Tableau de bord", "\uE8A9", () => ShowShellPage("dashboard")),
            new MenuAction("Applications et fichiers", "\uE8B7", () => ShowWorkspace()),
            new MenuAction("Personnaliser…", "\uE713", () => Settings(), Separator: true),
            new MenuAction("Rétablir la barre Windows", "\uE777", () => SetTaskbar(false)),
            new MenuAction("Quitter Panneaux", "\uE711", () => Application.Current.Shutdown())
        }, true);
    internal void TaskbarAppMenu(Window parent, FrameworkElement anchor, Entry entry, bool pinned)
        => GlassMenu.Open(this, parent, anchor, entry.Name, new[]
        {
            new MenuAction("Ouvrir une nouvelle fenêtre", "\uE8A7", () => Launch(entry)),
            new MenuAction(pinned ? "Détacher de la barre" : "Épingler à la barre", "\uE718", () => TogglePinned(entry, pinned)),
            new MenuAction("Tout mon espace", "\uE8B7", () => ShowWorkspace(), Separator: true)
        }, true);
    // Le menu épingle l'identifiant du catalogue, à défaut la cible pour les accès libres absents du catalogue.
    void TogglePinned(Entry entry, bool pinned)
    {
        string id = Store.Entries.Any(e => e.Id == entry.Id) ? entry.Id : entry.Target;
        if (pinned)
            Store.State.TaskbarPins.Remove(entry.Id);
        else if (!string.IsNullOrWhiteSpace(id) && !Store.State.TaskbarPins.Contains(id))
            Store.State.TaskbarPins.Add(id);
        Store.Save();
        taskbar?.Refresh();
    }
    internal void ToggleAlwaysOnTop(IntPtr? handle = null)
    {
        IntPtr target = handle ?? Native.GetForegroundWindow();
        if (target != IntPtr.Zero && Native.Owns(target))
            target = state.LastForeign;
        if (target == IntPtr.Zero || !Native.IsWindow(target))
        {
            ShowToast("Aucune fenêtre à épingler");
            return;
        }
        if (!Native.ToggleTopmost(target, out bool topmost))
        {
            ShowToast("Windows a refusé ce changement");
            return;
        }
        string title = Native.Title(target);
        if (title.Length > 38)
            title = title[..37] + "…";
        ShowToast((topmost ? "Au premier plan · " : "Rangée normalement · ") + title);
    }
    internal void ApplyClipboardSettings() => clipboard.ApplySettings();
    internal void ToggleNotifications()
    {
        bool next = !SystemTools.NotificationsOn;
        SystemTools.SetNotifications(next);
        ShowToast(next ? "Notifications rétablies" : "Notifications coupées");
    }
    internal void RestartExplorer()
    {
        if (!Confirm("Redémarrer l’Explorateur Windows ?\n\n"
                + "La barre des tâches et le Bureau vont clignoter, tes fenêtres restent ouvertes."))
            return;
        CloseDialogs();
        SystemTools.RestartExplorer();
        ShowToast("Explorateur redémarré");
    }
}
