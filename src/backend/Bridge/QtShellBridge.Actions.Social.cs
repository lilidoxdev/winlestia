// language: C# 12, file: QtShellBridge.Actions.Social.cs, target: Windows 11 / .NET 8, actions du pont : réseaux sociaux et favoris des
// navigateurs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Devices.Radios;
namespace PanneauxGlass;

internal sealed partial class QtShellBridge
{
    [BridgeAction("socialOpen")]
    void OnSocialOpen(BridgeCall c)
    {
        _ = social.Open(c.Id, false);
    }

    [BridgeAction("socialLogin")]
    void OnSocialLogin(BridgeCall c)
    {
        _ = social.Open(c.Id, true);
    }

    [BridgeAction("socialEnable")]
    void OnSocialEnable(BridgeCall c)
    {
        _ = social.Enable(c.Id, Flag(c.Args, "enabled"));
    }

    [BridgeAction("socialLogout")]
    void OnSocialLogout(BridgeCall c)
    {
        _ = social.Logout(c.Id);
    }

    [BridgeAction("socialMute")]
    void OnSocialMute(BridgeCall c)
    {
        social.MuteNet(c.Id, Flag(c.Args, "muted"));
    }

    [BridgeAction("socialGlobalMute")]
    void OnSocialGlobalMute(BridgeCall c)
    {
        social.GlobalMute(Flag(c.Args, "muted"));
    }

    [BridgeAction("socialSound")]
    void OnSocialSound(BridgeCall c)
    {
        social.SoundEnabled(Flag(c.Args, "enabled"));
    }

    [BridgeAction("socialVolume")]
    void OnSocialVolume(BridgeCall c)
    {
        social.Volume(Number(c.Args, "value"));
    }

    [BridgeAction("socialHistoryClear")]
    void OnSocialHistoryClear(BridgeCall c)
    {
        social.HistoryClear();
    }

    [BridgeAction("bookmarksOpen")]
    void OnBookmarksOpen(BridgeCall c)
    {
        bookmarks.Open(c.Id);
    }

    [BridgeAction("bookmarksBrowser")]
    void OnBookmarksBrowser(BridgeCall c)
    {
        bookmarks.ChooseBrowser(c.Id);
    }

    [BridgeAction("bookmarksHide")]
    void OnBookmarksHide(BridgeCall c)
    {
        bookmarks.Hide(c.Id);
    }

    [BridgeAction("bookmarksUnhideAll")]
    void OnBookmarksUnhideAll(BridgeCall c)
    {
        bookmarks.UnhideAll();
    }

    [BridgeAction("bookmarksDeleteInBrowser")]
    void OnBookmarksDeleteInBrowser(BridgeCall c)
    {
        _ = bookmarks.DeleteInBrowser(c.Id, Flag(c.Args, "confirmSync"));
    }

    [BridgeAction("bookmarksItems")]
    void OnBookmarksItems(BridgeCall c)
    {
        c.BookmarksItems = bookmarks.Items();
    }

    [BridgeAction("bookmarksRefresh")]
    void OnBookmarksRefresh(BridgeCall c)
    {
        bookmarks.Refresh(Flag(c.Args, "ifDirty"));
    }

    [BridgeAction("socialTestSound")]
    void OnSocialTestSound(BridgeCall c)
    {
        social.TestSound();
    }
}
