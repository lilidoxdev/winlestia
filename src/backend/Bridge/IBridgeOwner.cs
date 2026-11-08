// language: C# 12, file: IBridgeOwner.cs, target: Windows 11 / .NET 8, sous-ensemble du Coordinator réellement appelé par le pont
using System;
using System.Collections.Generic;
namespace PanneauxGlass;

// Couture étroite : le pont ne dépend plus du Coordinator concret (fenêtres WPF, bureau), seulement de ces appels. Le Coordinator
// l'implémente par délégation (Core/Coordinator.BridgeOwner.cs) ; les tests fournissent un propriétaire factice.
internal interface IBridgeOwner
{
    IShortcutBindings Keys { get; }
    IShortcutEditor Shortcuts { get; }
    IClipboardService QtClipboard { get; }
    IIconSource Icons { get; }
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

// Vues étroites des services concrets (presse-papiers, icônes, raccourcis) : le pont ne voit plus les classes WPF/Win32.
internal interface IClipboardService
{
    IReadOnlyList<ClipboardItem> Items { get; }
    void TogglePin(ClipboardItem item);
    void CopyBack(ClipboardItem item);
}

internal interface IIconSource
{
    void Load(Entry entry, Action<System.Windows.Media.ImageSource> callback, string scope = "");
    bool IsPlaceholder(System.Windows.Media.ImageSource image);
    int FailureCount(Entry entry);
    void ResetFailures();
}

internal interface IShortcutEditor
{
    object[] List();
    object Status();
    ShortcutResult Reset(string id);
    ShortcutResult Apply(string id, string? combo);
}

// Adaptateur du ShortcutManager réel (ses membres restent internes et inchangés).
internal sealed class ShortcutEditor : IShortcutEditor
{
    readonly ShortcutManager manager;
    internal ShortcutEditor(ShortcutManager manager) => this.manager = manager;
    public object[] List() => manager.List();
    public object Status() => manager.Status();
    public ShortcutResult Reset(string id) => manager.Reset(id);
    public ShortcutResult Apply(string id, string? combo) => manager.Apply(id, combo);
}
