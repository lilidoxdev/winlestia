// language: C# 12, file: IBridgeOwner.cs, target: Windows 11 / .NET 8, sous-ensemble du Coordinator réellement appelé par le pont
namespace PanneauxGlass;

// Couture étroite : le pont ne dépend plus du Coordinator concret (fenêtres WPF, bureau), seulement de ces appels. Le Coordinator
// l'implémente par délégation (Core/Coordinator.BridgeOwner.cs) ; les tests fournissent un propriétaire factice.
internal interface IBridgeOwner
{
    IShortcutBindings Keys { get; }
    ShortcutManager Shortcuts { get; }
    ClipboardService QtClipboard { get; }
    IconLoader Icons { get; }
    bool TaskbarEnabled { get; }

    void SetTheme(string id);
    void SetTaskbar(bool enabled);
    void SuspendTaskbar();
    void RestoreDesktopIcons();
    void ApplyClipboardSettings();
    void RefreshCatalogue();
    void RefreshCatalogueIfStale();

    void Launch(Entry entry);
    void LaunchTarget(string target);
    void Reveal(Entry entry);
    void Edit(Entry entry);
    void RunQuickTab(WindowsShortcut tab);
    void WebSearch(string value);

    void AddAccess(string board);
    void AddFolder(string board);
    void AddWebsite();

    void OpenWorkspaces();
    void OpenTools();
    void OpenRuler();
    void OpenNotes();
    void OpenMemo();
    void OpenLargeFiles();
    void OpenHelp();
    void OpenFocus();
    void OpenColorPicker();
    void OpenCapture();
    void OpenAudio();
    void EditQuickTabs();
    void ToggleNotifications();
    void ToggleAlwaysOnTop();
    void TaskManager();
    void PinImage(System.Windows.Media.Imaging.BitmapImage image);
}
