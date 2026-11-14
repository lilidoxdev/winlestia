// C#12 / SpectrumPolicy: la capture du spectre suit la page Média du tableau de bord OU la Pilule, et seulement en lecture.
using PanneauxGlass;
namespace Winlestia.Tests;
public class SpectrumPolicyTests
{
    [Theory]
    [InlineData(true, 1, false, true, true)]
    [InlineData(true, 0, false, true, false)]
    [InlineData(false, 1, false, true, false)]
    [InlineData(false, 0, true, true, true)]
    [InlineData(true, 3, true, true, true)]
    [InlineData(true, 1, true, false, false)]
    [InlineData(false, 0, true, false, false)]
    [InlineData(false, 0, false, true, false)]
    public void WantedFollowsSurfaceAndPlayback(bool dashboard, int page, bool pill, bool playing, bool expected) =>
        Assert.Equal(expected, SpectrumPolicy.Wanted(dashboard, page, pill, playing));

    [Fact]
    public void CaptureStopsWhenPillIdleAndDashboardClosed() =>
        Assert.False(SpectrumPolicy.Surface(false, 1, false));
}
