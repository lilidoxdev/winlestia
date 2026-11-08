// Invariants de restauration de la barre des taches (docs/RESTORE.md) : bail, repli et decision du gardien.
// Logique pure uniquement : aucun appel a la vraie barre, aucun processus gardien.
using System.Collections.Generic;
using System.IO;
using System.Text;
using PanneauxGlass;
namespace Winlestia.Tests;
public class TaskbarLeasePolicyTests
{
    static TaskbarNative.Lease Sample(int state = 1) => new()
    {
        State = state,
        Visible = new List<long> { 65918, 131074 },
        Ready = @"Local\PanneauxGuardReady-42-abc",
        Release = @"Local\PanneauxGuardRelease-42-abc",
        ParentStart = 638_700_000_000_000_000L
    };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void SaveThenParseRestoresSameLeaseAndKeepsOriginalState(int state)
    {
        using var dir = new TempDir();
        string path = dir.Combine("taskbar-session.json");
        TaskbarLeasePolicy.Write(path, Sample(state));
        var back = TaskbarLeasePolicy.Read(path);
        Assert.NotNull(back);
        Assert.Equal(state, back!.State);
        Assert.Equal(new long[] { 65918, 131074 }, back.Visible);
        Assert.Equal(@"Local\PanneauxGuardReady-42-abc", back.Ready);
        Assert.Equal(@"Local\PanneauxGuardRelease-42-abc", back.Release);
        Assert.Equal(638_700_000_000_000_000L, back.ParentStart);
        // Decision de restauration : l'etat d'origine du bail l'emporte sur le repli.
        Assert.Equal(state, TaskbarLeasePolicy.ForRestore(back).State);
    }

    [Fact]
    public void MissingLeaseFileFallsBackToAlwaysVisible()
    {
        using var dir = new TempDir();
        var read = TaskbarLeasePolicy.Read(dir.Combine("absent.json"));
        Assert.Null(read);
        var plan = TaskbarLeasePolicy.ForRestore(read);
        Assert.Equal(2, plan.State);
        Assert.Equal(TaskbarLeasePolicy.FallbackState, plan.State);
        Assert.Empty(plan.Visible);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"State\": 1, \"Visible\": [1, 2")]
    [InlineData("{\"State\": \"deux\"}")]
    [InlineData("[1, 2, 3]")]
    [InlineData("null")]
    public void CorruptLeaseFileFallsBackToAlwaysVisible(string content)
    {
        using var dir = new TempDir();
        string path = dir.Combine("taskbar-session.json");
        File.WriteAllText(path, content);
        Assert.Null(TaskbarLeasePolicy.Read(path));
        Assert.Equal(2, TaskbarLeasePolicy.ForRestore(TaskbarLeasePolicy.Read(path)).State);
    }

    [Fact]
    public void TruncatedWriteOfARealLeaseIsRejected()
    {
        string full = TaskbarLeasePolicy.Serialize(Sample());
        Assert.NotNull(TaskbarLeasePolicy.Parse(full));
        // Coupure en plein milieu (crash pendant l'ecriture) : le bail est illisible, pas a moitie lu.
        Assert.Null(TaskbarLeasePolicy.Parse(full.Substring(0, full.Length / 2)));
    }

    [Fact]
    public void ParseIsCaseInsensitiveAndIgnoresUnknownProperties()
    {
        var lease = TaskbarLeasePolicy.Parse("{\"state\":1,\"visible\":[7],\"parentstart\":99,\"futur\":true}");
        Assert.NotNull(lease);
        Assert.Equal(1, lease!.State);
        Assert.Equal(new long[] { 7 }, lease.Visible);
        Assert.Equal(99, lease.ParentStart);
    }

    [Fact]
    public void LeaseWithUtf8BomIsStillParsed()
    {
        using var dir = new TempDir();
        string path = dir.Combine("taskbar-session.json");
        File.WriteAllText(path, TaskbarLeasePolicy.Serialize(Sample()), new UTF8Encoding(true));
        Assert.Equal(1, TaskbarLeasePolicy.Read(path)!.State);
    }

    [Fact]
    public void EmptyObjectGivesAnEmptyLeaseNotTheFallback()
    {
        // Documente le comportement actuel : un `{}` valide n'est pas « illisible », il donne l'etat 0 sans barre.
        var lease = TaskbarLeasePolicy.Parse("{}");
        Assert.NotNull(lease);
        Assert.Equal(0, lease!.State);
        Assert.Empty(lease.Visible);
        Assert.Same(lease, TaskbarLeasePolicy.ForRestore(lease));
    }

    [Fact]
    public void WriteOverwritesAnOlderLease()
    {
        using var dir = new TempDir();
        string path = dir.Combine("taskbar-session.json");
        TaskbarLeasePolicy.Write(path, Sample(1));
        TaskbarLeasePolicy.Write(path, Sample(2));
        Assert.Equal(2, TaskbarLeasePolicy.Read(path)!.State);
    }

    [Fact]
    public void GuardWithoutLeaseDoesNothing()
    {
        Assert.Equal(TaskbarLeasePolicy.GuardAction.NoLease, TaskbarLeasePolicy.Decide(null, 123));
        Assert.Equal(TaskbarLeasePolicy.GuardAction.NoLease, TaskbarLeasePolicy.Decide(null, null));
    }

    [Fact]
    public void GuardRestoresAtOnceWhenTheParentWasRelaunched()
    {
        // Meme PID reutilise par un autre processus : heure de demarrage differente, donc parent d'origine deja mort.
        var lease = Sample();
        Assert.Equal(TaskbarLeasePolicy.GuardAction.RestoreNow, TaskbarLeasePolicy.Decide(lease, lease.ParentStart + 1));
        Assert.Equal(TaskbarLeasePolicy.GuardAction.RestoreNow, TaskbarLeasePolicy.Decide(lease, 0));
    }

    [Fact]
    public void GuardWaitsWhenTheParentIsTheOriginalProcess()
    {
        var lease = Sample();
        Assert.Equal(TaskbarLeasePolicy.GuardAction.Wait, TaskbarLeasePolicy.Decide(lease, lease.ParentStart));
    }

    [Fact]
    public void GuardRestoresWhenTheParentCannotBeFound()
    {
        Assert.Equal(TaskbarLeasePolicy.GuardAction.RestoreAfterError, TaskbarLeasePolicy.Decide(Sample(), null));
    }
}
