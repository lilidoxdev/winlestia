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

    static readonly Regex TitleCount = new(@"^\s*\(([\d\s\u00a0\u202f,.]*\d\+?)\)", RegexOptions.Compiled);
    static readonly Regex TitleCountGmail = new(@"\(([\d\s\u00a0\u202f,.]*\d\+?)\)\s*$", RegexOptions.Compiled);
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

    // null = titre non informatif (Gmail : mail ouvert, autre vue) : on ne touche à rien.
    static readonly HashSet<string> GmailLists = new(StringComparer.OrdinalIgnoreCase) { "", "inbox", "starred", "snoozed", "sent",
        "drafts", "imp", "chats", "scheduled", "all", "spam", "trash", "category", "label", "search" };
    static readonly Regex GmailMessageId = new("^[A-Za-z0-9]{16,}$", RegexOptions.Compiled);

    // Vue de liste Gmail (#inbox, #category/..., #label/x, #search/...) sans identifiant de message en dernier segment.
    static bool GmailListView(string? source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var u))
            return false;
        string[] parts = u.Fragment.TrimStart('#').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return true;
        if (!GmailLists.Contains(parts[0]))
            return false;
        return !(parts.Length >= 2 && GmailMessageId.IsMatch(parts[^1]));
    }

    static int? ParseUnread(string id, string title, string? source = null)
    {
        if (id == "gmail")
        {
            if (!GmailListView(source) || !title.TrimEnd().EndsWith("Gmail", StringComparison.Ordinal))
                return null;
            var g = TitleCountGmail.Match(title.Split(" - ")[0]);
            if (!g.Success)
                return 0;
            string gd = new string(g.Groups[1].Value.Where(char.IsDigit).ToArray());
            return int.TryParse(gd, out int gv) ? Math.Min(gv, 99999) : null;
        }
        var m = TitleCount.Match(title);
        if (m.Success)
        {
            string digits = new string(m.Groups[1].Value.Where(char.IsDigit).ToArray());
            if (int.TryParse(digits, out int v))
                return Math.Min(v, 99999);
            return 1;
        }
        string t = title.TrimStart();
        return t.Length > 0 && "•*●".IndexOf(t[0]) >= 0 ? 1 : 0;
    }

    // Compteur = non-lus réels : pour un réseau dont le titre affiche un nombre, le titre fait foi (Count = titre).
    // Une chute à 0 n'efface le badge qu'après 5 s (une transition SPA ne compte pas). Son + historique seulement sur une vraie hausse.
    void OnTitleChanged(object? sender, object e)
    {
        var n = Owner(sender);
        if (n == null || n.Wv == null)
            return;
        int? parsed = ParseUnread(n.Id, n.Wv.DocumentTitle ?? "", n.Wv.Source);
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
        string title = Clip(e.Notification?.Title, 120), body = Clip(e.Notification?.Body, 140);
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

    static string Clip(string? text, int max)
    {
        text = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length > max ? text[..max] : text;
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
