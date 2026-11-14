// language: C# 12, file: Coordinator.Desktop.cs, target: Windows 11 / .NET 8 WPF, icônes du bureau, barre Windows, zone de notification
// et reprise après redémarrage d'Explorer
using System;
using System.Windows;
using Forms = System.Windows.Forms;

namespace PanneauxGlass;

internal sealed partial class Coordinator
{
    readonly uint explorerCreated = TaskbarNative.RegisterWindowMessage("TaskbarCreated");
    bool taskbarDeferred;
    // TaskbarCreated : Explorer vient de (re)démarrer. La barre différée au lancement est activée, puis le service du Bureau
    // réinstalle son hook.
    void ExplorerCreated()
    {
        if (preview || disposed)
            return;
        Store.Log("Explorateur prêt (TaskbarCreated).");
        if (taskbarDeferred)
        {
            taskbarDeferred = false;
            if (Store.State.ReplaceTaskbar && taskbar != null && !taskbar.Enable())
            {
                Store.State.ReplaceTaskbar = false;
                Store.Log("La barre Windows reste active : secours indisponible.");
            }
        }
        ReassertDesktop();
    }
    void ReassertDesktop()
    {
        desktopIcons.OnExplorerCreated();
        // Les fenêtres des Winjets sont réaffirmées par le frontend (compteur d'état + évènement).
        winjets?.NotifyExplorerRestarted();
        QtShell?.Send("desktop.explorerRestarted");
    }
    // Reprise après veille ou hibernation : Explorer ne redémarre pas, mais la barre, les icônes et les calques du bureau peuvent
    // avoir été réaffichés. PowerModeChanged arrive sur un autre thread et parfois deux fois : on rebascule sur l'UI et on temporise.
    static readonly TimeSpan ResumeDelay = TimeSpan.FromSeconds(2);
    System.Windows.Threading.DispatcherTimer? resumeDebounce;
    void PowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode != Microsoft.Win32.PowerModes.Resume)
            return;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke((Action)(() =>
        {
            if (preview || disposed)
                return;
            if (resumeDebounce == null)
            {
                resumeDebounce = new System.Windows.Threading.DispatcherTimer { Interval = ResumeDelay };
                resumeDebounce.Tick += (_, _) =>
                {
                    resumeDebounce.Stop();
                    if (preview || disposed)
                        return;
                    Store.Log("Reprise après veille : barre et bureau réaffirmés.");
                    if (taskbar?.Enabled == true)
                        taskbar.Refresh();
                    ReassertDesktop();
                };
            }
            resumeDebounce.Stop();
            resumeDebounce.Start();
        }));
    }
    void StopPowerWatch()
    {
        Microsoft.Win32.SystemEvents.PowerModeChanged -= PowerModeChanged;
        resumeDebounce?.Stop();
    }
    // Après une relance du frontend : même chemin que le démarrage, mais seulement si la barre n'est pas déjà remplacée.
    internal void ResumeTaskbar()
    {
        if (!preview && !disposed && Store.State.ReplaceTaskbar && taskbar?.Enabled == false && TaskbarNative.ExplorerReady())
            taskbar.Enable();
    }
    // Après une relance réussie du frontend : barre et icônes du Bureau reprennent le chemin du démarrage (Restore() a suspendu le masquage).
    internal void ResumeShell()
    {
        ResumeTaskbar();
        if (!preview && !disposed)
            desktopIcons.Hide();
    }
    // Au lancement, la barre Windows n'est remplacée que si Explorer est prêt ; sinon TaskbarCreated reprendra la main.
    void StartTaskbar()
    {
        if (!preview && Store.State.ReplaceTaskbar)
        {
            if (!TaskbarNative.ExplorerReady())
            {
                taskbarDeferred = true;
                Store.Log("Explorateur pas encore prêt : barre Windows laissée telle quelle jusqu'à TaskbarCreated.");
            }
            else if (!taskbar!.Enable())
            {
                Store.State.ReplaceTaskbar = false;
                Store.Log("La barre Windows reste active : secours indisponible.");
            }
        }
        else if (preview && Program.DockPreview)
            taskbar!.ShowPreview();
    }
    void BuildTray()
    {
        tray.Show("Winlestia · " + UserName.First);
        // Shell Qt : l'icône de la zone de notification permet d'atteindre les réglages même sans dock
        // (le menu est affiché par le NotifyIcon lui-même au clic droit).
        if (Program.QtMode)
        {
            var qtMenu = new Forms.ContextMenuStrip();
            qtMenu.Items.Add("Ouvrir le tableau de bord", null, (_, _) => QtShell?.Send("toggle"));
            qtMenu.Items.Add("Paramètres de Winlestia", null, (_, _) => QtShell?.Send("settings"));
            qtMenu.Items.Add("Contrôles Windows", null, (_, _) => QtShell?.Send("controls"));
            qtMenu.Items.Add(new Forms.ToolStripSeparator());
            qtMenu.Items.Add("Retrouver le Bureau organisé", null, (_, _) => Rescue());
            qtMenu.Items.Add("Quitter", null, (_, _) => Application.Current.Shutdown());
            tray.SetMenu(qtMenu);
        }
        tray.RightClicked += () =>
        {
            if (QtShell != null)
                return;
            if (!state.Open)
                Open();
            GlassMenu.Open(this, DialogParent, state.Detailed ? (FrameworkElement)toolbar.ContentHost : home.ContentHost, "Winlestia",
                new[]
                {
                    new MenuAction("Ouvrir l’espace", "\uE8A9", Open),
                    new MenuAction("Commande rapide  ·  " + Keys.PaletteLabel, "\uE721", TogglePalette),
                    new MenuAction("Note rapide", "\uE70B", OpenNotes),
                    new MenuAction("Minuteur…", "\uE917", OpenFocus),
                    new MenuAction("Personnaliser", "\uE713", () => Settings(), Separator: true),
                    new MenuAction("Actualiser les accès", "\uE72C", RefreshCatalogue),
                    new MenuAction("Retrouver le Bureau organisé", "\uE777", Rescue, Separator: true),
                    new MenuAction("Quitter Winlestia", "\uE711", () => Application.Current.Shutdown())
                }, true);
        };
        tray.DoubleClicked += () => Open();
    }
    internal void RestoreDesktopIcons() => desktopIcons.Restore();
    internal void Rescue() => launcher.Rescue();
}
