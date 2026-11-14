// C#12 / Calculs purs de l'échantillonneur d'usage par processus et liste de refus de l'arrêt. Aucun vrai processus n'est ouvert.
using System.Collections.Generic;
using System.Linq;
using PanneauxGlass;
using Xunit;
namespace Winlestia.Tests;
public class ProcessUsageSamplerTests
{
    const long Second = 10_000_000;
    static readonly Dictionary<int, (long Start, long Ticks)> None = new();

    [Fact]
    public void CpuPercent_DividesByWallTimeAndCores()
    {
        // 2 s de temps processeur en 2 s sur 4 cœurs = 25 % de la machine.
        Assert.Equal(25, ProcessUsageSampler.CpuPercent(0, 2 * Second, 2, 4), 6);
    }

    [Fact]
    public void CpuPercent_ClampsAndIgnoresBadInput()
    {
        Assert.Equal(100, ProcessUsageSampler.CpuPercent(0, 50 * Second, 1, 4));
        Assert.Equal(0, ProcessUsageSampler.CpuPercent(5 * Second, Second, 1, 4));
        Assert.Equal(0, ProcessUsageSampler.CpuPercent(0, Second, 0, 4));
        Assert.Equal(0, ProcessUsageSampler.CpuPercent(0, Second, 1, 0));
    }

    [Fact]
    public void Aggregate_FirstSampleHasNoCpu()
    {
        var usage = ProcessUsageSampler.Aggregate(new[] { new ProcessUsageSampler.Raw(1, 100, @"C:\a\x.exe", 9 * Second, 1000) }, None, 0, 4, out var next);
        Assert.Equal(0, Assert.Single(usage).Cpu);
        Assert.Equal((100L, 9 * Second), next[1]);
    }

    [Fact]
    public void Aggregate_UsesDeltaFromPreviousSample()
    {
        var previous = new Dictionary<int, (long, long)> { [1] = (100, Second) };
        var usage = ProcessUsageSampler.Aggregate(new[] { new ProcessUsageSampler.Raw(1, 100, @"C:\a\x.exe", 3 * Second, 1) }, previous, 1, 2, out _);
        Assert.Equal(100, Assert.Single(usage).Cpu);
    }

    [Fact]
    public void Aggregate_ReusedPidWithOtherStartTimeCountsAsNew()
    {
        var previous = new Dictionary<int, (long, long)> { [1] = (100, Second) };
        var usage = ProcessUsageSampler.Aggregate(new[] { new ProcessUsageSampler.Raw(1, 999, @"C:\a\x.exe", 50 * Second, 1) }, previous, 1, 2, out var next);
        Assert.Equal(0, Assert.Single(usage).Cpu);
        Assert.Equal(999, next[1].Start);
    }

    [Fact]
    public void Aggregate_GroupsByPathIgnoringCase()
    {
        var previous = new Dictionary<int, (long, long)> { [1] = (1, 0), [2] = (2, 0) };
        var raws = new[]
        {
            new ProcessUsageSampler.Raw(1, 1, @"C:\Apps\Chrome.exe", Second / 2, 100),
            new ProcessUsageSampler.Raw(2, 2, @"c:\apps\chrome.EXE", Second / 2, 300),
            new ProcessUsageSampler.Raw(3, 3, @"C:\Apps\Other.exe", 0, 50)
        };
        var usage = ProcessUsageSampler.Aggregate(raws, previous, 1, 4, out _);
        Assert.Equal(2, usage.Count);
        var chrome = usage.Single(u => u.Count == 2);
        Assert.Equal(400, chrome.MemoryBytes);
        Assert.Equal(25, chrome.Cpu, 6);
        Assert.Equal(1, usage.Single(u => u.Count == 1).Count);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(5, false)]
    [InlineData(5.1, true)]
    [InlineData(600, true)]
    public void IsStale_TreatsLongGapAsFirstSample(double elapsed, bool stale) => Assert.Equal(stale, ProcessUsageSampler.IsStale(elapsed));

    [Fact]
    public void Ancestors_WalksParentChainAndStopsOnCycleOrReusedPid()
    {
        var processes = new[] { (Pid: 10, Parent: 5), (Pid: 5, Parent: 3), (Pid: 3, Parent: 1), (Pid: 1, Parent: 3), (Pid: 20, Parent: 99) };
        var all = ProcessUsageSampler.Ancestors(new[] { 10 }, processes, _ => 0);
        Assert.Equal(new[] { 1, 3, 5 }, all.OrderBy(x => x));
        // Le parent 3 est plus récent que son enfant 5 : PID réutilisé, la remontée s'arrête avant lui.
        var starts = new Dictionary<int, long> { [10] = 100, [5] = 90, [3] = 95, [1] = 10 };
        var cut = ProcessUsageSampler.Ancestors(new[] { 10 }, processes, p => starts.GetValueOrDefault(p));
        Assert.Equal(new[] { 5 }, cut.OrderBy(x => x));
    }

    [Fact]
    public void Tree_FollowsDescendantsOnly()
    {
        var all = new[] { (10, 1), (11, 10), (12, 11), (20, 1), (30, 20) };
        var tree = ProcessUsageSampler.Tree(new[] { 10 }, all);
        Assert.Equal(new[] { 10, 11, 12 }, tree.OrderBy(p => p).ToArray());
        Assert.Empty(ProcessUsageSampler.Tree(new[] { 0 }, all));
    }

    [Theory]
    [InlineData(@"C:\Windows\explorer.exe")]
    [InlineData(@"D:\x\EXPLORER.EXE")]
    [InlineData(@"D:\x\MsMpEng.exe")]
    [InlineData(@"D:\x\RuntimeBroker.exe")]
    [InlineData(@"D:\x\SecurityHealthService.exe")]
    [InlineData(@"D:\x\Panneaux.Glass.exe")]
    [InlineData("")]
    public void StopPolicy_DeniesProtectedNames(string path) => Assert.True(StopPolicy.IsDenied(path));

    [Theory]
    [InlineData(@"C:\Python\python.exe")]
    [InlineData(@"C:\Program Files\App\app.exe")]
    public void StopPolicy_AllowsOrdinaryPrograms(string path) => Assert.False(StopPolicy.IsDenied(path));
}
