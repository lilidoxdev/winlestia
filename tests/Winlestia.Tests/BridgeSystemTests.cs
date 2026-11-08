// C#12 / Handlers du pont Qt qui touchent l'OS (audio, Wi-Fi, bureaux, corbeille, fond d'écran, Explorateur, presse-papiers, outils) :
// tous passent par IBridgeSystem / IBridgeOwner, donc par des doublures. Aucun effet réel : la corbeille n'est jamais vidée.
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PanneauxGlass;
namespace Winlestia.Tests;
public class BridgeSystemTests
{
    static void With(Action<BridgeRig> body) => BridgeHandlerTests.With(body);

    [Fact]
    public void RigBuildsTheBridgeWithItsRealConstructor() => With(r =>
    {
        // Plus de réflexion dans le banc : le constructeur d'essai affecte tout ce que les actions lisent.
        Assert.NotNull(r.Bridge);
        Assert.Null(r.Run("state").Raw);
    });

    [Fact]
    public void RegisteredActionCountIsPinned()
    {
        // Ajouter ou retirer une action impose de mettre ce nombre à jour avec docs/bridge-schema.json.
        Assert.Equal(102, QtShellBridge.ActionNames.Count);
        Assert.Equal(QtShellBridge.ActionNames.Count, QtShellBridge.ActionNames.Distinct().Count());
    }

    [Fact]
    public void VolumeIsClampedAndReadBack() => With(r =>
    {
        r.Run("volume", "{\"value\":150}");
        r.Run("volume", "{\"value\":-5}");
        r.Run("volume", "{\"value\":33}");
        Assert.Equal(new[] { "SetVolume:100", "ReadAudio", "SetVolume:0", "ReadAudio", "SetVolume:33", "ReadAudio" }, r.Sys.Calls);
        Assert.Equal(33, r.Bridge.LastSound!.Volume);
    });

    [Fact]
    public void MasterMuteAndDeviceSelectRefreshTheSound() => With(r =>
    {
        r.Run("masterMute", "{\"muted\":true}");
        Assert.True(r.Bridge.LastSound!.Muted);
        r.Run("deviceSelect", "{\"id\":\"d1\"}");
        Assert.Equal(new[] { "SetMute:True", "ReadAudio", "SetDevice:d1:-:-:True", "ReadAudio" }, r.Sys.Calls);
    });

    [Fact]
    public void MixerActionsForwardLevelsAndMutes() => With(r =>
    {
        r.Run("mediaVolume", "{\"value\":250}");
        r.Run("sessionVolume", "{\"id\":\"s1\",\"value\":40}");
        r.Run("sessionMute", "{\"id\":\"s1\",\"muted\":true}");
        r.Run("deviceVolume", "{\"id\":\"d1\",\"value\":10}");
        r.Run("deviceMute", "{\"id\":\"d1\"}");
        Assert.Equal(new[] { "SetMediaVolume:100", "SetApplication:s1:40:-", "SetApplication:s1:-:True", "SetDevice:d1:10:-:False",
            "SetDevice:d1:-:False:False" }, r.Sys.Calls);
    });

    [Fact]
    public void MediaCommandCarriesActionAndPosition() => With(r =>
    {
        r.Run("media", "{\"action\":\"seek\",\"value\":12.5}");
        Assert.Equal(new[] { "Media:seek:12.5" }, r.Sys.Calls);
    });

    [Fact]
    public void BrightnessWithoutIdTargetsTheCursorDisplay() => With(r =>
    {
        r.Run("brightness", "{\"value\":150}");
        r.Run("brightness", "{\"value\":20}");
        Assert.Equal(new[] { "Brightness:100", "Brightness:20" }, r.Sys.Calls);
    });

    [Fact]
    public void WifiActionsReachTheHost() => With(r =>
    {
        r.Run("wifiConnect", "{\"ssid\":\"Maison\",\"password\":\"secret\"}");
        r.Run("wifiForget", "{\"ssid\":\"Maison\"}");
        r.Run("wifiPortal");
        r.Run("wifiSettings", "{\"page\":\"location\"}");
        r.Run("wifiSettings", "{\"page\":\"wifi\"}");
        Assert.Equal(new[] { "WifiConnect:Maison:secret", "WifiForget:Maison", "WifiPortal", "WifiSettings:True", "WifiSettings:False" },
            r.Sys.Calls);
    });

