// SocialService : création et configuration des WebView2 par réseau.
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
    // Une seule création à la fois par réseau : Start, Open et Enable attendent la même tâche.
    async Task CreateAsync(Net n, bool navigate = true, string reason = "Open")
    {
        if (n.Ctl != null || disposed)
            return;
        if (n.CreateTask != null)
        {
            await n.CreateTask;
            return;
        }
        var task = CreateCore(n, navigate, reason);
        n.CreateTask = task;
        try
        {
            await task;
        }
        finally { if (ReferenceEquals(n.CreateTask, task)) n.CreateTask = null; }
    }

    async Task CreateCore(Net n, bool navigate, string reason)
    {
        if (n.Ctl != null || disposed)
            return;
        n.Creating = true;
        n.Wanted = true;
        Changed();
        try
        {
            var env = await GetEnvironment();
            if (!n.Wanted || disposed)
                return;
            EnsureWindow(n);
            var handle = new WindowInteropHelper(n.Win!).EnsureHandle();
            var options = env.CreateCoreWebView2ControllerOptions();
            options.ProfileName = n.Id;
            var ctl = await env.CreateCoreWebView2ControllerAsync(handle, options);
            if (!n.Wanted || disposed || n.Win == null)
            {
                ctl.Close();
                CloseView(n);
                return;
            }
            n.Ctl = ctl;
            n.Wv = ctl.CoreWebView2;
            n.U.Title = 0;
            n.Armed = false;
            n.Arming = false;
            n.Wv.Settings.IsStatusBarEnabled = false;
            n.Wv.Settings.IsPasswordAutosaveEnabled = false;
            n.Wv.Settings.IsGeneralAutofillEnabled = false;
            n.Wv.NewWindowRequested += OnNewWindow;
            n.Wv.IsMuted = !n.Win.IsVisible;
            n.Wv.NavigationStarting += OnNavigationStarting;
            n.Wv.NavigationCompleted += OnNavigationCompleted;
            n.Wv.SourceChanged += OnSourceChanged;
            n.Wv.DocumentTitleChanged += OnTitleChanged;
            n.Wv.PermissionRequested += OnPermission;
            n.Wv.NotificationReceived += OnNotification;
            n.Wv.ProcessFailed += OnProcessFailed;
            ctl.IsVisible = n.Win!.IsVisible;
            n.Wv.MemoryUsageTargetLevel = n.Win.IsVisible ? CoreWebView2MemoryUsageTargetLevel.Normal
                : CoreWebView2MemoryUsageTargetLevel.Low;
            SyncBounds(n);
            if (!n.Win.IsVisible)
                n.HiddenSince = DateTime.UtcNow;
            if (!slowTimer.IsEnabled)
                slowTimer.Start();
            Store.Log("Social WebView2 créé · raison=" + reason + " · réseau=" + n.Id);
            if (navigate)
                n.Wv.Navigate(n.Url);
        }
        catch (Exception ex) { Store.Log("Social vue " + n.Id + ": " + ex.Message); }
        finally { n.Creating = false; Changed(); }
    }

    void EnsureWindow(Net n)
    {
        if (n.Win != null)
            return;
        var w = new System.Windows.Window
        {
            Title = "Winlestia — " + n.Name,
            Width = 1100,
            Height = 800,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen,
            ShowInTaskbar = false,
            Background = System.Windows.Media.Brushes.Black,
            ShowActivated = true
        };
        w.Closing += (_, e) => { if (n.ForceClose) return; e.Cancel = true; HideWindow(n); };
        w.SizeChanged += (_, _) => SyncBounds(n);
        w.DpiChanged += (_, _) => SyncBounds(n);
        n.Win = w;
    }

    void SyncBounds(Net n)
    {
        if (n.Ctl == null || n.Win == null || !n.Win.IsVisible)
            return;
        try
        {
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(n.Win);
            n.Ctl.Bounds = new System.Drawing.Rectangle(0, 0, Math.Max(1, (int)Math.Round(n.Win.ActualWidth * dpi.DpiScaleX)), Math.Max(1,
                (int)Math.Round(n.Win.ActualHeight * dpi.DpiScaleY)));
        }
        catch (Exception)
        {
            // Redimensionnement très fréquent : un contrôle en cours de fermeture est ignoré.
        }
    }

    void ShowWindow(Net n)
    {
        if (n.Win == null || n.Ctl == null || n.Wv == null)
            return;
        if (n.U.ResetOnOpen())
        {
            countsDirty = true;
            QueueSave();
            Changed();
        }
        if (n.Suspended)
        {
            try
            {
                n.Wv.Resume();
            }
            catch (Exception err)
            {
                Store.Log("SocialService.ShowWindow : " + err.GetType().Name + ": " + err.Message);
            }
            n.Suspended = false;
        }
        n.Wv.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
        n.Wv.IsMuted = false;
        n.Win.ShowInTaskbar = true;
        n.Win.Show();
        if (n.Win.WindowState == System.Windows.WindowState.Minimized)
            n.Win.WindowState = System.Windows.WindowState.Normal;
        n.HiddenSince = DateTime.MinValue;
        n.Ctl.IsVisible = true;
        SyncBounds(n);
        n.Win.Activate();
        try
        {
            n.Ctl.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
        }
        catch (Exception ex)
        {
            Store.Log("SocialService.ShowWindow : " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    void HideWindow(Net n)
    {
        if (n.Win == null)
            return;
        n.Win.ShowInTaskbar = false;
        n.Win.Hide();
        n.HiddenSince = DateTime.UtcNow;
        try
        {
            if (n.Ctl != null)
                n.Ctl.IsVisible = false;
            if (n.Wv != null)
            {
                n.Wv.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
                n.Wv.IsMuted = true;
            }
        }
        catch (Exception ex)
        {
            Store.Log("SocialService.HideWindow : " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    // Plus aucune vue et pas d'arrière-plan : lâche l'environnement pour que les processus msedgewebview2 se terminent ; GetEnvironment le
    // recrée.
    void ReleaseEnvironmentIfIdle()
    {
        if (disposed || Store.State.SocialBackground || environment == null || nets.Any(x => x.Ctl != null || x.Creating
            || x.CreateTask != null))
            return;
        environment = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Store.Log("Social environnement WebView2 libéré");
    }

    // Ferme la vue et sa fenêtre (désabonne tout, libère le contrôleur). Le profil sur disque reste sauf déconnexion.
    void CloseView(Net n)
    {
        var wv = n.Wv;
        var ctl = n.Ctl;
        var win = n.Win;
        n.Wv = null;
        n.Ctl = null;
        n.Win = null;
        n.Suspended = false;
        n.Navigating = false;
        n.Armed = false;
        n.Arming = false;
        n.U.CancelZero();
        Safe.Try(() =>
        {
            if (wv != null)
            {
                wv.NewWindowRequested -= OnNewWindow;
                wv.NavigationStarting -= OnNavigationStarting;
                wv.NavigationCompleted -= OnNavigationCompleted;
                wv.SourceChanged -= OnSourceChanged;
                wv.DocumentTitleChanged -= OnTitleChanged;
                wv.PermissionRequested -= OnPermission;
                wv.NotificationReceived -= OnNotification;
                wv.ProcessFailed -= OnProcessFailed;
            }
        });
        Safe.Try(() => ctl?.Close());
        if (win != null)
        {
            n.ForceClose = true;
            Safe.Try(() => win.Close());
            n.ForceClose = false;
        }
        if (nets.All(x => x.Ctl == null))
            slowTimer.Stop();
    }

    // ---- Événements WebView2 ---------------------------------------------------------------------------------
    Net? Owner(object? sender) => sender is CoreWebView2 wv ? nets.FirstOrDefault(n => ReferenceEquals(n.Wv, wv)) : null;

    void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        var n = Owner(sender);
        if (n == null)
            return;
        if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !IsAllowed(n, uri))
        {
            e.Cancel = true;
            OpenExternal(uri);
            return;
        }
        n.Navigating = true;
        Changed();
    }

    void OnNewWindow(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        var n = Owner(sender);
        if (n == null)
            return;
        e.Handled = true;
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
            return;
        if (IsAllowed(n, uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            n.Wv?.Navigate(uri.AbsoluteUri);
        else
            OpenExternal(uri);
    }

    static bool IsAllowed(Net n, Uri u) => n.Allowed.Any(h => u.Host.Equals(h, StringComparison.OrdinalIgnoreCase) || u.Host.EndsWith("."
        + h, StringComparison.OrdinalIgnoreCase));

    static void OpenExternal(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return;
        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception ex) { Store.Log("Social lien externe: " + ex.Message); }
    }

    void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        var n = Owner(sender);
        if (n == null)
            return;
        n.Navigating = false;
        EvaluateUrl(n);
        MaybeArm(n);
        Changed();
        _ = CheckLater(n, 3000);
    }

    void OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
    {
        var n = Owner(sender);
        if (n != null)
            EvaluateUrl(n);
    }
}
