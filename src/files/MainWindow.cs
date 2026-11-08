using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using PanneauxGlass;

namespace WinlestiaFiles;

// Étape 2 : onglets avec historique propre, fil d'Ariane/adresse modifiable, barre latérale, liste détaillée virtualisée, ligne
// d'état.
public sealed partial class MainWindow : Window
{
    // Durée d'affichage de l'info-bulle : le temps de lire une courte phrase.
    const int ToastMs = 2500;
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    sealed class TabView
    {
        internal readonly FolderTab Tab = new();
        internal Border Pill = null!;
        internal TextBlock Title = null!;
        internal bool Hot, Drop;
    }

    readonly List<TabView> tabs = new();
    TabView activeView = null!;
    FolderTab Active => activeView.Tab;
    readonly StackPanel tabStrip = new() { Orientation = Orientation.Horizontal };
    readonly BulkListBox list = new();
    readonly Border content = new();
    readonly TextBlock status = UI.Text("", M3.FontSmall, UI.Label);
    readonly TextBlock[] chevrons = new TextBlock[4];
    Grid listHost = null!;
    FrameworkElement headerRow = null!;
    Button sortButton = null!;
    Border addressPill = null!;
    Button back = null!, forward = null!, up = null!, plus = null!;
    StackPanel crumbs = null!;
    ScrollViewer crumbScroll = null!;
    TextBox addressBox = null!;
    Sidebar sidebar = null!;
    bool containersLogged;
    string shownKey = "", crumbPath = "";
    readonly bool reduced = M3.ReducedEffects;

    internal MainWindow(string? path)
    {   // null: hidden preload, no tab yet
        Perf.Mark("ctor begin");
        Title = "Winlestia Fichiers" + (path == null ? "" : " - " + path);
        WindowStyle = WindowStyle.None;
        Width = 1100;
        Height = 700;
        MinWidth = 760;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = M3.Body;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;     // crisp 1 px edges at 125 % / 150 %
        try
        {
            Icon = System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/winlestia-files.ico"),
                                                                   System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                                                                   System.Windows.Media.Imaging.BitmapCacheOption.OnDemand);
        }
        catch (Exception ex)
        {
            App.LogLine("MainWindow.MainWindow : " + ex.GetType().Name + ": " + ex.Message);
        }
        Background = UI.T(M3Role.Surface, reduced ? (byte)255 : (byte)0xEE);
        WindowChrome.SetWindowChrome(this,
                                     new WindowChrome
                                     {
                                         CaptionHeight = 48,
                                         ResizeBorderThickness = new Thickness(6),
                                         GlassFrameThickness = reduced ? new Thickness(0) : new Thickness(-1),
                                         CornerRadius = new CornerRadius(0),
                                         UseAeroCaptionButtons = false
                                     });
        SourceInitialized += (_, _) =>
        {
            Thumbs.Dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            Perf.Mark("SourceInitialized");
            ApplyDwm();
            Perf.Mark("dwm applied");
        };
        InputBindings.Add(new KeyBinding(new Relay(() => PathRouter.LaunchExplorer(Active.Location)), Key.E,
            ModifierKeys.Control | ModifierKeys.Shift));

        Perf.Mark("chrome set");
        FileItem.UseMaterial = M3.Symbols != null;
        Resources["IconFont"] = M3.Symbols ?? new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        Perf.Mark("symbols font");
        Resources.MergedDictionaries.Add(Styles.Load());
        Perf.Mark("Styles.Load");

