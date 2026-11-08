// C# 12 / .NET 8 / Windows 11. GPL-3.0-or-later. Réseaux sociaux en arrière-plan (WebView2) : connexion, compteur de notifications, son.
// Un seul environnement WebView2 (un seul navigateur partagé), un profil par réseau (cookies persistants séparés).
// Tout s'exécute sur le dispatcher WPF ; les écritures disque (compteurs, préférences) partent sur un thread d'arrière-plan.

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
    // Intervalle entre deux essais d'armement, juste au-dessus des 7,5 s de stabilité du titre.
    const int ArmStepMs = 8000;
    // Délai avant de confirmer un compteur retombé à zéro : écarte les titres transitoires.
    const int ZeroConfirmMs = 5000;
    // Regroupe les changements de compteurs rapprochés en une seule notification vers l'interface.
    const int NotifyDebounceMs = 250;
    // Laisse le démarrage du shell se terminer avant de charger les WebView2 en arrière-plan.
    const int PreloadDelayMs = 6000;
    // Espace les créations de WebView2 pour ne pas saturer le processeur au démarrage.
    const int PreloadSpacingMs = 4000;

    [StructLayout(LayoutKind.Sequential)]
    struct MemoryStatus
    {
        public uint Length, Load; public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual,
            AvailExtendedVirtual;
    }
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

    const ulong LowMemory = 4UL << 30, RecoveredMemory = 5UL << 30;

    internal sealed class HistoryItem
    {
        public int Seq
        {
            get; set;
        }
        public string Net { get; set; } = ""; public string Title { get; set; } = ""; public string Body { get; set; } = "";
            public string Kind { get; set; } = "notif"; public long At
        {
            get; set;
        }
    }

    sealed class Net
    {
        public string Id = "", Name = "", Url = "", Login = ""; public string[] Hosts = Array.Empty<string>(); public bool Script;
        public string[] Allowed = Array.Empty<string>(); public Task? CreateTask; public HistoryItem? LastCountEntry;
        public readonly UnreadCounter U = new(); public int Count
        {
            get => U.Count; set => U.Count = value;
        }
        public bool Connected, Navigating, Creating, Wanted, Suspended, ForceClose, Armed, Arming;
            public DateTime HiddenSince = DateTime.MinValue;
        public DateTime LastNative = DateTime.MinValue, TitleChangedAt = DateTime.MinValue, NextCheck = DateTime.MinValue;
        public System.Windows.Window? Win; public CoreWebView2Controller? Ctl; public CoreWebView2? Wv;
    }

    readonly Net[] nets ={
        new(){Id="discord",Name="Discord",Url="https://discord.com/channels/@me",Login="https://discord.com/login",
            Hosts=new[]{"discord.com"},Allowed=new[]{"discord.com","discordapp.com"}},
        new(){Id="instagram",Name="Instagram",Url="https://www.instagram.com/direct/inbox/",Hosts=new[]{"instagram.com"},
            Allowed=new[]{"instagram.com","facebook.com"}},
        new(){Id="snapchat",Name="Snapchat",Url="https://web.snapchat.com/",Hosts=new[]{"snapchat.com"},Allowed=new[]{"snapchat.com",
            "accounts.snapchat.com"}},
        new(){Id="whatsapp",Name="WhatsApp",Url="https://web.whatsapp.com/",Hosts=new[]{"whatsapp.com"},Allowed=new[]{"whatsapp.com",
            "web.whatsapp.com"},Script=true},
        new(){Id="telegram",Name="Telegram",Url="https://web.telegram.org/k/",Hosts=new[]{"telegram.org"},Allowed=new[]{"telegram.org",
            "web.telegram.org","t.me"},Script=true},
        new(){Id="gmail",Name="Gmail",Url="https://mail.google.com/mail/u/0/#inbox",Hosts=new[]{"mail.google.com"},
            Allowed=new[]{"mail.google.com","accounts.google.com","www.google.com","google.com","ssl.gstatic.com","googleusercontent.com",
            "myaccount.google.com"}},
    };

    readonly Dispatcher dispatcher; readonly Action changed;
    readonly DispatcherTimer notifyTimer = new() { Interval = TimeSpan.FromMilliseconds(NotifyDebounceMs) };
    readonly DispatcherTimer slowTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    readonly string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Winlestia",
        "Social");
    readonly object saveLock = new();
    Task<CoreWebView2Environment>? environment;
    // Historique : modifié et lu uniquement sur le dispatcher (les écritures disque reçoivent une copie).
    readonly List<HistoryItem> history = new(); int nextSeq = 1;
    bool disposed, countsDirty, prefsSaveQueued, saveQueued, started, lowMemory;
    DateTime bootUtc;

    internal SocialService(Dispatcher dispatcher, Action changed)
    {
        this.dispatcher = dispatcher;
        this.changed = changed;
        var s = Store.State;
        s.SocialEnabled ??= new();
        s.SocialNetMuted ??= new();
        s.SocialConnected ??= new();
        s.SocialVolume = Math.Clamp(s.SocialVolume, 0, 100);
        bootUtc = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
        foreach (var n in nets)
        {
            n.Connected = s.SocialConnected.GetValueOrDefault(n.Id);
        }
        LoadCounts();
        notifyTimer.Tick += (_, _) => { notifyTimer.Stop(); if (!disposed) changed(); };
        slowTimer.Tick += SlowTick;
    }

    bool Enabled(Net n) => Store.State.SocialEnabled.TryGetValue(n.Id, out bool chosen) ? chosen : true;
    Net? Find(string id) => nets.FirstOrDefault(n => n.Id == id);

    // ---- État pour le front ----------------------------------------------------------------------------------
    internal object Snapshot() => new
    {
        muted = Store.State.SocialMuted,
        sound = Store.State.SocialSound,
        volume = Store.State.SocialVolume,
        history = history.Select(h => new { seq = h.Seq, net = h.Net, title = h.Title, body = h.Body, kind = h.Kind, at = h.At })
            .ToArray(),
        networks = nets.Select(n => new { id = n.Id, name = n.Name, enabled = Enabled(n), connected = n.Connected,
            muted = Store.State.SocialNetMuted.GetValueOrDefault(n.Id), count = n.Count, busy = n.Creating || n.Navigating }).ToArray()
    };

    // Coalesce les rafales : au plus un push "social" toutes les 250 ms.
    void Changed()
    {
        if (disposed)
            return;
        if (!notifyTimer.IsEnabled)
            notifyTimer.Start();
    }

    // ---- Démarrage échelonné ---------------------------------------------------------------------------------
    internal async Task Start()
    {
        if (started || disposed)
            return;
        started = true;
        try
        {
            await Task.Delay(PreloadDelayMs);
            await PreloadAsync();
        }
        catch (Exception ex) { Store.Log("Social démarrage: " + ex.Message); }
    }

    // Crée les vues des réseaux connectés, une par une. Rien si SocialBackground est faux (création à l'ouverture).
    async Task PreloadAsync()
    {
        foreach (var n in nets)
        {
            if (disposed)
                return;
            if (!SocialPolicy.ShouldPreload(Store.State.SocialBackground, Enabled(n), n.Connected))
                continue;
            if (n.Ctl == null)
                await CreateAsync(n, reason: "Preload");
            await Task.Delay(PreloadSpacingMs);
        }
    }

    // Appelé quand l'option « arrière-plan » est activée à l'exécution.
    internal void BackgroundChanged()
    {
        if (started && !disposed && Store.State.SocialBackground)
            _ = PreloadAsync();
    }

    // ---- Actions -----------------------------------------------------------------------------------------------
    internal async Task Open(string id, bool login)
    {
        var n = Find(id);
        if (n == null || disposed)
            return;
        try
        {
            if (login && !Enabled(n))
            {
                Store.State.SocialEnabled[n.Id] = true;
                Store.Save();
            }
            if (!Enabled(n))
                return;
            n.Wanted = true;
            if (n.Ctl == null)
                await CreateAsync(n, navigate: false, reason: "Open");
            if (n.Ctl == null && n.Wanted)
                await CreateAsync(n, navigate: false, reason: "Open");
            if (n.Ctl == null || n.Wv == null || n.Win == null || disposed)
                return;
            ShowWindow(n);
            string target = login && !n.Connected && n.Login.Length > 0 ? n.Login : n.Url;
            if (!OnService(n, n.Wv.Source) || (login && !n.Connected && n.Login.Length > 0 && !SamePath(n.Wv.Source, n.Login)
                && !OnLoginPage(n)))
                n.Wv.Navigate(target);
            Changed();
        }
        catch (Exception ex) { Store.Log("Social ouverture: " + ex.Message); }
    }

    internal async Task Enable(string id, bool enabled)
    {
        var n = Find(id);
        if (n == null)
            return;
        Store.State.SocialEnabled[n.Id] = enabled;
        Store.Save();
        if (enabled)
        {
            if (n.Connected)
            {
                n.Wanted = true;
                await CreateAsync(n, reason: "Enable");
                if (n.Ctl == null && n.Wanted)
                    await CreateAsync(n, reason: "Enable");
            }
        }
        else
        {
            n.Wanted = false;
            CloseView(n);
        }
        Changed();
    }

    internal async Task Logout(string id)
    {
        var n = Find(id);
        if (n == null)
            return;
        n.Wanted = false;
        try
        {
            if (n.Wv != null)
            {
                var profile = n.Wv.Profile;
                await ClearAsync(profile);
                CloseView(n);
            }
            else
            {
                // Pas de vue ouverte : on en ouvre une, invisible, juste pour atteindre le profil.
                var env = await GetEnvironment();
                var opt = env.CreateCoreWebView2ControllerOptions();
                opt.ProfileName = n.Id;
                var host = new System.Windows.Window { ShowInTaskbar = false, Width = 1, Height = 1,
                    WindowStyle = System.Windows.WindowStyle.None };
                var handle = new WindowInteropHelper(host).EnsureHandle();
                var ctl = await env.CreateCoreWebView2ControllerAsync(handle, opt);
                try
                {
                    await ClearAsync(ctl.CoreWebView2.Profile);
                }
                finally { ctl.Close(); host.Close(); }
            }
        }
        catch (Exception ex) { Store.Log("Social déconnexion: " + ex.Message); }
        n.U.Reset();
        SetConnected(n, false);
        countsDirty = true;
        QueueSave();
        Changed();
    }

    static Task ClearAsync(CoreWebView2Profile profile) => profile.ClearBrowsingDataAsync();

    internal void MuteNet(string id, bool muted)
    {
        var n = Find(id);
        if (n == null)
            return;
        Store.State.SocialNetMuted[n.Id] = muted;
        Store.Save();
        Changed();
    }
    internal void GlobalMute(bool muted)
    {
        Store.State.SocialMuted = muted;
        Store.Save();
        Changed();
    }
    internal void SoundEnabled(bool enabled)
    {
        Store.State.SocialSound = enabled;
        Store.Save();
        Changed();
    }
}