    [Theory]
    [InlineData("wifi", "ToggleRadio:WiFi")]
    [InlineData("bluetooth", "ToggleRadio:Bluetooth")]
    public void RadioTogglesThenRereadsTheConnections(string kind, string expected) => With(r =>
    {
        var (_, call, _) = r.Run("radio", "{\"kind\":\"" + kind + "\"}");
        Assert.Null(call.Notice);
        Assert.Equal(new[] { expected, "ReadConnections" }, r.Sys.Calls);
    });

    [Fact]
    public void DesktopActionsAreDetachedAndZeroBased() => With(r =>
    {
        r.Run("desktopClose", "{\"index\":3}");
        r.Run("desktopNew");
        r.Run("desktopSwitch", "{\"index\":2,\"relative\":5}");
        Assert.True(r.Sys.WaitFor("CloseDesktop:2"));
        Assert.True(r.Sys.WaitFor("NewDesktop"));
        Assert.True(r.Sys.WaitFor("SwitchDesktop:2:1"));
    });

    [Fact]
    public void EmptyRecycleBinGoesThroughTheSystemSeam() => With(r =>
    {
        r.Run("emptyRecycleBin");
        Assert.Equal(new[] { "EmptyRecycleBin" }, r.Sys.Calls);
    });

    [Fact]
    public void OpenFilesUsesTheDefaultHandlerSeam() => With(r =>
    {
        r.Run("openFiles");
        Assert.Equal(new[] { "OpenFilesHandler" }, r.Sys.Calls);
    });

    [Fact]
    public void QuitShutsDownOnTheDispatcher() => With(r =>
    {
        r.Run("quit");
        Assert.Empty(r.Sys.Calls);
        Assert.True(r.PumpUntil(() => r.Sys.Calls.Contains("Shutdown")));
    });

    [Fact]
    public void CopyTextIgnoresLongText() => With(r =>
    {
        r.Run("copyText", "{\"text\":\"bonjour\"}");
        r.Run("copyText", "{\"text\":\"" + new string('x', 513) + "\"}");
        Assert.Equal(new[] { "SetClipboardText:bonjour" }, r.Sys.Calls);
    });

    [Fact]
    public void ClipboardCopyAndPinResolveTheItemById() => With(r =>
    {
        r.Owner.ClipboardStub.Store.Add(new ClipboardItem { Id = "c1", Text = "a" });
        r.Run("clipboardCopy", "{\"id\":\"c1\"}");
        r.Run("clipboardCopy", "{\"id\":\"zz\"}");
        r.Run("clipboardPin", "{\"id\":\"c1\"}");
        r.Run("clipboardPin", "{\"id\":\"zz\"}");
        Assert.Equal(new[] { "CopyBack:c1", "Pin:c1" }, r.Owner.ClipboardStub.Calls);
        Assert.True(r.Owner.ClipboardStub.Store[0].Pinned);
    });

    [Fact]
    public void ActivateFocusesAWindowOrLaunchesTheDockEntry() => With(r =>
    {
        r.Run("activate", "{\"hwnd\":77}");
        Assert.Empty(r.Owner.Calls);
        r.Sys.WindowFound = false;
        r.Bridge.DockEntries["d1"] = r.AddEntry("d1");
        r.Run("activate", "{\"id\":\"d1\",\"hwnd\":78}");
        r.Run("activate", "{\"id\":\"absent\",\"hwnd\":79}");
        Assert.Equal(new[] { "Activate:77", "Activate:78", "Activate:79" }, r.Sys.Calls);
        Assert.Equal(new[] { "Launch:d1" }, r.Owner.Calls);
    });

    [Fact]
    public void WindowListReturnsThePaletteWindows() => With(r =>
    {
        var (_, call, _) = r.Run("windowList");
        Assert.Single((object[])call.WindowItems!);
    });