        var root = new Grid { Margin = new Thickness(M3.Sp2) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var tabRow = TabRow();
        Perf.Mark("TabRow");
        var navRow = NavRow();
        Grid.SetRow(navRow, 1);
        Grid.SetRow(tabRow, 0);
        root.Children.Add(tabRow);
        root.Children.Add(navRow);

        Perf.Mark("tab+nav rows");
        sidebar = new Sidebar(OpenFromSidebar, OpenPinnedFile);
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(DeviceHook);
        Activated += (_, _) => sidebar.RecheckThrottled();
        Perf.Mark("sidebar");
        var body = new Grid { Margin = new Thickness(0, M3.Sp1, 0, 0) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(M3.Sp2) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(sidebar.Root, 0);
        var main = MainCard();
        Grid.SetColumn(main, 2);
        body.Children.Add(sidebar.Root);
        body.Children.Add(main);
        Grid.SetRow(body, 2);
        root.Children.Add(body);
        Perf.Mark("main card");
        Content = root;
        Perf.Mark("BuildUi done");

        if (Perf.Enabled)
        {
            Perf.Stall = new StallMonitor();
            ContentRendered += (_, _) =>
            {
                Perf.Mark("ContentRendered");
                Perf.FirstFrame();
            };
            ContentRendered += (_, _) =>
                Perf.Log($"process start -> first window render (ContentRendered) " +
                    $"{(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds:F0} ms");
        }
        Perf.Mark("before AddTab");
        if (path != null)
            OpenFirstTab(path);
        Perf.Mark("after AddTab");
        PaletteLoader.Start(Dispatcher, ApplyDwm);
        WireOps();
        WireDnd();
        WireRubber();
        sidebar.DriveMenu = p => ShowContextMenu(null, p);
        sidebar.DrivesChanged += () => { if (activeView?.Tab is { } t && ThisPc.Is(t.Location) && thisPcWrap != null) FillThisPc(); };
        InitWatch();
        if (App.Resident)
            FileOps.Idle += () => Dispatcher.BeginInvoke(TryRecycle);
#if WINLESTIA_SELFTEST
        StartSelfTest();
#endif
    }

    // L'énumération tourne en parallèle du reste de la construction ; les lignes mises en file dans les 60 ms entrent dans la première
    // passe de mise en page.
    void OpenFirstTab(string path)
    {
        AddTab(path);
        if (Active.FirstPosted.Wait(60))
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
    }

    // ---- resident mode ---------------------------------------------------------------------------------------

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool SetProcessWorkingSetSize(IntPtr process, IntPtr min, IntPtr max);

    // Préchargement caché : crée le HWND et exécute une mesure/disposition de ListBox avec de fausses lignes pour que le premier vrai
    // Show() n'ait plus rien à compiler à la volée.
    internal void Preload()
    {
        new WindowInteropHelper(this).EnsureHandle();
        if (Content is FrameworkElement root)
        {
            var data = new List<FileItem>();
            for (int i = 0; i < 40; i++)
                data.Add(new FileItem("f" + i, "f" + i, false, 1, DateTime.Now, "Fichier"));
            list.ItemsSource = data;
            var size = new Size(Width, Height);
            root.Measure(size);
            root.Arrange(new Rect(size));
            root.UpdateLayout();
            list.ItemsSource = null;
        }
        Dispatcher.BeginInvoke(Trim, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    // Demande d'ouverture venue du canal (thread UI). Caché : retour à un seul onglet neuf. Visible : l'onglet existant de ce dossier,
    // sinon un onglet de plus (max MaxTabs).
    // Jamais réentrant : OpenFirstTab peut pomper le dispatcher, et une demande traitée dans ce pompage imbriquerait un autre ShowAt (un
    // niveau de pile de plus par
    // demande en file : c'est le 0xC00000FD vu avec ~450 ouvertures rapides). Un appel imbriqué est mis de côté et exécuté par le
    // plus externe.
    internal void ShowAt(string path)
    {
        if (closed)
        {
            App.LogLine("ShowAt ignored: window already closed");
            return;
        }
        if (showing)
        {
            if (deferred.Count < MaxTabs)
                deferred.Enqueue(path);
            return;
        }
        showing = true;
        try
        {
            ShowAtCore(path);
            while (deferred.Count > 0 && !closed)
                ShowAtCore(deferred.Dequeue());
        }
        finally { showing = false; }
    }

    void ShowAtCore(string path)
    {
        Perf.Log("ShowAt " + (IsVisible ? "visible" : "hidden"));
        if (!IsVisible)
        {
            DropTabs();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            OpenFirstTab(path);
            if (closed)
                return;
            try
            {
                Show();
            }
            catch (InvalidOperationException ex)
            {   // fenêtre fermée entre la vérification et Show() : ce n'est pas un plantage, un nouveau processus prend la requête
                App.LogLine("ShowAt: Show after close (" + ex.Message + "), handed to a fresh process");
                closed = true;
                try
                {
                    (Application.Current as App)?.ReleasePipe();
                    // l'analyseur n'a pas de séparateur "--" et joint tous les arguments en un seul chemin : un processus par chemin,
                    // chemins préfixés par '-' rendus absolus
                    var all = new List<string> { path };
                    all.AddRange(deferred);
                    deferred.Clear();
                    foreach (var p in all)
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
                        psi.ArgumentList.Add(p.StartsWith('-') ? System.IO.Path.GetFullPath(p) : p);
                        System.Diagnostics.Process.Start(psi)?.Dispose();
                    }
                }
                catch (Exception err) when (err is System.IO.IOException or UnauthorizedAccessException or System.ComponentModel
                    .Win32Exception or InvalidOperationException)
                {
                    App.LogLine("MainWindow.ShowAtCore : " + err.GetType().Name + ": " + err.Message);
                }
                return;
            }
            // une seule fois, hors du thread UI : prend en compte un changement de thème/fond fait pendant que la fenêtre était
            // masquée
            PaletteLoader.Start(Dispatcher, ApplyDwm);
        }
        else
        {
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            var same = tabs.Find(v => string.Equals(v.Tab.Location.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase));
            if (same != null)
            {
                if (same != activeView)
                    Activate(same);
            }
            else
                AddTab(path);
        }
        if (closed)
            return;
        Activate();
        SetForegroundWindow(new WindowInteropHelper(this).Handle);
    }

    // ---- plafond d'onglets et info-bulle intégrée ----------------------------------------------------------------------------

    internal const int MaxTabs = 50;
    bool closed, showing;
    readonly Queue<string> deferred = new();
    Border? toast;
}
