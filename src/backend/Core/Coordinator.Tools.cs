// language: C# 12, file: Coordinator.Tools.cs, target: Windows 11 / .NET 8 WPF, outils (capture, pipette, presse-papiers…) et raccourcis
// globaux
using System.Threading.Tasks;

namespace PanneauxGlass;

internal sealed partial class Coordinator
{
    const int CaptureCollapseDelayMs = 260;
    const int ToolCollapseDelayMs = 180;
    // Avec le shell Qt, l'outil s'ouvre côté frontend : vrai quand la commande a été transmise et qu'il n'y a plus rien à faire ici.
    bool ForwardToQt(string command)
    {
        if (QtShell == null)
            return false;
        QtShell.Send(command);
        return true;
    }
    // Replie l'espace et laisse l'animation finir avant l'outil plein écran, sinon il capturerait ou masquerait le panneau.
    async Task CollapseThen(int delayMs)
    {
        CloseDialogs();
        CollapseImmediately();
        await Task.Delay(delayMs);
    }
    internal async void OpenCapture(bool textOnly = false)
    {
        if (ForwardToQt(textOnly ? "capture-ocr" : "capture-select"))
            return;
        await CollapseThen(CaptureCollapseDelayMs);
        CaptureOverlay.Begin(this, textOnly);
    }
    internal async void OpenColorPicker()
    {
        await CollapseThen(ToolCollapseDelayMs);
        ColorTools.OpenPicker(this);
    }
    internal async void OpenRuler()
    {
        await CollapseThen(ToolCollapseDelayMs);
        ColorTools.OpenRuler(this);
    }
    internal void OpenMemo()
    {
        CollapseImmediately();
        PinWindow.OpenMemo(this);
    }
    internal void OpenClipboard()
    {
        if (!ForwardToQt("clipboard"))
            OpenClipboardLegacy();
    }
    internal ClipboardService QtClipboard => clipboard;
    internal void OpenClipboardLegacy() => ShowModal(() => new ClipboardWindow(this, clipboard), OpenOwner);
    internal void OpenAudio()
    {
        if (!ForwardToQt("audio"))
            ShowModal(() => new AudioWindow(this), OpenOwner);
    }
    internal void OpenWorkspaces() => ShowModal(() => new WorkspaceWindow(this), OpenOwner);
    internal void OpenLargeFiles() => ShowModal(() => new LargeFilesWindow(this), OpenOwner);
    internal void OpenTools()
    {
        if (!ForwardToQt("tools"))
            ShowModal(() => new ToolsWindow(this), OpenOwner);
    }
    internal void OpenHelp() => ShowModal(() => new HelpWindow(this), OpenOwner);
    internal void RefreshPaletteLabels()
    {
        if (paletteChip != null)
        {
            paletteChip.Content = Keys.PaletteLabel;
            paletteChip.ToolTip = "Commande rapide · " + Keys.PaletteLabel;
        }
        clock.RefreshLabels();
        palette?.ReloadTabs();
    }
    internal void ToggleShortcut()
    {
        if (ForwardToQt("toggle"))
            return;
        if (state.Open)
        {
            Collapse();
            return;
        }
        var choice = Store.State.Shortcut.View;
        bool full = choice == "workspace" || choice == "last" && state.Detailed,
            windows = choice == "controls" || choice == "last" && state.ControlsExpanded,
            status = choice == "pc" || choice == "last" && state.PcExpanded;
        OpenView(full, windows, status);
    }
}
