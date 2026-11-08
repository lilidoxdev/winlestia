// language: C# 12, file: Coordinator.Palette.cs, target: Windows 11 / .NET 8 WPF, palette de recherche, onglets rapides et notes
using System;
using System.Windows;

namespace PanneauxGlass;

internal sealed partial class Coordinator
{
    internal void ClosePalette() => palette?.Close();
    internal void TogglePalette()
    {
        if (ForwardToQt("palette"))
            return;
        if (state.PaletteOpen)
            palette?.Close();
        else
            ShowPalette();
    }
    internal void ShowPalette()
    {
        if (ForwardToQt("palette") || state.PaletteOpen)
            return;
        state.PaletteOpen = true;
        var dialog = new CommandPalette(this) { Owner = OpenOwner, ShowInTaskbar = preview };
        palette = dialog;
        dialogs.Layers(true);
        dialog.Closed += (_, _) =>
        {
            state.PaletteOpen = false;
            if (ReferenceEquals(palette, dialog))
                palette = null;
            dialogs.Layers(state.SettingsOpen);
            state.AwaySince = DateTime.MinValue;
        };
        dialog.Show();
        Native.Raise(dialog);
    }
    internal void OpenFocus() => ShowModal(() => new FocusSetupWindow(this), OpenOwner);
    internal void EditQuickTabs(Window? parent = null) => ShowModal(() => new QuickTabsWindow(this), parent ?? Anchor);
    // Les onglets glass: pointent vers une vue du Coordinator ; tout le reste est un raccourci Windows.
    internal void RunQuickTab(WindowsShortcut tab)
    {
        string target = tab.Target ?? "";
        if (!QuickTabRules.IsInternal(target))
        {
            LaunchWindowsShortcut(tab);
            return;
        }
        switch (QuickTabRules.Parse(target))
        {
            case QuickTabView.Home: ShowHome(); break;
            case QuickTabView.Apps: ShowWorkspace("apps"); break;
            case QuickTabView.Games: ShowWorkspace("games"); break;
            case QuickTabView.Projects: ShowWorkspace("projects"); break;
            case QuickTabView.Documents: ShowWorkspace("documents"); break;
            case QuickTabView.Workspace: ShowWorkspace(); break;
            case QuickTabView.Note: OpenNotes(); break;
            case QuickTabView.Focus: OpenFocus(); break;
            case QuickTabView.Settings: Settings(); break;
            case QuickTabView.Tools: OpenTools(); break;
            case QuickTabView.Capture: OpenCapture(); break;
            case QuickTabView.Text: OpenCapture(textOnly: true); break;
            case QuickTabView.Clipboard: OpenClipboard(); break;
            case QuickTabView.Audio: OpenAudio(); break;
            case QuickTabView.Workspaces: OpenWorkspaces(); break;
            case QuickTabView.Files: OpenLargeFiles(); break;
            case QuickTabView.Help: OpenHelp(); break;
            case QuickTabView.Pin: ToggleAlwaysOnTop(); break;
            case QuickTabView.Memo: OpenMemo(); break;
            case QuickTabView.Controls: ToggleControls(); break;
            case QuickTabView.Pc: TogglePc(); break;
            case QuickTabView.Palette: TogglePalette(); break;
            default: Message("Cet onglet rapide pointe vers une vue inconnue.\n" + target); break;
        }
    }
    internal void OpenNotes()
    {
        ShowModal(() => new NotesWindow(this), OpenOwner);
        state.AwaySince = DateTime.MinValue;
    }
    internal void StartFocus(int seconds) => FocusTimer.Start(this, seconds);
    internal void WebSearch(string value, bool address = false) => launcher.WebSearch(value, address);
}
