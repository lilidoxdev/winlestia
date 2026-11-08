// language: C# 12, file: Coordinator.Boards.cs, target: Windows 11 / .NET 8 WPF, sites, panneaux et entrées (lancement, édition, ajout)
using System.Linq;
using System.Windows.Input;

namespace PanneauxGlass;

internal sealed partial class Coordinator
{
    internal void RefreshWebsites() => websites.Refresh();
    internal void AddWebsite(WebShortcut? site = null, bool chooseImage = false)
        => ShowModal(() => new WebShortcutWindow(this, site, chooseImage), DialogParent);
    internal void RefreshBoards()
    {
        foreach (var board in Boards.Where(b => b.IsVisible))
            board.Refresh(false);
        if (home.IsVisible)
            home.Refresh();
        if (state.Detailed && state.Open)
            websites.Refresh();
    }
    internal bool HandleShortcut(KeyEventArgs e)
    {
        if (e.Key == Key.F2 && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (!state.PaletteOpen)
                TogglePalette();
            return true;
        }
        if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ShowHome();
            return true;
        }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is >= Key.D1 and <= Key.D4)
        {
            string[] ids = { "apps", "games", "projects", "documents" };
            ShowWorkspace(ids[(int)(e.Key - Key.D1)]);
            return true;
        }
        return false;
    }
    internal void Launch(Entry entry) => launcher.Launch(entry);
    internal void Reveal(Entry e) => launcher.Reveal(e);
    internal void AddPath(string path, string board) => launcher.AddPath(path, board);
    internal void Settings(string? board = null)
    {
        if (ForwardToQt("settings") || state.SettingsOpen)
            return;
        if (!state.Open)
            Open();
        ShowModal(() => new PersonalizeWindow(this, board), DialogParent);
    }
    internal void Edit(Entry e) => ShowModal(() => new EditWindow(this, e, false), DialogParent, resetAway: false);
    internal void AddAccess(string board)
    {
        foreach (string path in dialogs.PickFiles())
            AddPath(path, board);
        RefreshBoards();
    }
    internal void AddFolder(string board)
    {
        string? folder = dialogs.PickFolder();
        if (folder != null)
            AddPath(folder, board);
        RefreshBoards();
    }
}