    [Fact]
    public void DictationRepliesWithTheSnapshotAndTogglesCapture() => With(r =>
    {
        var (raw, _, _) = r.Run("dictation");
        using var doc = JsonDocument.Parse(raw!);
        var d = doc.RootElement.GetProperty("dictation");
        Assert.Equal("bonjour", d.GetProperty("text").GetString());
        Assert.Equal("idle", d.GetProperty("state").GetString());
        r.Run("dictationCapture", "{\"enabled\":true}");
        r.Run("dictationCapture", "{\"enabled\":false}");
        Assert.Equal(new[] { "DictationBegin", "DictationEnd" }, r.Sys.Calls);
    });

    [Fact]
    public void VisualizerRepliesWithTheSpectrum() => With(r =>
    {
        var (raw, _, _) = r.Run("visualizer");
        using var doc = JsonDocument.Parse(raw!);
        Assert.Equal(new[] { 0.25, 0.5 }, doc.RootElement.GetProperty("spectrum").EnumerateArray().Select(e => e.GetDouble()));
    });

    [Fact]
    public void ExplorerFolderIsStoredOnceAndCancelIsIgnored() => With(r =>
    {
        r.Run("explorerFolder");
        Assert.Empty(r.Prefs.ExplorerFolders);
        r.Sys.Folder = Path.Combine(r.Dir.Path, "dossier");
        r.Run("explorerFolder");
        r.Run("explorerFolder");
        Assert.Equal(new[] { r.Sys.Folder }, r.Prefs.ExplorerFolders);
    });

    [Fact]
    public void ExplorerPaletteAndRestoreUseTheSeam() => With(r =>
    {
        r.Prefs.ExplorerTheme = true;
        var (_, palette, _) = r.Run("explorerPalette", "{\"accent\":\"#112233\",\"background\":\"#000000\",\"text\":\"#ffffff\"}");
        Assert.Null(palette.Notice);
        var (_, restore, _) = r.Run("explorerRestore");
        Assert.False(r.Prefs.ExplorerTheme);
        Assert.Equal("Explorateur rétabli", restore.Notice);
        Assert.Equal(new[] { "ExplorerSync:#112233:#000000:#ffffff", "ExplorerRestore" }, r.Sys.Calls);
    });

    [Fact]
    public void WallpaperChangedAndPickerAreRelayed() => With(r =>
    {
        r.Run("wallpaperChanged");
        r.Run("wallpaperPicker");
        Assert.True(r.PumpUntil(() => r.Bridge.LateWallpaperPicker != null));
        Assert.Equal(new[] { "NotifyWallpaper", "WallpaperPicker" }, r.Sys.Calls);
    });

    [Fact]
    public void WallpaperApplySuccessNotifiesAndFailureBecomesALateNotice() => With(r =>
    {
        r.Run("wallpaperApply", "{\"path\":\"a.jpg\",\"display\":2}");
        Assert.True(r.PumpUntil(() => r.Bridge.LateWallpaperPicker != null));
        Assert.Null(r.Bridge.LateNotice);
        Assert.Contains("NotifyWallpaper", r.Sys.Calls);
        r.Sys.Wallpaper = (1, "Fond refusé");
        r.Run("wallpaperApply", "{\"path\":\"b.jpg\",\"display\":0}");
        Assert.True(r.PumpUntil(() => r.Bridge.LateNotice != null));
        Assert.Equal("Fond refusé", r.Bridge.LateNotice);
        Assert.Contains(r.Sys.Calls, line => line.StartsWith("ApplyWallpaper:a.jpg:2:"));
    });

    [Fact]
    public void WallpaperCaptureReportsTheShotAndTheRegion() => With(r =>
    {
        var (_, call, _) = r.Run("wallpaperCapture", "{\"token\":\"t1\",\"x\":10,\"y\":20,\"w\":100,\"h\":50}");
        var json = JsonSerializer.Serialize(call.WallpaperCapture);
        Assert.Contains("\"token\":\"t1\"", json);
        Assert.Contains("\"ok\":true", json);
        r.Sys.Shot = null;
        var (_, none, _) = r.Run("wallpaperCapture", "{\"token\":\"t2\"}");
        Assert.Contains("\"ok\":false", JsonSerializer.Serialize(none.WallpaperCapture));
        Assert.Equal(new[] { "CaptureWallpaper:10,20,110,70", "CaptureWallpaper:plein" }, r.Sys.Calls);
    });

