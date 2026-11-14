// Garde-fous de repli vers l'Explorateur et de ré-enregistrement du verbe Dossier (logique pure uniquement).
using System.IO;
using Winlestia.Shared;
using WinlestiaFiles;
namespace Winlestia.Tests;
public class FilesFallbackTests
{
    [Fact]
    public void FallbackIsClaimedOnceForARealPath()
    {
        int claimed = 0;
        Assert.True(PathRouter.TryClaimFallback(@"C:\Users", false, ref claimed));
        Assert.False(PathRouter.TryClaimFallback(@"C:\Users", false, ref claimed));   // OnStartup catch puis EarlyDeath
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NoFallbackWithoutPath(string? arg)
    {
        int claimed = 0;
        Assert.False(PathRouter.TryClaimFallback(arg, false, ref claimed));
        Assert.Equal(0, claimed);                                                      // ne consomme pas le droit
        PathRouter.LaunchExplorer(arg ?? "");                                          // no-op : aucun processus lance
    }

    [Fact]
    public void NoFallbackInBackground()
    {
        int claimed = 0;
        Assert.False(PathRouter.TryClaimFallback(@"C:\Users", true, ref claimed));
        Assert.Equal(0, claimed);
    }

    [Fact]
    public void HandlerNotReRegisteredWhileACrashIsPending()
    {
        Assert.Equal(HandlerStartup.Ensure, FilesHandlerGuard.DecideStartup(true, true, false));
        Assert.Equal(HandlerStartup.Nothing, FilesHandlerGuard.DecideStartup(true, true, false, crashPending: true));
        Assert.Equal(HandlerStartup.TrippedDisable, FilesHandlerGuard.DecideStartup(true, true, true, crashPending: true));
    }

    [Fact]
    public void CrashMarkerIsClearedByAHealthyStart()
    {
        string dir = Path.Combine(Path.GetTempPath(), "WinlestiaGuard-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Assert.False(FilesHandlerGuard.HasPendingCrash(dir));
            File.WriteAllText(Path.Combine(dir, FilesHandlerGuard.StampsFile), "1700000000000\n");
            Assert.True(FilesHandlerGuard.HasPendingCrash(dir));
            FilesHandlerGuard.ClearCrashes(dir);
            Assert.False(FilesHandlerGuard.HasPendingCrash(dir));
        }
        finally { Directory.Delete(dir, true); }
    }
}
