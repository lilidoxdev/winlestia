using System;
using System.Runtime.ExceptionServices;
using System.Threading;
namespace Winlestia.Tests;
// xUnit 2 runs on MTA threads; the desktop tests (WinForms/WPF, shell COM) need an STA thread, as the old [STAThread] Main did.
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
