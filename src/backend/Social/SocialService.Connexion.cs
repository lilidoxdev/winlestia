// SocialService : détection de l'état de connexion à chaque réseau.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace PanneauxGlass;

internal sealed partial class SocialService
{
    // ---- Connexion ---------------------------------------------------------------------------------------------
    static bool HostMatches(Net n, Uri? u) => u != null && n.Hosts.Any(h => u.Host.Equals(h, StringComparison.OrdinalIgnoreCase)
        || u.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));
    static bool OnService(Net n, string? source) => Uri.TryCreate(source, UriKind.Absolute, out var u) && HostMatches(n, u);
    static bool SamePath(string? source, string target) => Uri.TryCreate(source, UriKind.Absolute, out var a) && Uri.TryCreate(target,
        UriKind.Absolute, out var b) && a.Host == b.Host && a.AbsolutePath.TrimEnd('/') == b.AbsolutePath.TrimEnd('/');
    bool OnLoginPage(Net n) => Uri.TryCreate(n.Wv?.Source, UriKind.Absolute, out var u) && HostMatches(n, u) && UrlSaysConnected(n,
        u) == false;

    // true/false si l'adresse suffit à conclure, null sinon (autre domaine, réseau décidé par script...).
    static bool? UrlSaysConnected(Net n, Uri u)
    {
        if (n.Id == "gmail" && u.Host.Equals("accounts.google.com", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!HostMatches(n, u))
            return null;
        string path = u.AbsolutePath.ToLowerInvariant();
        switch (n.Id)
        {
            case "discord":
                if (path.StartsWith("/login") || path.StartsWith("/register"))
                    return false;
                return path.StartsWith("/channels") || path.StartsWith("/app") ? true : null;
            case "instagram":
                if (path.StartsWith("/accounts/login") || path.StartsWith("/accounts/emailsignup")
                    || path.StartsWith("/accounts/password"))
                    return false;
                return true;
            case "snapchat":
                return u.Host.Equals("accounts.snapchat.com", StringComparison.OrdinalIgnoreCase) ? false : true;
            case "gmail":
                return true;
            default:
                return null;
        }
    }

    void EvaluateUrl(Net n)
    {
        if (n.Wv == null || !Uri.TryCreate(n.Wv.Source, UriKind.Absolute, out var u))
            return;
        var verdict = UrlSaysConnected(n, u);
        if (verdict.HasValue)
            SetConnected(n, verdict.Value);
    }

    void SetConnected(Net n, bool value)
    {
        if (n.Connected == value)
            return;
        n.Connected = value;
        Store.State.SocialConnected[n.Id] = value;
        SavePrefsSoon();
        if (value)
            MaybeArm(n);
        Changed();
    }

    // Les préférences ne s'écrivent que sur le dispatcher (Store.Save n'est pas thread-safe), regroupées.
    void SavePrefsSoon()
    {
        if (prefsSaveQueued || disposed)
            return;
        prefsSaveQueued = true;
        dispatcher.BeginInvoke((Action)(() => { prefsSaveQueued = false; if (!disposed) Store.Save(); }), DispatcherPriority.Background);
    }

    async Task CheckLater(Net n, int delayMs)
    {
        try
        {
            n.NextCheck = DateTime.UtcNow.AddSeconds(30);
            await Task.Delay(delayMs);
            if (disposed || n.Wv == null || n.Suspended)
                return;
            if (!n.Script && n.Id != "instagram")
                return;
            if (n.Id == "instagram" && !n.Connected)
                return;
            string script = n.Id switch
            {
                "whatsapp" => "(()=>{if(document.querySelector('#pane-side,[data-testid=\"chat-list\"]'))return " +
                    "1;if(document.querySelector('[data-ref],[data-testid=\"qrcode\"],canvas[aria-label]'))return 0;return -1;})()",
                "telegram" => "(()=>{if(document.querySelector('.chatlist-container,.chatlist'))return " +
                    "1;if(document.querySelector('.page-signQR,.page-sign,#auth-pages .tabs-container'))return 0;return -1;})()",
                _ => "(()=>document.querySelector('input[name=\"password\"]')?0:1)()"
            };
            string result = await n.Wv.ExecuteScriptAsync(script);
            if (disposed || n.Wv == null)
                return;
            if (result == "1")
                SetConnected(n, true);
            else if (result == "0")
                SetConnected(n, false);
        }
        catch (Exception ex) { Store.Log("Social détection " + n.Id + ": " + ex.Message); }
    }

    // Une fois toutes les 30 s, et seulement tant qu'une vue existe : réseaux à script non connectés, puis mémoire libre.
    void SlowTick(object? sender, EventArgs e)
    {
        if (disposed)
            return;
        foreach (var n in nets)
            if (n.Script && !n.Connected && n.Wv != null && !n.Suspended && !n.Navigating)
                _ = CheckLater(n, 0);
        var now = DateTime.UtcNow;
        foreach (var n in nets)
            if (n.Wv != null && !n.Creating && !n.Navigating && SocialPolicy.ShouldRelease(n.Win is { IsVisible: true }, n.HiddenSince,
                now,
                Store.State.SocialBackground, n.Connected))
            {
                n.Wanted = false;
                CloseView(n);
                Store.Log("Social WebView2 libéré · réseau=" + n.Id);
                n.Count = 0;
                countsDirty = true;
                Changed();
                ReleaseEnvironmentIfIdle();
            }
        try
        {
            var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            if (!GlobalMemoryStatusEx(ref status))
                return;
            if (!lowMemory && status.AvailPhys < LowMemory)
            {
                lowMemory = true;
                foreach (var n in nets)
                    if (n.Wv != null && n.Win is { IsVisible: false } && !n.Suspended)
                        _ = SuspendAsync(n);
            }
            else if (lowMemory && status.AvailPhys > RecoveredMemory)
            {
                lowMemory = false;
                foreach (var n in nets)
                    if (n.Wv != null && n.Suspended)
                    {
                        try
                        {
                            n.Wv.Resume();
                        }
                        catch (Exception err)
                        {
                            Store.Log("SocialService.SlowTick : " + err.GetType().Name + ": " + err.Message);
                        }
                        n.Suspended = false;
                    }
            }
        }
        catch (Exception ex) { Store.Log("Social mémoire: " + ex.Message); }
    }
}
