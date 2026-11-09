// C#12 / Tests du service de données : liste d'hôtes, intervalle minimal, taille, cache périmé, requête unique, minuteur.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PanneauxGlass.Winjets;
using Xunit;
namespace Winlestia.Tests.Winjets;

public class WinjetDataServiceTests
{
    sealed class Fake : HttpMessageHandler
    {
        internal int Calls;
        internal Func<HttpRequestMessage, Task<HttpResponseMessage>> Reply = _ => Task.FromResult(Ok("{\"p\":1}"));
        internal static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls);
            return Reply(request);
        }
    }

    sealed class Rig : IDisposable
    {
        internal readonly TempDir Dir = new();
        internal readonly Fake Http = new();
        internal DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        internal bool ThrowOnce;
        DateTimeOffset Clock()
        {
            if (ThrowOnce)
            {
                ThrowOnce = false;
                throw new InvalidOperationException("horloge");
            }
            return Now;
        }
        internal readonly WinjetDataService Service;
        internal readonly WinjetPollTarget Target;

        internal Rig(string source = "live")
        {
            Service = new WinjetDataService(Dir.Combine("cache"), Http, Clock);
            string dir = Pkg.Default().WriteFolder(Directory.CreateDirectory(Dir.Combine("pkg")).FullName);
            using var v = WinjetValidator.ValidateDirectory(dir, Pkg.Current);
            Assert.True(v.Ok, string.Join("|", v.Errors));
            Target = With(v.Package!, source);
        }

        internal static WinjetPollTarget With(WinjetPackage p, string source)
            => new("w-1", p, new Dictionary<string, object?> { ["source"] = source }, new Dictionary<string, object?>());

        internal WinjetSource Live => Target.Package.Definition.Sources.First(s => s.Id == "live");

        public void Dispose()
        {
            Service.Dispose();
            Dir.Dispose();
        }
    }

    [Fact]
    public async Task FetchesAndStores()
    {
        using var r = new Rig();
        await r.Service.RefreshAsync(r.Target, true);
        var v = r.Service.Get("w-1", "live")!;
        Assert.Equal(1, v.Value!.Value.GetProperty("p").GetInt32());
        Assert.False(v.Stale);
        Assert.Equal(1, r.Http.Calls);
    }

    [Fact]
    public async Task DisabledSourceIsNotFetched()
    {
        using var r = new Rig("demo");
        await r.Service.RefreshAsync(r.Target, true);
        Assert.Equal(0, r.Http.Calls);
    }

    [Fact]
    public async Task MinimumIntervalEnforced()
    {
        using var r = new Rig();
        await r.Service.RefreshAsync(r.Target, false);
        r.Now += TimeSpan.FromSeconds(30);
        await r.Service.RefreshAsync(r.Target, true);
        Assert.Equal(1, r.Http.Calls);
        r.Now += TimeSpan.FromSeconds(61);
        await r.Service.RefreshAsync(r.Target, true);
        Assert.Equal(2, r.Http.Calls);
    }

    [Fact]
    public async Task PollDueRespectsRefresh()
    {
        using var r = new Rig();
        r.Service.SetTargets(new[] { r.Target });
        await r.Service.PollDueAsync();
        Assert.Equal(1, r.Http.Calls);
        r.Now += TimeSpan.FromSeconds(60);
        await r.Service.PollDueAsync();
        Assert.Equal(1, r.Http.Calls);
        r.Now += TimeSpan.FromSeconds(61);
        await r.Service.PollDueAsync();
        Assert.Equal(2, r.Http.Calls);
    }

    [Fact]
    public async Task OversizedResponseRejected()
    {
        using var r = new Rig();
        r.Http.Reply = _ => Task.FromResult(Fake.Ok(new string('x', 600 * 1024)));
        await r.Service.RefreshAsync(r.Target, true);
        var v = r.Service.Get("w-1", "live");
        Assert.True(v == null || v.Value == null);
    }

    [Fact]
    public async Task FailureFallsBackToStaleCache()
    {
        using var r = new Rig();
        await r.Service.RefreshAsync(r.Target, true);
        r.Http.Reply = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        r.Now += TimeSpan.FromSeconds(120);
        await r.Service.RefreshAsync(r.Target, true);
        var v = r.Service.Get("w-1", "live")!;
        Assert.True(v.Stale);
        Assert.Equal(1, v.Value!.Value.GetProperty("p").GetInt32());
    }

    [Fact]
    public async Task CacheSurvivesRestart()
    {
        using var r = new Rig();
        await r.Service.RefreshAsync(r.Target, true);
        var offline = new Fake { Reply = _ => throw new HttpRequestException("hors ligne") };
        using var again = new WinjetDataService(r.Dir.Combine("cache"), offline, () => r.Now.AddMinutes(10));
        await again.RefreshAsync(r.Target, true);
        var v = again.Get("w-1", "live")!;
        Assert.True(v.Stale);
        Assert.NotNull(v.Value);
    }

    [Fact]
    public async Task SingleInFlightPerSource()
    {
        using var r = new Rig();
        var gate = new TaskCompletionSource<HttpResponseMessage>();
        r.Http.Reply = _ => gate.Task;
        var a = r.Service.RefreshAsync(r.Target, true);
        var b = r.Service.RefreshAsync(r.Target, true);
        gate.SetResult(Fake.Ok("{}"));
        await Task.WhenAll(a, b);
        Assert.Equal(1, r.Http.Calls);
    }

    [Fact]
    public async Task HostNotInAllowlistRefused()
    {
        using var r = new Rig();
        var bad = r.Live with { Url = "https://evil.example.org/x" };
        await r.Service.FetchAsync(r.Target, bad, true);
        Assert.Equal(0, r.Http.Calls);
    }

    [Fact]
    public async Task RedirectIsNotFollowed()
    {
        using var r = new Rig();
        r.Http.Reply = _ =>
        {
            var m = new HttpResponseMessage(HttpStatusCode.Found);
            m.Headers.Location = new Uri("https://evil.example.org/");
            return Task.FromResult(m);
        };
        await r.Service.RefreshAsync(r.Target, true);
        Assert.Equal(1, r.Http.Calls);
        var v = r.Service.Get("w-1", "live");
        Assert.True(v == null || v.Value == null);
    }

    [Fact]
    public async Task TimeoutStopsRequest()
    {
        using var r = new Rig();
        r.Service.Timeout = TimeSpan.FromMilliseconds(100);
        r.Http.Reply = async _ =>
        {
            await Task.Delay(5000);
            return Fake.Ok("{}");
        };
        await r.Service.RefreshAsync(r.Target, true);
        var v = r.Service.Get("w-1", "live");
        Assert.True(v == null || v.Value == null);
    }

    [Fact]
    public void TimerOnlyWithEnabledHttpSource()
    {
        using var r = new Rig("demo");
        r.Service.SetTargets(new[] { r.Target });
        Assert.False(r.Service.HasTimer);
        r.Service.SetTargets(new[] { Rig.With(r.Target.Package, "live") });
        Assert.True(r.Service.HasTimer);
        r.Service.SetTargets(Array.Empty<WinjetPollTarget>());
        Assert.False(r.Service.HasTimer);
    }

    [Fact]
    public async Task PollingSurvivesAnException()
    {
        using var r = new Rig();
        r.Service.SetTargets(new[] { r.Target });
        r.ThrowOnce = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => r.Service.PollDueAsync());
        Assert.True(r.Service.HasTimer);
        await r.Service.PollDueAsync();
        Assert.Equal(1, r.Http.Calls);
    }

    [Fact]
    public async Task ForgetCancelsInFlightFetch()
    {
        using var r = new Rig();
        var gate = new TaskCompletionSource<HttpResponseMessage>();
        r.Http.Reply = _ => gate.Task;
        var run = r.Service.RefreshAsync(r.Target, true);
        r.Service.Forget("w-1");
        gate.SetResult(Fake.Ok("{}"));
        await run;
        Assert.Null(r.Service.Get("w-1", "live"));
    }

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.16.0.1", false)]
    [InlineData("172.32.0.1", true)]
    [InlineData("192.168.1.1", false)]
    [InlineData("169.254.1.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("::1", false)]
    [InlineData("::", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700::1111", true)]
    public void OnlyPublicAddressesAllowed(string ip, bool expected)
        => Assert.Equal(expected, WinjetDataService.IsPublicAddress(IPAddress.Parse(ip)));

    sealed class FixedProxy : IWebProxy
    {
        internal Uri? Via;
        internal bool Bypass;
        public ICredentials? Credentials { get; set; }
        public Uri? GetProxy(Uri destination) => Via;
        public bool IsBypassed(Uri host) => Bypass;
    }

    [Fact]
    public void AddressFilterSkippedOnlyThroughAProxy()
    {
        var target = new Uri("https://api.example.com/v1");
        Assert.True(WinjetDataService.ShouldFilterAddresses(target, null));
        Assert.True(WinjetDataService.ShouldFilterAddresses(target, new FixedProxy { Via = new Uri("http://10.0.0.1:3128") , Bypass = true }));
        Assert.True(WinjetDataService.ShouldFilterAddresses(target, new FixedProxy { Via = target }));
        Assert.True(WinjetDataService.ShouldFilterAddresses(target, new FixedProxy { Via = null }));
        Assert.False(WinjetDataService.ShouldFilterAddresses(target, new FixedProxy { Via = new Uri("http://10.0.0.1:3128") }));
    }

    [Fact]
    public void EnabledWhenEvaluated()
    {
        using var r = new Rig("demo");
        Assert.False(WinjetDataService.IsEnabled(r.Live, r.Target));
        Assert.True(WinjetDataService.IsEnabled(r.Live, Rig.With(r.Target.Package, "live")));
    }
}
