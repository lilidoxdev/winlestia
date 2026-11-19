// WindowOverview.Project: filtrage, ordre et projection de la vue d'ensemble.
using System.Collections.Generic;
using System.Linq;
using PanneauxGlass;
namespace Winlestia.Tests;
public class WindowOverviewTests
{
    static OverviewRaw Win(long hwnd, string title = "Doc", uint pid = 10, int w = 800, int h = 600, bool min = false,
        bool cloaked = false, bool primary = true, string path = @"C:\apps\notepad.exe") =>
        new(hwnd, title, path, pid, 100, 50, 100 + w, 50 + h, min, cloaked, primary);

    static (string, string, string) Resolve(OverviewRaw w) => ("App", "id" + w.Hwnd, "id" + w.Hwnd);

    static OverviewItem[] Run(params OverviewRaw[] raw) => WindowOverview.Project(raw, new HashSet<uint> { 99 }, Resolve);

    [Fact]
    public void KeepsInputOrder() =>
        Assert.Equal(new long[] { 3, 1, 2 }, Run(Win(3), Win(1), Win(2)).Select(i => i.Hwnd));

    [Fact]
    public void DropsCloakedShellEmptyAndShellTitles()
    {
        var items = Run(Win(1, cloaked: true), Win(2, pid: 99), Win(3, title: "  "), Win(4, title: "Panneaux · Réglages"),
            Win(5, title: "Caelestia shell"), Win(6));
        Assert.Equal(new long[] { 6 }, items.Select(i => i.Hwnd));
    }

    [Fact]
    public void DropsWindowsOffThePrimaryMonitor() =>
        Assert.Equal(new long[] { 2 }, Run(Win(1, primary: false), Win(2)).Select(i => i.Hwnd));

    [Fact]
    public void DropsDegenerateRectsAndDuplicates() =>
        Assert.Equal(new long[] { 1, 4 }, Run(Win(1), Win(2, w: 1), Win(3, h: 0), Win(1), Win(4)).Select(i => i.Hwnd));

    [Fact]
    public void CapsAtMaxWindows() =>
        Assert.Equal(WindowOverview.MaxWindows, Run(Enumerable.Range(1, 100).Select(i => Win(i)).ToArray()).Length);

    [Fact]
    public void FallsBackToFileNameForAppName()
    {
        var items = WindowOverview.Project(new[] { Win(1) }, new HashSet<uint>(), _ => ("", "", ""));
        Assert.Equal("notepad", items[0].App);
    }

    [Fact]
    public void ProjectsGeometryAndFlags()
    {
        var item = Run(Win(7, w: 640, h: 480, min: true))[0];
        Assert.Equal((100, 50, 640, 480), (item.X, item.Y, item.W, item.H));
        Assert.True(item.Minimized);
        Assert.True(item.Primary);
        Assert.Equal("id7", item.Id);
    }
}
