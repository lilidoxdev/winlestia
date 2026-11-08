// language: C# 12, file: Coordinator.Views.cs, target: Windows 11 / .NET 8 WPF, ouverture, bascule et repli des vues
using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace PanneauxGlass;

internal sealed partial class Coordinator
{
    void OpenView(bool full, bool showControls = false, bool showPc = false)
    {
        if (disposed)
            return;
        if (state.Closing)
            CollapseImmediately();
        if (state.Open)
        {
            if (state.Detailed != full)
                SwitchView(full, null);
            else
                Native.Raise(full ? toolbar : home);
            return;
        }
        Native.GetCursorPos(out var mouse);
        currentScreen = Forms.Screen.FromPoint(new System.Drawing.Point(mouse.X, mouse.Y));
        dismissal.SetActive(false);
        state.Open = true;
        state.Detailed = full;
        state.ControlsExpanded = showControls;
        state.PcExpanded = showPc;
        if (state.ControlsExpanded && state.PcExpanded && !FitsBoth())
            state.PcExpanded = false;
        rail.HideImmediately();
        state.AwaySince = DateTime.MinValue;
        if (!full)
            home.Reset();
        PinWindow.SpaceOpen(true, ToolbarHandle);
        Arrange(true);
        var focus = full ? (Window)toolbar : state.ControlsExpanded ? controls : state.PcExpanded ? pc : home;
        Native.Raise(focus);
        PinWindow.EnsureAbove();
        dismissal.SetActive(true);
        Store.Log(full ? "Ouverture · complète" : "Ouverture · accueil");
    }
    internal void ShowHome()
    {
        if (ForwardToQt("toggle"))
            return;
        if (state.Open)
            SwitchView(false, null);
        else
            Open();
    }
    internal void ShowWorkspace(string? board = null)
    {
        if (ForwardToQt("workspace:" + (board ?? "apps")))
            return;
        if (state.Open)
            SwitchView(true, board);
        else
            OpenView(true);
    }
    void SwitchView(bool full, string? board)
    {
        dismissal.SetActive(false);
        GlassMenu.DismissActive();
        state.PopupOpen = false;
        foreach (var window in Glass)
            window.HideImmediately();
        websites.HideImmediately();
        state.Detailed = full;
        state.ControlsExpanded = state.PcExpanded = false;
        state.AwaySince = DateTime.MinValue;
        if (!full)
            home.Reset();
        Arrange(true);
        var active = full ? (Window?)Boards.FirstOrDefault(b => b.Model.Id == board) ?? toolbar : home;
        active.Activate();
        dismissal.SetActive(true);
        Store.Log(full ? "Vue complète" : "Vue accueil");
    }
    // Préparation commune aux panneaux latéraux : accueil ouvert, clics extérieurs suspendus. Faux si le frontend Qt a pris la main.
    bool PrepareSidePanel(string qtCommand)
    {
        if (ForwardToQt(qtCommand))
            return false;
        if (!state.Open)
            Open();
        if (state.Detailed)
            ShowHome();
        dismissal.SetActive(false);
        GlassMenu.DismissActive();
        return true;
    }
    // Un panneau déplié reçoit le focus clavier ; replié, l'accueil le récupère.
    void FocusSidePanel(GlassWindow panel, bool expanded)
    {
        Arrange(true);
        (expanded ? panel : (Window)home).Activate();
        if (expanded)
        {
            panel.ContentHost.Focusable = true;
            Keyboard.Focus(panel.ContentHost);
        }
        dismissal.SetActive(true);
    }
    internal void ToggleControls()
    {
        if (!PrepareSidePanel("controls"))
            return;
        state.ControlsExpanded = !state.ControlsExpanded;
        if (state.ControlsExpanded && !FitsBoth())
            state.PcExpanded = false;
        FocusSidePanel(controls, state.ControlsExpanded);
        Store.Log(state.ControlsExpanded ? "Commandes Windows ouvertes" : "Commandes Windows repliées");
    }
    internal void ControlsChanged() => controls.Rebuild();
    bool FitsBoth() => LayoutRules.FitsBoth(Screen.WorkingArea.Width, VisualTreeHelper.GetDpi(home).DpiScaleX);
    internal void TogglePc()
    {
        if (!PrepareSidePanel("performance"))
            return;
        state.PcExpanded = !state.PcExpanded;
        if (state.PcExpanded && !FitsBoth())
            state.ControlsExpanded = false;
        FocusSidePanel(pc, state.PcExpanded);
        Store.Log(state.PcExpanded ? "État PC ouvert" : "État PC replié");
    }
    internal (long samples, bool running) PcActivity => (pc.Samples, pc.Sampling);
    internal (bool open, bool closing, int visible) UiActivity
        => (state.Open, state.Closing, Windows.Count(w => w.IsVisible && w != controller));
    internal void BeginLayoutInteraction()
    {
        state.Interacting = true;
        state.AwaySince = DateTime.MinValue;
    }
    internal void EndLayoutInteraction()
    {
        state.Interacting = false;
        state.AwaySince = DateTime.MinValue;
        Store.Save();
    }
    internal void ResetLayout()
    {
        foreach (var board in Boards)
            board.Model.Placement = null;
        Store.Save();
        Arrange(true);
    }
    // Un seul geste ferme toute la couche : aucune boîte de dialogue ne doit survivre à la surface qui l'a ouverte.
    void CloseDialogs()
    {
        var open = Application.Current.Windows.OfType<GlassWindow>()
            .Where(w => !Windows.Contains(w) && w is not FocusTimer)
            .ToArray();
        foreach (var window in open)
        {
            try
            {
                window.Close();
            }
            catch (Exception ex) { Store.Log("Fermeture dialogue: " + ex.Message); }
        }
    }
    internal void SetTheme(string id)
    {
        Theme.Use(id);
        Store.Save();
        foreach (var window in Application.Current.Windows.OfType<GlassWindow>().Where(w => !Program.QtMode || w.IsVisible))
        {
            window.RefreshTheme();
            window.FlashTheme();
        }
        clock.RefreshLabels();
        RefreshBoards();
        taskbar?.Refresh();
    }
    internal void Collapse()
    {
        QtShell?.Send("close");
        if (!state.Open || state.Closing)
            return;
        CloseDialogs();
        state.Open = false;
        dismissal.SetActive(false);
        state.Closing = true;
        var visible = Glass.Where(w => w.IsVisible).ToArray();
        int remaining = visible.Length + 1;
        void Done()
        {
            if (--remaining == 0)
            {
                state.Closing = false;
                state.RailAway = DateTime.UtcNow;
                PinWindow.SpaceOpen(false, ToolbarHandle);
            }
        }
        websites.SlideOut(Done);
        for (int i = 0; i < visible.Length; i++)
            visible[i].SlideOut(i, Done);
    }
    internal void CollapseImmediately()
    {
        QtShell?.Send("close");
        CloseDialogs();
        if (!state.Open && !state.Closing)
            return;
        state.Open = false;
        dismissal.SetActive(false);
        state.Closing = false;
        GlassMenu.DismissActive();
        state.PopupOpen = false;
        foreach (var window in Glass.Append(rail))
            window.HideImmediately();
        websites.HideImmediately();
        PinWindow.SpaceOpen(false, ToolbarHandle);
        state.AwaySince = state.EdgeSince = DateTime.MinValue;
        state.RailAway = DateTime.UtcNow;
        Store.Log("Repli immédiat · clic extérieur ou changement d’application");
    }
}
