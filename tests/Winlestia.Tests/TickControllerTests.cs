// C#12 / TickController avec un TickHost factice : ordre des signaux (barre Windows > arrêt > bascule), sortie anticipée quand Qt
// est inactif, suspension du repli automatique et délai d'éloignement. Les signaux de Program sont remplacés par des événements
// anonymes ; le rail n'est jamais touché sur ces chemins, donc aucune fenêtre n'est créée.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using PanneauxGlass;
namespace Winlestia.Tests;
public class TickControllerTests
{
    sealed class Rig : IDisposable
    {
        internal readonly List<string> Calls = new();
        internal readonly ShellState State = new();
        internal readonly TickController Controller;
        internal bool QtIdle;
        internal readonly EventWaitHandle Restore = new(false, EventResetMode.AutoReset);
        internal readonly EventWaitHandle Quit = new(false, EventResetMode.AutoReset);
        internal readonly EventWaitHandle Show = new(false, EventResetMode.AutoReset);
        readonly EventWaitHandle oldRestore = Program.TaskbarRestoreSignal, oldQuit = Program.QuitSignal, oldShow = Program.ShowSignal;
        readonly TempDir dir = new();
        readonly Preferences prefs = Store.State;
        readonly string? env = Environment.GetEnvironmentVariable("WINLESTIA_DATA_DIR");

        internal Rig()
        {
            Environment.SetEnvironmentVariable("WINLESTIA_DATA_DIR", dir.Path);
            Store.State = new Preferences { AutoHide = true };
            Program.TaskbarRestoreSignal = Restore;
            Program.QuitSignal = Quit;
            Program.ShowSignal = Show;
            var host = new TickHost(() => Array.Empty<Window>(), () => Array.Empty<Window>(), () => Calls.Add("pointer"),
                on => Calls.Add("taskbar:" + on), () => Calls.Add("open"), () => Calls.Add("collapse"), () => QtIdle);
            Controller = new TickController(State, null!, host);
        }

        public void Dispose()
        {
            Program.TaskbarRestoreSignal = oldRestore;
            Program.QuitSignal = oldQuit;
            Program.ShowSignal = oldShow;
            Store.State = prefs;
            Environment.SetEnvironmentVariable("WINLESTIA_DATA_DIR", env);
            Restore.Dispose();
            Quit.Dispose();
            Show.Dispose();
            dir.Dispose();
        }
    }

    [Fact]
    public void SignalsAreConsumedOneAtATimeInPriorityOrder()
    {
        using var rig = new Rig();
        rig.Restore.Set();
        rig.Quit.Set();
        rig.Show.Set();
        rig.Controller.Tick();
        Assert.Equal(new[] { "taskbar:False" }, rig.Calls);
        Assert.False(rig.Restore.WaitOne(0));
        Assert.True(rig.Quit.WaitOne(0, false) && SetBack(rig.Quit));
        Assert.True(rig.Show.WaitOne(0) && SetBack(rig.Show));
        // Tour 2 : l'arrêt passe avant la bascule (Application.Current est nul ici : l'échec est journalisé, le signal est consommé).
        rig.Controller.Tick();
        Assert.False(rig.Quit.WaitOne(0));
        Assert.True(rig.Show.WaitOne(0));
        Assert.Equal(new[] { "taskbar:False" }, rig.Calls);
    }

    static bool SetBack(EventWaitHandle handle) => handle.Set();

    [Fact]
    public void ShowSignalOpensWhenClosedAndCollapsesWhenOpen()
    {
        using var rig = new Rig();
        rig.Show.Set();
        rig.Controller.Tick();
        rig.State.Open = true;
        rig.Show.Set();
        rig.Controller.Tick();
        Assert.Equal(new[] { "open", "collapse" }, rig.Calls);
    }

    [Fact]
    public void AConsumedSignalSkipsThePointerWork()
    {
        using var rig = new Rig();
        rig.Restore.Set();
        rig.Controller.Tick();
        Assert.DoesNotContain("pointer", rig.Calls);
    }

    [Fact]
    public void IdleQtReturnsBeforeThePointerTick()
    {
        using var rig = new Rig { };
        rig.QtIdle = true;
        rig.Controller.Tick();
        Assert.Empty(rig.Calls);
        rig.QtIdle = false;
        rig.State.Open = true;
        rig.Controller.Tick();
        Assert.Equal(new[] { "pointer" }, rig.Calls);
    }

    [Theory]
    [InlineData(false, false, false, false, false, true)]
    [InlineData(true, true, false, false, false, true)]
    [InlineData(true, false, true, false, false, true)]
    [InlineData(true, false, false, true, false, true)]
    [InlineData(true, false, false, false, true, true)]
    [InlineData(true, false, false, false, false, false)]
    public void AutoHideSuspensionNeverCollapses(bool detailed, bool interacting, bool pinned, bool settings, bool popup,
        bool autoHideOff)
    {
        using var rig = new Rig();
        Store.State.AutoHide = !autoHideOff;
        rig.State.Open = true;
        rig.State.Detailed = detailed;
        rig.State.Interacting = interacting;
        rig.State.Pinned = pinned;
        rig.State.SettingsOpen = settings;
        rig.State.PopupOpen = popup;
        rig.State.AwaySince = DateTime.UtcNow.AddHours(-1);
        bool suspended = TickRules.AutoHideSuspended(detailed, interacting, pinned, settings, popup, !autoHideOff);
        rig.Controller.Tick();
        Assert.Contains("pointer", rig.Calls);
        if (suspended)
            Assert.DoesNotContain("collapse", rig.Calls);
    }

    [Fact]
    public void OpenBoardsCollapseOnceThePointerHasBeenAwayLongEnough()
    {
        using var rig = new Rig();
        rig.State.Open = true;
        rig.State.Detailed = true;
        rig.State.AwaySince = DateTime.UtcNow.AddHours(-1);
        rig.Controller.Tick();
        Assert.Equal(new[] { "pointer", "collapse" }, rig.Calls);
    }

    [Fact]
    public void FreshAbsenceOnlyStartsTheTimer()
    {
        using var rig = new Rig();
        rig.State.Open = true;
        rig.State.Detailed = true;
        rig.Controller.Tick();
        Assert.Equal(new[] { "pointer" }, rig.Calls);
        Assert.NotEqual(DateTime.MinValue, rig.State.AwaySince);
    }
}