    [Fact]
    public void SocialTestSoundPlaysTheInjectedChime() => With(r =>
    {
        r.Run("socialTestSound");
        Assert.Equal(new[] { "Chime" }, r.Owner.Calls);
    });

    [Theory]
    [InlineData("capture", "capture")]
    [InlineData("ocr", "capture-ocr")]
    [InlineData("audio", "audio")]
    [InlineData("clipboard", "clipboard")]
    public void ToolRunPanelsOnlySendAnEvent(string tool, string expected) => With(r =>
    {
        Assert.Equal(new[] { expected }, r.Run("toolRun", "{\"id\":\"" + tool + "\"}").Events);
        Assert.Empty(r.Owner.Calls);
    });

    [Theory]
    [InlineData("notifications", "ToggleNotifications")]
    public void ToolRunNotificationsCallsTheOwner(string tool, string expected) => With(r =>
    {
        Assert.Empty(r.Run("toolRun", "{\"id\":\"" + tool + "\"}").Events);
        Assert.Equal(new[] { expected }, r.Owner.Calls);
    });

    [Theory]
    [InlineData("pipette", "OpenColorPicker")]
    [InlineData("ruler", "OpenRuler")]
    [InlineData("pin", "ToggleAlwaysOnTop")]
    [InlineData("largefiles", "OpenLargeFiles")]
    [InlineData("profiles", "OpenWorkspaces")]
    [InlineData("memo", "OpenMemo")]
    [InlineData("help", "OpenHelp")]
    public void ToolRunDialogsCloseThePanelThenOpenTheTool(string tool, string expected) => With(r =>
    {
        Assert.Equal(new[] { "close" }, r.Run("toolRun", "{\"id\":\"" + tool + "\"}").Events);
        Assert.True(r.PumpUntil(() => r.Owner.Calls.Count > 0));
        Assert.Equal(new[] { expected }, r.Owner.Calls);
        Assert.False(r.Bridge.DialogOpen);
    });

    [Fact]
    public void FileSearchStoresTheLatestGenerationAndPublishesIt() => With(r =>
    {
        r.Run("fileSearch", "{\"term\":\"rapport\",\"gen\":7}");
        object? payload = null;
        var seen = new System.Collections.Generic.List<string>();
        Assert.True(r.PumpUntil(() =>
        {
            var (_, call, events) = r.Run("fileResults");
            seen.AddRange(events);
            return (payload = call.FileItems) != null && seen.Contains("files");
        }));
        var json = JsonSerializer.Serialize(payload);
        Assert.Contains("\"gen\":7", json);
        Assert.Contains("a.txt", json);
        seen.AddRange(r.Bridge.DrainEvents());
        Assert.Contains("files", seen);
        r.Run("fileCancel");
        Assert.Null(r.Run("fileResults").Call.FileItems);
    });

    [Fact]
    public void CaptureExportRefusesPathsOutsideTheCaptureFolder() => With(r =>
    {
        var (_, call, _) = r.Run("captureExport", "{\"path\":\"" + Path.Combine(r.Dir.Path, "x.png").Replace("\\", "\\\\")
            + "\",\"action\":\"pin\"}");
        Assert.Equal("Capture invalide.", call.Notice);
        Assert.Empty(r.Owner.Calls);
    });

    [Fact]
    public void CaptureExportPinsAValidCapture() => With(r =>
    {
        string folder = Path.Combine(r.Dir.Path, "captures");
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "c.png");
        var pixels = new byte[4 * 4 * 4];
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, pixels, 16)));
        using (var stream = File.Create(file))
            encoder.Save(stream);
        var (_, call, _) = r.Run("captureExport", "{\"path\":\"" + file.Replace("\\", "\\\\") + "\",\"action\":\"pin\"}");
        Assert.Equal("Capture épinglée", call.Notice);
        Assert.Equal(new[] { "PinImage" }, r.Owner.Calls);
    });

    [Fact]
    public void RunningRefreshIsAcceptedWithoutReply() => With(r =>
    {
        var (raw, call, _) = r.Run("runningRefresh");
        Assert.Null(raw);
        Assert.Null(call.Notice);
    });
}
