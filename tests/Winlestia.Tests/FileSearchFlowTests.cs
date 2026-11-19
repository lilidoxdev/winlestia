// C#12 / FileSearch : cycle Start / Payload / TryResolve / Cancel avec une requête injectée (aucun index Windows Search).
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PanneauxGlass;
namespace Winlestia.Tests;
public class FileSearchFlowTests
{
    static FileSearch Canned(params string[] paths) => new((term, gen, _) =>
    {
        var hits = new List<FileHit>();
        foreach (string path in paths)
            hits.Add(new FileHit(System.IO.Path.GetFileName(path), path));
        return Task.FromResult(new FileSnapshot(gen, FileSearch.Ok, hits));
    });

    static bool Wait(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (!done() && DateTime.UtcNow < until)
            Thread.Sleep(5);
        return done();
    }

    [Fact]
    public void StartStoresTheSnapshotAndSignalsDone()
    {
        using var search = Canned("C:/a/x.txt", "C:/a/y.txt");
        Assert.Null(search.Payload());
        using var signal = new ManualResetEventSlim();
        search.Start("rapport", 3, signal.Set);
        Assert.True(signal.Wait(5000));
        Assert.NotNull(search.Payload());
        Assert.True(search.TryResolve(3, 1, out string path));
        Assert.Equal("C:/a/y.txt", path);
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, -1)]
    [InlineData(3, 2)]
    public void TryResolveRefusesOtherGenerationsAndBadIndexes(long gen, int index)
    {
        using var search = Canned("C:/a/x.txt", "C:/a/y.txt");
        using var signal = new ManualResetEventSlim();
        search.Start("rapport", 3, signal.Set);
        Assert.True(signal.Wait(5000));
        Assert.False(search.TryResolve(gen, index, out string path));
        Assert.Equal("", path);
    }

    [Fact]
    public void OlderGenerationIsIgnored()
    {
        using var search = Canned("C:/a/x.txt");
        using var first = new ManualResetEventSlim();
        search.Start("abc", 5, first.Set);
        Assert.True(first.Wait(5000));
        bool called = false;
        search.Start("abc", 4, () => called = true);
        Thread.Sleep(50);
        Assert.False(called);
        Assert.True(search.TryResolve(5, 0, out _));
    }

    [Fact]
    public void CancelAndDisposeDropTheSnapshot()
    {
        var search = Canned("C:/a/x.txt");
        using var signal = new ManualResetEventSlim();
        search.Start("abc", 1, signal.Set);
        Assert.True(signal.Wait(5000));
        search.Cancel();
        Assert.Null(search.Payload());
        Assert.False(search.TryResolve(1, 0, out _));
        search.Dispose();
        bool called = false;
        search.Start("abc", 9, () => called = true);
        Thread.Sleep(50);
        Assert.False(called);
    }

    [Fact]
    public void BuildProducesAScopedContainsQuery()
    {
        Assert.True(FileQuery.TryBuild("o'brien *x*", "C:/Users/moi", out string sql));
        Assert.Contains("SCOPE='file:C:/Users/moi'", sql);
        Assert.Contains("o''brien", sql);
        Assert.False(FileQuery.TryBuild("a", "C:/Users/moi", out _));
        Assert.False(FileQuery.TryBuild("abc", "relatif", out _));
        Assert.True(FileQuery.IsExecutable("C:/x/run.EXE"));
        Assert.False(FileQuery.IsExecutable("C:/x/note.txt"));
    }
}
