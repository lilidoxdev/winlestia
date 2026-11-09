// C#12 / Handlers du pont Qt (suite) : réseaux sociaux, favoris navigateur, fichiers, dock, sondes brutes et arguments absents ou
// invalides. Même banc que BridgeHandlerTests : dispatch réel, propriétaire factice, aucun service du bureau.
using System;
using System.Text.Json;
using PanneauxGlass;
namespace Winlestia.Tests;
public class BridgeSocialFilesTests
{
    static void With(Action<BridgeRig> body) => BridgeHandlerTests.With(body);

    [Fact]
    public void SocialMuteTargetsOnlyKnownNetworks() => With(r =>
    {
        r.Run("socialMute", "{\"id\":\"discord\",\"muted\":true}");
        r.Run("socialMute", "{\"id\":\"inconnu\",\"muted\":true}");
        Assert.True(Assert.Single(r.Prefs.SocialNetMuted).Value);
        Assert.True(r.Prefs.SocialNetMuted["discord"]);
        r.Run("socialMute", "{\"id\":\"discord\"}");
        Assert.False(r.Prefs.SocialNetMuted["discord"]);
    });

    [Fact]
    public void SocialGlobalSettingsAreStored() => With(r =>
    {
        r.Run("socialGlobalMute", "{\"muted\":true}");
        Assert.True(r.Prefs.SocialMuted);
        r.Run("socialSound", "{\"enabled\":false}");
        Assert.False(r.Prefs.SocialSound);
        r.Run("socialVolume", "{\"value\":150}");
        Assert.Equal(100, r.Prefs.SocialVolume);
        r.Run("socialVolume", "{\"value\":-4}");
        Assert.Equal(0, r.Prefs.SocialVolume);
        r.Run("socialVolume", "{\"value\":33.6}");
        Assert.Equal(34, r.Prefs.SocialVolume);
        r.Run("socialHistoryClear");
    });

    [Fact]
    public void SocialActionsOnUnknownNetworksAreIgnored() => With(r =>
    {
        foreach (string command in new[] { "socialOpen", "socialLogin", "socialEnable", "socialLogout" })
        {
            var (raw, call, events) = r.Run(command, "{\"id\":\"inconnu\",\"enabled\":true}");
            Assert.Null(raw);
            Assert.Null(call.Notice);
            Assert.Empty(events);
        }
        Assert.Empty(r.Prefs.SocialEnabled);
    });

    [Fact]
    public void SocialEnableDisablesAKnownNetwork() => With(r =>
    {
        r.Run("socialEnable", "{\"id\":\"discord\",\"enabled\":false}");
        Assert.False(r.Prefs.SocialEnabled["discord"]);
    });

    [Fact]
    public void BookmarksItemsReplyCarriesAVersionedPayload() => With(r =>
    {
        var items = r.Run("bookmarksItems").Call.BookmarksItems;
        Assert.NotNull(items);
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(items));
        Assert.True(doc.RootElement.TryGetProperty("items", out var list));
        Assert.Equal(0, list.GetArrayLength());
    });

    [Fact]
    public void BookmarksActionsWithoutALoadedBrowserDoNothing() => With(r =>
    {
        foreach (string command in new[] { "bookmarksOpen", "bookmarksBrowser", "bookmarksHide", "bookmarksUnhideAll",
            "bookmarksDeleteInBrowser", "bookmarksRefresh" })
        {
            var (raw, call, events) = r.Run(command, "{\"id\":\"absent\",\"ifDirty\":true,\"confirmSync\":false}");
            Assert.Null(raw);
            Assert.Null(call.Notice);
            Assert.Empty(events);
        }
        Assert.Empty(r.Prefs.BookmarksHidden);
        Assert.Equal("", r.Prefs.BookmarksBrowser);
    });

    [Fact]
    public void FileResultsHasNoPayloadBeforeAnySearch() => With(r =>
    {
        Assert.Null(r.Run("fileResults").Call.FileItems);   // aucune recherche lancée : pas de charge utile
        r.Run("fileCancel");
    });

    [Fact]
    public void FileOpenWithAnUnknownGenerationDoesNothing() => With(r =>
    {
        var (_, call, events) = r.Run("fileOpen", "{\"gen\":99,\"index\":0}");
        Assert.Null(call.Notice);
        Assert.Empty(events);
    });

    [Theory]
    [InlineData("clipboardFiles")]
    [InlineData("zipFiles")]
    [InlineData("recycleFiles")]
    public void FileActionsWithoutPathsLeaveNoNotice(string command) => With(r =>
    {
        var (raw, call, _) = r.Run(command, "{}");
        Assert.Null(raw);
        Assert.Null(call.Notice);
    });

    [Fact]
    public void RecycleFilesIgnoresNonArrayPaths() => With(r =>
    {
        Assert.Null(r.Run("recycleFiles", "{\"paths\":\"c:\\\\x\"}").Call.Notice);
    });

    [Fact]
    public void DockAppOnAnUnknownApplicationExplainsIt() => With(r =>
    {
        var (_, call, _) = r.Run("dockApp", "{\"id\":\"absent\",\"action\":\"open\"}");
        Assert.Equal("Cette application n’est plus disponible.", call.Notice);
        Assert.Empty(r.Owner.Calls);
    });

    [Fact]
    public void DockPreviewRequestsAreQueuedNotExecuted() => With(r =>
    {
        var (raw, call, events) = r.Run("dockPreview", "{\"id\":\"a\",\"x\":1,\"y\":2,\"cx\":1,\"cy\":1,\"dpr\":2}");
        Assert.Null(raw);
        Assert.Null(call.Notice);
        Assert.Empty(events);
        Assert.Null(r.Run("dockPreviewEnd", "{\"now\":true}").Raw);
    });

    [Fact]
    public void UnconfirmedPowerActionsAreIgnored() => With(r =>
    {
        foreach (string id in new[] { "shutdown", "restart", "logout", "hibernate", "sleep" })
            Assert.Null(r.Run("systemAction", "{\"id\":\"" + id + "\"}").Call.Notice);
    });

    [Fact]
    public void AudioBarsAnswersWithARawProbeReply() => With(r =>
    {
        var (raw, call, events) = r.Run("audioBars", "{\"seq\":-1}");
        Assert.NotNull(raw);
        using var doc = JsonDocument.Parse(raw!);
        var bars = doc.RootElement.GetProperty("audioBars");
        Assert.True(bars.GetProperty("idle").GetBoolean());
        Assert.Equal(raw, call.RawReply);
        Assert.Empty(events);
    });

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"texte\"")]
    [InlineData("42")]
    public void NonObjectArgumentsNeverEscapeTheDispatch(string args) => With(r =>
    {
        foreach (string command in new[] { "launch", "preference", "theme", "customTheme", "shortcut", "move", "editEntry",
            "paletteRun", "workspace", "settings", "clipboardFiles", "zipFiles", "recycleFiles", "socialMute", "dockApp", "fileOpen" })
            _ = r.Run(command, args);
    });

    [Theory]
    [InlineData("customTheme")]
    [InlineData("clipboardFiles")]
    public void AbsentArgumentsBecomeANoticeNotAnException(string command) => With(r =>
    {
        var (raw, call, _) = r.Run(command, "null");
        Assert.Null(raw);
        Assert.False(string.IsNullOrEmpty(call.Notice));
    });
}
