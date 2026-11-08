// language: C# 12, file: Coordinator.BridgeOwner.cs, target: Windows 11 / .NET 8 WPF, délégation vers la couture du pont Qt
namespace PanneauxGlass;

// Implémentation explicite : les membres internes du Coordinator restent inchangés, l'interface n'est qu'une vue étroite.
internal sealed partial class Coordinator : IBridgeOwner
{
    IShortcutBindings IBridgeOwner.Keys => Keys;
    IShortcutEditor IBridgeOwner.Shortcuts => new ShortcutEditor(Shortcuts);
    IClipboardService IBridgeOwner.QtClipboard => QtClipboard;
    IIconSource IBridgeOwner.Icons => Icons;
    bool IBridgeOwner.TaskbarEnabled => TaskbarEnabled;

    void IBridgeOwner.SetTheme(string id) => SetTheme(id);
    void IBridgeOwner.SetTaskbar(bool enabled) => SetTaskbar(enabled);
    void IBridgeOwner.SuspendTaskbar() => SuspendTaskbar();
    void IBridgeOwner.RestoreDesktopIcons() => RestoreDesktopIcons();
    void IBridgeOwner.ApplyClipboardSettings() => ApplyClipboardSettings();
    void IBridgeOwner.RefreshCatalogue() => RefreshCatalogue();
    void IBridgeOwner.RefreshCatalogueIfStale() => RefreshCatalogueIfStale();

    void IBridgeOwner.Launch(Entry entry) => Launch(entry);
    void IBridgeOwner.LaunchTarget(string target) => LaunchTarget(target);
    void IBridgeOwner.Reveal(Entry entry) => Reveal(entry);
    void IBridgeOwner.Edit(Entry entry) => Edit(entry);
    void IBridgeOwner.RunQuickTab(WindowsShortcut tab) => RunQuickTab(tab);
    void IBridgeOwner.WebSearch(string value) => WebSearch(value);

    void IBridgeOwner.AddAccess(string board) => AddAccess(board);
    void IBridgeOwner.AddFolder(string board) => AddFolder(board);
    void IBridgeOwner.AddWebsite() => AddWebsite();

    void IBridgeOwner.OpenWorkspaces() => OpenWorkspaces();
    void IBridgeOwner.OpenTools() => OpenTools();
    void IBridgeOwner.OpenRuler() => OpenRuler();
    void IBridgeOwner.OpenNotes() => OpenNotes();
    void IBridgeOwner.OpenMemo() => OpenMemo();
    void IBridgeOwner.OpenLargeFiles() => OpenLargeFiles();
    void IBridgeOwner.OpenHelp() => OpenHelp();
    void IBridgeOwner.OpenFocus() => OpenFocus();
    void IBridgeOwner.OpenColorPicker() => OpenColorPicker();
    void IBridgeOwner.OpenCapture() => OpenCapture();
    void IBridgeOwner.OpenAudio() => OpenAudio();
    void IBridgeOwner.EditQuickTabs() => EditQuickTabs();
    void IBridgeOwner.ToggleNotifications() => ToggleNotifications();
    void IBridgeOwner.ToggleAlwaysOnTop() => ToggleAlwaysOnTop();
    void IBridgeOwner.TaskManager() => SystemActions.TaskManager(this);
    void IBridgeOwner.PinImage(System.Windows.Media.Imaging.BitmapImage image) => PinWindow.Open(this, image);
}
