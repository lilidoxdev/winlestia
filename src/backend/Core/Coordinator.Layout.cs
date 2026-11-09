// language: C# 12, file: Coordinator.Layout.cs, target: Windows 11 / .NET 8 WPF, disposition des fenêtres de l'accueil et de la vue
// complète
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PanneauxGlass;

internal sealed partial class Coordinator
{
    // Le thème « shell » remplace l'horloge de l'accueil par celle du dock : elle reste donc masquée.
    static bool ShellTheme => Theme.Current.Id.StartsWith("shell", StringComparison.Ordinal);
    // Les flèches changent de glyphe et d'infobulle selon que leur panneau est déplié.
    static void UpdateArrow(GlassWindow arrow, bool expanded, (string Expanded, string Collapsed) glyphs,
        (string Collapse, string Show) tips)
    {
        if (arrow.ContentHost.Children[0] is not Button button)
            return;
        button.Content = UI.Glyph(expanded ? glyphs.Expanded : glyphs.Collapsed);
        button.ToolTip = expanded ? tips.Collapse : tips.Show;
        System.Windows.Automation.AutomationProperties.SetName(button, button.ToolTip.ToString());
    }
    internal void Arrange(bool animate = false)
    {
        if (!state.Open)
            return;
        var w = Screen.WorkingArea;
        double d = VisualTreeHelper.GetDpi(home).DpiScaleX;
        var work = new Rect(w.Left / d, w.Top / d, w.Width / d, w.Height / d);
        if (!LayoutRules.FitsBoth(w.Width, d) && state.ControlsExpanded && state.PcExpanded)
            state.ControlsExpanded = false;
        var layout = LayoutRules.Detailed(work, Store.State.Margin, Store.State.Gap, Store.State.TopRatio, Store.State.LeftRatio);
        PanelBounds = layout.Panel;
        if (state.Detailed)
            ArrangeDetailed(layout, animate);
        else
            ArrangeHome(work, animate);
    }
    void ArrangeHome(Rect work, bool animate)
    {
        var pair = ControlPairLayout.Calculate(work, state.ControlsExpanded, state.PcExpanded, !ShellTheme);
        if (ShellTheme)
            clock.HideImmediately();
        else
            clock.TransitionTo(pair.Clock, 0, animate);
        home.TransitionTo(pair.Home, 0, animate);
        home.Refresh();
        leftArrow.TransitionTo(pair.LeftArrow, 1, animate);
        rightArrow.TransitionTo(pair.RightArrow, 1, animate);
        if (state.PcExpanded)
            pc.TransitionTo(pair.Pc, 2, animate);
        else
            pc.HideImmediately();
        if (state.ControlsExpanded)
            controls.TransitionTo(pair.Controls, 2, animate);
        else
            controls.HideImmediately();
        UpdateArrow(leftArrow, state.PcExpanded, ("\uE76C", "\uE76B"), ("Replier l’état du PC", "Afficher l’état du PC"));
        UpdateArrow(rightArrow, state.ControlsExpanded, ("\uE76B", "\uE76C"),
            ("Replier les commandes Windows", "Afficher les commandes Windows"));
    }
    void ArrangeDetailed(WorkspaceLayout layout, bool animate)
    {
        clock.HideImmediately();
        toolbar.MoveInto(layout.ToolbarX, layout.ToolbarY, layout.ToolbarWidth, LayoutRules.ToolbarHeight, 0, animate);
        websites.Arrange(layout.ToolbarX - 16, layout.ToolbarY, layout.SideWidth, animate);
        for (int i = 0; i < Boards.Count; i++)
        {
            var r = Boards[i].Model.Placement?.Resolve(PanelBounds) ?? PanelLayout.Clamp(layout.Targets[i], PanelBounds);
            Boards[i].MoveInto(r.X, r.Y, r.Width, r.Height, i + 1, animate);
            Boards[i].Refresh(false);
        }
        toolbar.Topmost = true;
    }
}
