// C#12 / Noyau de dispatch du pont : recherche, appel, assemblage de la réponse. Propriétaire factice, aucun bureau requis.
using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using PanneauxGlass;
namespace Winlestia.Tests;
public class BridgeDispatchTests
{
    sealed class Stub
    {
        internal readonly List<string> Calls = new();

        [BridgeAction("sync")]
        void OnSync(BridgeCall c) => Calls.Add("sync");

        [BridgeAction("alias1")]
        [BridgeAction("alias2")]
        void OnAlias(BridgeCall c) => Calls.Add("alias");

        [BridgeAction("raw")]
        void OnRaw(BridgeCall c)
        {
            c.Notice = "ignoré";
            c.RawReply = "{\"probe\":1}";
        }

        [BridgeAction("boom")]
        void OnBoom(BridgeCall c)
        {
            c.WindowItems = "déjà posé";
            c.Notice = "avant";
            throw new InvalidOperationException("échec voulu");
        }

        [BridgeAction("rawThenBoom")]
        void OnRawThenBoom(BridgeCall c)
        {
            c.RawReply = "{\"probe\":2}";
            throw new InvalidOperationException("après la sonde");
        }

        [BridgeAction("async")]
        async Task OnAsync(BridgeCall c)
        {
            await Task.Delay(30);
            Calls.Add("async");
            c.Notice = "terminé";
        }
    }

    static BridgeCall NewCall() => new(default(JsonElement), "");
    static readonly BridgeActionTable<Stub> Table = BridgeActionTable<Stub>.Build(typeof(Stub));

    [Fact]
    public async Task UnknownCommandIsIgnoredAndLeavesTheStateReply()
    {
        var stub = new Stub();
        var call = NewCall();
        string? raw = await Table.Run(stub, "nope", call, _ => { });
        Assert.Null(raw);
        Assert.Null(call.Notice);
        Assert.Empty(stub.Calls);
    }

    [Fact]
    public async Task AliasesShareOneHandler()
    {
        var stub = new Stub();
        await Table.Run(stub, "alias1", NewCall(), _ => { });
        await Table.Run(stub, "alias2", NewCall(), _ => { });
        Assert.Equal(new[] { "alias", "alias" }, stub.Calls);
    }

    [Fact]
    public async Task ThrowingHandlerBecomesNoticeAndKeepsPriorOutputs()
    {
        var logged = new List<string>();
        var call = NewCall();
        string? raw = await Table.Run(new Stub(), "boom", call, logged.Add);
        Assert.Null(raw);
        Assert.Equal("échec voulu", call.Notice);
        Assert.Equal("déjà posé", call.WindowItems);
        Assert.Single(logged);
        Assert.Contains("échec voulu", logged[0]);
    }

    [Fact]
    public async Task RawReplyShortCircuitsTheStateReply()
    {
        string? raw = await Table.Run(new Stub(), "raw", NewCall(), _ => { });
        Assert.Equal("{\"probe\":1}", raw);
    }

    [Fact]
    public async Task RawReplyIsDroppedWhenTheHandlerThrowsAfterwards()
    {
        var call = NewCall();
        string? raw = await Table.Run(new Stub(), "rawThenBoom", call, _ => { });
        Assert.Null(raw);
        Assert.Equal("après la sonde", call.Notice);
    }

    [Fact]
    public async Task AsyncHandlerIsAwaitedBeforeTheReplyIsAssembled()
    {
        var stub = new Stub();
        var call = NewCall();
        await Table.Run(stub, "async", call, _ => { });
        Assert.Equal(new[] { "async" }, stub.Calls);
        Assert.Equal("terminé", call.Notice);
    }

    [Fact]
    public void RealBridgeTableIsBuiltEagerlyAndNotEmpty()
    {
        Assert.Contains("state", QtShellBridge.ActionNames);
    }

    sealed class Duplicate
    {
        [BridgeAction("x")] void A(BridgeCall c) { }
        [BridgeAction("x")] void B(BridgeCall c) { }
    }

    sealed class Static
    {
        [BridgeAction("x")] static void A(BridgeCall c) { }
    }

    sealed class BadParameter
    {
        [BridgeAction("x")] void A(string c) { }
    }

    sealed class BadReturn
    {
        [BridgeAction("x")] int A(BridgeCall c) => 0;
    }

    sealed class PublicBadSignature
    {
        [BridgeAction("x")] public void A() { }
    }

    sealed class AsyncVoid
    {
        [BridgeAction("x")] async void A(BridgeCall c) => await Task.Yield();
    }

    [Theory]
    [InlineData(typeof(Duplicate))]
    [InlineData(typeof(Static))]
    [InlineData(typeof(BadParameter))]
    [InlineData(typeof(BadReturn))]
    [InlineData(typeof(PublicBadSignature))]
    [InlineData(typeof(AsyncVoid))]
    public void InvalidActionsFailLoudlyAtBuildTime(Type type)
    {
        var build = typeof(BridgeActionTable<>).MakeGenericType(type).GetMethod("Build",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var ex = Assert.Throws<System.Reflection.TargetInvocationException>(() => build.Invoke(null, new object[] { type }));
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }
}
