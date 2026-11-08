// C#12 / NetworkKindWatcher: Start/Stop (event subscription) are idempotent, Changed fires only on a real change.
using System;
using System.Threading;
using PanneauxGlass;
namespace Winlestia.Tests;
public class NetworkKindTests
{
    [Fact]
    public void AllChecks()
    {
        NetworkKindWatcher.Start();
        NetworkKindWatcher.Start();
        Thread.Sleep(1500);
        NetworkKindWatcher.Stop();
        NetworkKindWatcher.Stop();
        NetworkKindWatcher.Start();
        Thread.Sleep(500);
        NetworkKindWatcher.Stop();
        int fired = 0;
        Action h = () => fired++;
        NetworkKindWatcher.Changed += h;
        string cur = NetworkKindWatcher.Current, other = cur == "ethernet" ? "wifi" : "ethernet";
        NetworkKindWatcher.RaiseChangedForTest(other);
        NetworkKindWatcher.RaiseChangedForTest(other);
        NetworkKindWatcher.RaiseChangedForTest(cur);
        NetworkKindWatcher.Changed -= h;
        NetworkKindWatcher.RaiseChangedForTest(other);
        Assert.Equal(2, fired);
        Assert.True(NetworkKindWatcher.Current is "ethernet" or "wifi" or "none",
            "Current is one of ethernet/wifi/none, got " + NetworkKindWatcher.Current);
    }
}
