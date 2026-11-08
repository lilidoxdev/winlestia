// C# 12 / .NET 8 / Windows 11. GPL-3.0-or-later. Réseaux sociaux en arrière-plan (WebView2) : connexion, compteur de notifications, son.

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

internal sealed partial class SocialService : IDisposable
{
    internal void Volume(double value)
    {
        Store.State.SocialVolume = (int)Math.Round(Math.Clamp(value, 0, 100));
        Store.Save();
        Changed();
    }
    internal void HistoryClear()
    {
        if (history.Count == 0)
            return;
        history.Clear();
        foreach (var n in nets)
            n.LastCountEntry = null;
        countsDirty = true;
        QueueSave();
        Changed();
    }
    internal void TestSound() => SocialChime.Play(Store.State.SocialVolume);

    // ---- Environnement et vues -------------------------------------------------------------------------------
    Task<CoreWebView2Environment> GetEnvironment()
    {
        if (environment == null || environment.IsFaulted || environment.IsCanceled)
        {
            Directory.CreateDirectory(folder);
            environment = CoreWebView2Environment.CreateAsync(null, Path.Combine(folder, "WebView2"), null);
        }
        return environment;
    }

    // Compteur = non-lus réels : pour un réseau dont le titre affiche un nombre, le titre fait foi (Count = titre).
    // Une chute à 0 n'efface le badge qu'après 5 s (une transition SPA ne compte pas). Son + historique seulement sur une vraie hausse.
    void OnTitleChanged(object? sender, object e)
    {
        var n = Owner(sender);
        if (n == null || n.Wv == null)
            return;
        int? parsed = SocialRules.ParseUnread(n.Id, n.Wv.DocumentTitle ?? "", n.Wv.Source);
        var now = DateTime.UtcNow;
        n.TitleChangedAt = now;
        if (parsed == null)
            return;
        var r = n.U.OnTitle(parsed.Value, now);
        if (r.Changed)
        {
            countsDirty = true;
            QueueSave();
            Changed();
        }
        if (r.Rise > 0 && n.Armed && !r.AfterNotif)
        {
            int d = r.Rise;
            Chime(n);
            n.LastCountEntry = AddHistory(n, "count", d == 1 ? "1 nouveau message" : d + " nouveaux messages", "");
        }
        if (r.ZeroToken > 0)
            _ = ResetLater(n, r.ZeroToken);
        if (n.Script && now >= n.NextCheck)
            _ = CheckLater(n, 500);
    }

    async Task ResetLater(Net n, int token)
    {
        try
        {
            await Task.Delay(ZeroConfirmMs);
        }
        catch { return; }
        if (!disposed && n.U.ZeroExpired(token))
        {
            countsDirty = true;
            QueueSave();
            Changed();
        }
    }

    // Absorption des non-lus initiaux : au premier état connecté dont le titre est stable 8 s, ce qui est affiché devient le niveau de
    // départ sans compter.
    void MaybeArm(Net n)
    {
        if (n.Armed || n.Arming || !n.Connected || n.Wv == null || disposed)
            return;
        n.Arming = true;
        _ = ArmAsync(n);
    }

    async Task ArmAsync(Net n)
    {
        try
        {
            for (int i = 0; i < 6; i++)
            {
                await Task.Delay(ArmStepMs);
                if (disposed || n.Wv == null || !n.Connected)
                    return;
                if ((DateTime.UtcNow - n.TitleChangedAt).TotalSeconds >= 7.5)
                    break;
            }
            if (disposed || n.Wv == null || !n.Connected)
                return;
            if (n.U.Absorb())
            {
                countsDirty = true;
                QueueSave();
                Changed();
            }
            n.Armed = true;
        }
        finally { n.Arming = false; }
    }

    void OnPermission(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        var n = Owner(sender);
        if (n == null || e.PermissionKind != CoreWebView2PermissionKind.Notifications)
            return;
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && HostMatches(n, uri))
        {
            e.State = CoreWebView2PermissionState.Allow;
            e.Handled = true;
        }
    }

    void OnNotification(object? sender, CoreWebView2NotificationReceivedEventArgs e)
    {
        var n = Owner(sender);
        if (n == null)
            return;
        e.Handled = true; // pas de toast Windows : seul le compteur et le son de Winlestia comptent
        var now = DateTime.UtcNow;
        n.LastNative = now;
        string title = SocialRules.Clip(e.Notification?.Title, 120), body = SocialRules.Clip(e.Notification?.Body, 140);
        // La hausse de titre (+d) a déjà compté d messages : chaque notification arrivée dans les 3 s consomme un de ces d crédits
        // (pas de double compte) ; au-delà, c'est une vraie notification en plus et elle compte normalement.
        if (n.U.ConsumeCredit(now))
        {
            var entry = n.LastCountEntry;
            n.LastCountEntry = null;
            if (entry != null && history.Contains(entry))
            {
                entry.Kind = "notif";
                entry.Title = title;
                entry.Body = body;
                countsDirty = true;
                QueueSave();
                Changed();
            }
            else
                AddHistory(n, "notif", title, body);
            return;
        }
        n.U.NoteNotification(now);
        // Réseau dont le titre fait foi : la notification ne compte pas (le titre suit). Sinon +1.
        if (!n.U.TitleSeen)
            Add(n, 1);
        AddHistory(n, "notif", title, body);
    }

    void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
    {
        var n = Owner(sender);
        Store.Log("Social processus " + (n?.Id ?? "?") + ": " + e.ProcessFailedKind);
    }

    HistoryItem AddHistory(Net n, string kind, string title, string body)
    {
        var item = new HistoryItem { Seq = nextSeq++, Net = n.Id, Title = title, Body = body, Kind = kind,
            At = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        history.Insert(0, item);
        if (history.Count > 100)
            history.RemoveRange(100, history.Count - 100);
        countsDirty = true;
        QueueSave();
        Changed();
        return item;
    }

    // ---- Comptage et son --------------------------------------------------------------------------------------
    void Add(Net n, int delta)
    {
        if (delta <= 0 || disposed)
            return;
        n.Count = (int)Math.Min(999999L, (long)n.Count + delta);
        countsDirty = true;
        QueueSave();
        Changed();
        Chime(n);
    }

    void Chime(Net n)
    {
        if (disposed)
            return;
        var s = Store.State;
        if (s.SocialSound && !s.SocialMuted && !s.SocialNetMuted.GetValueOrDefault(n.Id))
            SocialChime.Play(s.SocialVolume);
    }

    async Task SuspendAsync(Net n)
    {
        try
        {
            var wv = n.Wv;
            if (wv == null)
                return;
            if (await wv.TrySuspendAsync() && ReferenceEquals(n.Wv, wv))
                n.Suspended = true;
        }
        catch (Exception ex) { Store.Log("Social veille " + n.Id + ": " + ex.Message); }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        notifyTimer.Stop();
        slowTimer.Stop();
        foreach (var n in nets)
            CloseView(n);
        lock (saveLock)
        {
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(CountsFile, JsonSerializer.Serialize(MakeFile()));
            }
            catch (Exception err) when (err is IOException or UnauthorizedAccessException or JsonException)
            {
                Store.Log("SocialService.Dispose : " + err.GetType().Name + ": " + err.Message);
            }
        }
        environment = null;
    }
}
