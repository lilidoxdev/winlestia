// Process entry point and command line switches.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace PanneauxGlass;

internal static class Program
{
    internal static EventWaitHandle ShowSignal = null!,
        QuitSignal = null!,
        TaskbarRestoreSignal = null!;
    internal static bool DockPreview;
    internal static bool QtMode;
    internal static bool QtVerify;
    internal static bool QtVerifyTools;
    internal static bool QtReview;
    internal static string? QtRoot;
    internal static string? ThemeOverride;

    [STAThread]
    internal static int Main(string[] args)
    {
        Store.Root = AppDomain.CurrentDomain.BaseDirectory;
        QtMode =
            !args.Contains("--legacy-shell")
            && (args.Contains("--qt-shell") || File.Exists(Path.Combine(Store.Root, "caelestia.enabled")));
        QtReview = args.Contains("--qt-review");
        QtVerify = args.Contains("--qt-verify");
        QtVerifyTools = args.Contains("--qt-tools-check");
        QtRoot = args.FirstOrDefault(a => a.StartsWith("--qt-root="))?.Split('=', 2)[1];
        DockPreview = args.Contains("--dock-preview");
        if (args.Length >= 3 && args[0] == "--taskbar-guard" && int.TryParse(args[1], out var parentId))
            return TaskbarNative.Guard(parentId, args[2]);
        if (args.Contains("--restore-taskbar"))
        {
            try
            {
                using var recovery = EventWaitHandle.OpenExisting(@"Local\PanneauxGlassTaskbarRestore");
                recovery.Set();
            }
            catch { }
            TaskbarNative.RestoreSaved();
            return 0;
        }
        Native.Layered = !args.Contains("--legacy-glass");
        ThemeOverride = args.FirstOrDefault(a => a.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase))
            ?.Split('=', 2)[1];
        if (args.Contains("--taskbar-state"))
        {
            var snapshot = TaskbarNative.Snapshot();
            File.WriteAllText(
                Path.Combine(Store.Root, "taskbar-state.json"),
                JsonSerializer.Serialize(
                    new
                    {
                        At = DateTimeOffset.Now,
                        snapshot.State,
                        Visible = snapshot.Visible.Count,
                        Bars = TaskbarNative
                            .ExplorerBars()
                            .Select(h => new { Hwnd = h.ToInt64(), Visible = TaskbarNative.IsWindowVisible(h) }),
                        WorkAreas = Forms.Screen.AllScreens.Select(s => new
                        {
                            Screen = s.DeviceName,
                            s.Bounds,
                            s.WorkingArea,
                        }),
                    },
                    Store.Json
                )
            );
            return 0;
        }
        if (args.Contains("--probe-pc"))
        {
            using var pc = new PcTelemetrySource();
            var readings = new List<PcReading>();
            for (int i = 0; i < 4; i++)
            {
                readings.Add(pc.Read());
                Thread.Sleep(1000);
            }
            File.WriteAllText(
                Path.Combine(Store.Root, "probe-pc.json"),
                JsonSerializer.Serialize(readings, Store.Json)
            );
            return 0;
        }
        if (args.Length >= 2 && args[0] == "--capture-frames")
            return WgcCapture.Cli(args);
        if (args.Length >= 3 && args[0] == "--extract-frames")
            return WallpaperMedia.ExtractFramesCli(args);
        if (args.Contains("--probe-wallpaper"))
        {
            using var wallpaper = new WindowsWallpaper();
            for (int i = 0; i < 30 && wallpaper.Monitors.Length == 0; i++)
                Thread.Sleep(100);
            File.WriteAllText(
                Path.Combine(Store.Root, "wallpaper-probe.json"),
                JsonSerializer.Serialize(wallpaper.Monitors, Store.Json)
            );
            return 0;
        }
        if (args.Contains("--verify-screen-frame"))
            return FrameVerification.Run();
        if (args.Contains("--verify-tiling-latency"))
            return TilingLatencyVerification.Run();
        if (args.Contains("--verify-social-count"))
            return SocialCountVerification.Run();
        if (args.Contains("--verify-dock-preview"))
            return DockPreviewVerification.Run(args);
        if (args.Contains("--verify-audio-mixer"))
            return AudioMixerVerification.Run();
        if (args.Contains("--verify-qt-keys"))
            return QtShellVerification.Run();
        if (args.Contains("--verify-shortcuts"))
        {
            var verificationApp = new System.Windows.Application();
            return InteractionVerification.Shortcuts();
        }
        if (args.Contains("--verify-controls") || args.Contains("--verify-controls-write"))
            return ControlVerification.Run(args.Contains("--verify-controls-write")).GetAwaiter().GetResult();
        if (args.Contains("--verify-shell"))
            return ShellVerification.Run();
        if (args.Contains("--verify-workspace"))
            return WorkspaceVerification.Run();
        if (args.Contains("--verify-tools"))
            return VerificationTools.Run();
        if (args.Contains("--verify"))
            return Verification.Run();
        if (args.Contains("--verify-web"))
            return Verification.Web();
        if (args.Contains("--restore"))
        {
            Process.Start(
                new ProcessStartInfo(Path.Combine(Store.Root, "SECOURS_BUREAU.exe")) { UseShellExecute = true }
            );
            return 0;
        }
        bool first;
        using var mutex = new Mutex(true, @"Local\PanneauxGlassV5", out first);
        ShowSignal = new(false, EventResetMode.AutoReset, @"Local\PanneauxGlassV5Show");
        QuitSignal = new(false, EventResetMode.AutoReset, @"Local\PanneauxGlassV5Quit");
        TaskbarRestoreSignal = new(false, EventResetMode.AutoReset, @"Local\PanneauxGlassTaskbarRestore");
        Store.Log("Args " + Environment.CommandLine + " · pid=" + Environment.ProcessId + " · premier=" + first);
        if (!first)
        {
            if (args.Contains("--quit"))
                QuitSignal.Set();
            else
                ShowSignal.Set();
            return 0;
        }
        if (args.Contains("--quit"))
            return 0;
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Coordinator? coordinator = null;
        if (args.Contains("--diagnostic"))
        {
            var test = new Window
            {
                Title = "Panneaux Glass — diagnostic WPF",
                Width = 600,
                Height = 360,
                Background = Brushes.SteelBlue,
                Content = UI.Text("Rendu WPF", 36),
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
            };
            test.Closed += (_, _) => app.Shutdown();
            app.Run(test);
            return 0;
        }
        app.DispatcherUnhandledException += (_, e) =>
        {
            Store.Log("UI: " + e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Store.Log("FATAL: " + e.ExceptionObject);
        app.Startup += (_, _) =>
        {
            try
            {
                if (args.Contains("--corner-proof"))
                {
                    CornerProof.Show();
                    return;
                }
                bool offscreen = args.Contains("--render-themes") || args.Contains("--render-palette");
                if (offscreen)
                {
                    GlassWindow.CaptureOffset = new Vector(-40000, -40000);
                    if (!args.Contains("--qa-ui"))
                        args = args.Append("--qa-ui").ToArray();
                    if (!args.Contains("--preview"))
                        args = args.Append("--preview").ToArray();
                }
                coordinator = new Coordinator(
                    args.Contains("--preview"),
                    args.Contains("--qa-ui"),
                    args.Contains("--export-preview")
                );
                if (offscreen)
                {
                    string? only = args.FirstOrDefault(a => a.StartsWith("--only=", StringComparison.OrdinalIgnoreCase))
                        ?.Split('=', 2)[1];
                    if (args.Contains("--render-themes"))
                        ThemeRender.Themes(coordinator, only);
                    else
                        ThemeRender.Palette(
                            coordinator,
                            args.FirstOrDefault(a => a.StartsWith("--query=", StringComparison.OrdinalIgnoreCase))
                                ?.Split('=', 2)[1]
                                ?? "chrome"
                        );
                }
                if (args.Contains("--show") && !args.Contains("--preview"))
                    coordinator.Open();
                if (args.Contains("--workspace"))
                    coordinator.ShowWorkspace();
                if (args.Contains("--controls"))
                {
                    if (!args.Contains("--preview") && !args.Contains("--show"))
                        coordinator.Open();
                    coordinator.ToggleControls();
                }
                if (args.Contains("--pc"))
                {
                    if (!args.Contains("--preview") && !args.Contains("--show"))
                        coordinator.Open();
                    coordinator.TogglePc();
                }
                if (args.Contains("--verify-shell-live"))
                    ShellLiveCheck.Run(coordinator);
                if (args.Contains("--verify-interactions"))
                    InteractionVerification.Run(coordinator);
                if (args.Contains("--preview-palette"))
                    coordinator.PreviewPalette();
                if (args.Contains("--preview-notes"))
                    coordinator.PreviewNotes();
                if (args.Contains("--preview-focus"))
                    coordinator.PreviewFocus();
                if (args.Contains("--preview-tabs"))
                    coordinator.PreviewTabs();
                if (args.Contains("--preview-tools"))
                    coordinator.OpenTools();
                if (args.Contains("--preview-clipboard"))
                    coordinator.OpenClipboard();
                if (args.Contains("--preview-audio"))
                    coordinator.OpenAudio();
                if (args.Contains("--preview-workspaces"))
                    coordinator.OpenWorkspaces();
                if (args.Contains("--preview-largefiles"))
                    coordinator.OpenLargeFiles();
                if (args.Contains("--preview-help"))
                    coordinator.OpenHelp();
                if (args.Contains("--preview-settings"))
                    coordinator.Settings();
                if (args.Contains("--preview-pipette"))
                    coordinator.OpenColorPicker();
                if (args.Contains("--preview-ruler"))
                    coordinator.OpenRuler();
                if (args.Contains("--preview-pin"))
                    PinWindow.OpenMemo(
                        coordinator,
                        "Penser à envoyer le devis avant vendredi.\n\nRappeler Zoé pour l’écran secondaire."
                    );
                if (args.Contains("--preview-pin-image"))
                {
                    var shot = ScreenShot.Capture(new System.Drawing.Rectangle(0, 0, 520, 320));
                    PinWindow.Open(coordinator, shot);
                }
                if (args.Contains("--theme-preview"))
                    coordinator.SetTheme(Theme.Current.Id);
                if (args.Contains("--edge-test"))
                    coordinator.ProbeRightEdge();
                if (args.Contains("--benchmark-controls"))
                    ControlPerformanceProbe.Run(coordinator);
                if (args.Contains("--benchmark-home"))
                    PerformanceProbe.Run(coordinator, "motion-home-5.2.json");
                if (args.Contains("--benchmark-workspace"))
                    PerformanceProbe.Run(coordinator, "motion-workspace-5.2.json", true);
                if (args.Contains("--benchmark"))
                    PerformanceProbe.Run(
                        coordinator,
                        args.Contains("--baseline") ? "motion-before.json" : "motion-after.json"
                    );
            }
            catch (Exception ex)
            {
                Store.Log("Démarrage: " + ex);
                System.Windows.MessageBox.Show(
                    "Panneaux Glass n’a pas démarré. Le secours reste disponible dans Documents\\SECOURS_BUREAU.exe.\n\n"
                        + ex.Message,
                    "Panneaux Glass"
                );
                app.Shutdown(1);
            }
        };
        app.Exit += (_, _) =>
        {
            FilesPreload.Quit();
            coordinator?.Dispose();
        };
        app.Startup += (_, _) =>
        {
            FilesPreload.Start();
            FilesDefaultHandler.Start();
        };
        int code = app.Run();
        mutex.ReleaseMutex();
        return code;
    }
}
