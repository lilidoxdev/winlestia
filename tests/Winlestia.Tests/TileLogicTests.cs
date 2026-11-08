// C#12 / TileLogicTests / Pure tiling logic only: no window is created, moved or focused.
using System;
using System.Linq;
using PanneauxGlass;
namespace Winlestia.Tests;
public class TileLogicTests
{
    static void Check(bool ok, string name) => Assert.True(ok, name);
    static Native.Rect R(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };
    [Fact]
    public void AllChecks()
    {
        var area = R(0, 0, 1000, 600);
        // Layout: default identical to the previous binary split; partition without overlap for 1..16.
        bool part = true;
        for (int n = 1; n <= 16; n++)
        {
            var l = TileLogic.Layout(area, n, 8);
            part &= l.Length == n && l.All(r => r.Right > r.Left && r.Bottom > r.Top && r.Left >= 0 && r.Right <= 1000 && r.Bottom <= 600)
                && !l.SelectMany((a, i) => l.Skip(i + 1).Select(b => Math.Min(a.Right, b.Right) > Math.Max(a.Left, b.Left)
                && Math.Min(a.Bottom, b.Bottom) > Math.Max(a.Top, b.Top))).Any(x => x);
        }
        Check(part, "layout 1..16 sans chevauchement");
        var two = TileLogic.Layout(area, 2, 8);
        Check(two[0].Right == 496 && two[1].Left == 504, "deux tuiles a 50 % (identique a l'ancien calcul)");
        var skew = TileLogic.Layout(area, 2, 8, _ => .7);
        Check(skew[0].Right == Math.Max(skew[0].Left, 0) + (int)((1000 - 8) * .7) && skew[1].Left == skew[0].Right + 8,
            "ratio .7 applique");
        Check(TileLogic.Layout(area, 2, 8, _ => .05)[0].Right == TileLogic.Layout(area, 2, 8, _ => .2)[0].Right
            && TileLogic.Layout(area, 2, 8, _ => .99)[0].Right == TileLogic.Layout(area, 2, 8, _ => .8)[0].Right, "ratio borne a 20-80 %");
        var three = TileLogic.Layout(area, 3, 8, p => p == "1" ? .5 : .5);
        Check(three.Length == 3 && three[1].Top < three[2].Top, "3 tuiles : droite coupee en deux");
        // Deepest split + Grow.
        Check(TileLogic.DeepestSplit(1, 0) == null, "une seule tuile : pas de decoupe");
        var s = TileLogic.DeepestSplit(3, 2);
        Check(s != null && s.Value.path == "1" && !s.Value.inFirst, "3 tuiles, derniere : decoupe droite, 2e cote");
        s = TileLogic.DeepestSplit(3, 0);
        Check(s != null && s.Value.path == "" && s.Value.inFirst, "3 tuiles, premiere : decoupe racine");
        Check(TileLogic.Grow(.5, true, .05) == .55 && TileLogic.Grow(.5, false, .05) == .45 && TileLogic.Grow(.78, true, .05) == .8
            && TileLogic.Grow(.22, false, .05) == .2, "Grow signe et bornes");
        double r = .5;
        for (int i = 0; i < 20; i++)
            r = TileLogic.Grow(r, true, .05);
        Check(r == .8, "plafond 80 %");
        for (int i = 0; i < 40; i++)
            r = TileLogic.Grow(r, true, -.05);
        Check(r == .2, "plancher 20 %");
        // Neighbour.
        var grid = TileLogic.Layout(area, 4, 8);  // 2x2: 0 top-left,1 bottom-left,2 top-right,3 bottom-right (split order)
        int tl = Array.FindIndex(grid, g => g.Left == 0 && g.Top == 0), tr = Array.FindIndex(grid, g => g.Left > 0 && g.Top == 0),
            bl = Array.FindIndex(grid, g => g.Left == 0 && g.Top > 0), br = Array.FindIndex(grid, g => g.Left > 0 && g.Top > 0);
        Check(TileLogic.Neighbor(grid, tl, TileDir.Right) == tr && TileLogic.Neighbor(grid, tl, TileDir.Down) == bl
            && TileLogic.Neighbor(grid, br, TileDir.Left) == bl && TileLogic.Neighbor(grid, br, TileDir.Up) == tr,
            "voisines dans une grille 2x2");
        Check(TileLogic.Neighbor(grid, tl, TileDir.Left) == -1 && TileLogic.Neighbor(grid, tl, TileDir.Up) == -1
            && TileLogic.Neighbor(grid, tl, TileDir.Right) != br, "pas de voisine au bord, pas de diagonale");
        var wide = new[] { R(0, 0, 500, 600), R(508, 0, 1000, 292), R(508, 300, 1000, 600) };
        Check(TileLogic.Neighbor(wide, 0, TileDir.Right) is 1 or 2 && TileLogic.Neighbor(wide, 1, TileDir.Left) == 0
            && TileLogic.Neighbor(wide, 2, TileDir.Up) == 1, "cote a cote avec une colonne coupee");
        var multi = new[] { R(0, 0, 1000, 600), R(1008, 0, 2000, 600) };
        Check(TileLogic.Neighbor(multi, 0, TileDir.Right) == 1 && TileLogic.Neighbor(multi, 1, TileDir.Left) == 0,
            "voisine sur l'ecran adjacent");
        Check(TileLogic.TileAt(grid, 10, 10) == tl && TileLogic.TileAt(grid, 10, 10, tl) == -1 && TileLogic.TileAt(grid, 500, 10) == -1,
            "TileAt");
        // Order.
        var o = new TileOrder();
        IntPtr A = (IntPtr)1, B = (IntPtr)2, C = (IntPtr)3, D = (IntPtr)4;
        o.Sync(new[] { A, B, C });
        o.Sync(new[] { C, B, A });
        Check(o.Items.SequenceEqual(new[] { A, B, C }), "focaliser/re-trier la pile Z ne reorganise rien");
        o.Sync(new[] { A, B, C, D });
        Check(o.Items.SequenceEqual(new[] { A, B, C, D }), "nouvelle fenetre a la fin");
        o.Sync(new[] { A, C, D });
        Check(o.Items.SequenceEqual(new[] { A, C, D }), "fermeture : les autres gardent leur ordre");
        Check(o.Swap(A, D) && o.Items.SequenceEqual(new[] { D, C, A }) && !o.Swap(A, B) && !o.Swap(A, A), "echange");
        Check(o.Filter(new[] { A, D }).SequenceEqual(new[] { D, A }), "filtre par ecran garde l'ordre global");
        // Exclusions parsing.
        var ex = TileLogic.ParseExclusions("Notepad.EXE\r\n  code \n\nnotepad\n");
        Check(ex.SequenceEqual(new[] { "code", "notepad" }), "exclusions : normalisees, dedoublonnees, triees");
    }
}
