// Conversion d'une ligne de l'index en résultat, et charge utile de la palette : aucun index Windows Search.
using System.Threading.Tasks;
using PanneauxGlass;
namespace Winlestia.Tests;
public class FileSearchRowTests
{
    [Fact]
    public void RowWithoutPathIsDropped()
    {
        Assert.Null(FileQuery.ToHit("", "x"));
        Assert.Null(FileQuery.ToHit(null, "x"));
    }

    [Fact]
    public void RowWithoutNameFallsBackToFileName()
    {
        Assert.Equal("a.txt", FileQuery.ToHit(@"C:\u\a.txt", "")!.Name);
        Assert.Equal("Titre", FileQuery.ToHit(@"C:\u\a.txt", "Titre")!.Name);
    }

    [Fact]
    public async Task PayloadIndexesItemsAndResolveRejectsStaleGeneration()
    {
        var done = new TaskCompletionSource();
        var snap = new FileSnapshot(7, FileSearch.Ok, new[] { new FileHit("a", @"C:\a"), new FileHit("b", @"C:\b") });
        using var search = new FileSearch((_, _, _) => Task.FromResult(snap));
        Assert.Null(search.Payload());
        search.Start("ab", 7, () => done.TrySetResult());
        await done.Task.WaitAsync(System.TimeSpan.FromSeconds(5));
        Assert.NotNull(search.Payload());
        Assert.True(search.TryResolve(7, 1, out var path));
        Assert.Equal(@"C:\b", path);
        Assert.False(search.TryResolve(6, 0, out _));
        Assert.False(search.TryResolve(7, 2, out _));
    }
}
