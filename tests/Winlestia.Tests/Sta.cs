using System;
using System.Runtime.ExceptionServices;
using System.Threading;
namespace Winlestia.Tests;
// xUnit 2 s'exécute sur des threads MTA ; les tests de bureau (WinForms/WPF, COM du shell) exigent un thread STA, comme l'ancien
// [STAThread] Main.
internal static class Sta
{
    internal static void Run(Action body)
    {
        ExceptionDispatchInfo? error = null;
        var t = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { error = ExceptionDispatchInfo.Capture(ex); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        error?.Throw();
    }
}
