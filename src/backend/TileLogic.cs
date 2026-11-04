// C#12 / TileLogic.cs / Pure tiling logic (no Win32 access): split layout with ratios, directional neighbour, stable order, exclusions parsing.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("TileLogicTests")]

namespace PanneauxGlass;

internal enum TileDir
{
    Left,
    Right,
    Up,
    Down,
}

internal static class TileLogic
{
    internal const double MinRatio = .2,
        MaxRatio = .8,
        RatioStep = .05;

    // Binary split on the longer side. `ratio(path)` gives the first-part share for the split at `path` ("" = root, then "0"/"1" per descent); default .5.
    internal static Native.Rect[] Layout(Native.Rect area, int count, int gap, Func<string, double>? ratio = null)
    {
        var result = new List<Native.Rect>();
        void Split(Native.Rect r, int n, string path)
        {
            if (n == 1)
            {
                result.Add(r);
                return;
            }
            int first = n / 2;
            double share = Math.Clamp(ratio?.Invoke(path) ?? .5, MinRatio, MaxRatio);
            bool vertical = r.Right - r.Left >= r.Bottom - r.Top;
            if (vertical)
            {
                int middle = r.Left + (int)((r.Right - r.Left - gap) * share);
                Split(
                    new()
                    {
                        Left = r.Left,
                        Top = r.Top,
                        Right = middle,
                        Bottom = r.Bottom,
                    },
                    first,
                    path + "0"
                );
                Split(
                    new()
                    {
                        Left = middle + gap,
                        Top = r.Top,
                        Right = r.Right,
                        Bottom = r.Bottom,
                    },
                    n - first,
                    path + "1"
                );
            }
            else
            {
                int middle = r.Top + (int)((r.Bottom - r.Top - gap) * share);
                Split(
                    new()
                    {
                        Left = r.Left,
                        Top = r.Top,
                        Right = r.Right,
                        Bottom = middle,
                    },
                    first,
                    path + "0"
                );
                Split(
                    new()
                    {
                        Left = r.Left,
                        Top = middle + gap,
                        Right = r.Right,
                        Bottom = r.Bottom,
                    },
                    n - first,
                    path + "1"
                );
            }
        }
        if (count > 0 && area.Right > area.Left && area.Bottom > area.Top)
            Split(area, count, "");
        return result.ToArray();
    }

    // Deepest split containing tile `index` among `count` tiles; inFirst tells which side of that split the tile is on. null when count<2.
    internal static (string path, bool inFirst)? DeepestSplit(int count, int index)
    {
        if (count < 2 || index < 0 || index >= count)
            return null;
        string path = "";
        int start = 0,
            n = count;
        (string, bool)? found = null;
        while (n > 1)
        {
            int first = n / 2;
            bool inFirst = index < start + first;
            found = (path, inFirst);
            if (inFirst)
            {
                n = first;
                path += "0";
            }
            else
            {
                start += first;
                n -= first;
                path += "1";
            }
        }
        return found;
    }

    internal static double Grow(double current, bool inFirst, double delta) =>
        Math.Clamp(Math.Round(current + (inFirst ? delta : -delta), 3), MinRatio, MaxRatio);

    // Nearest tile in a direction: must lie strictly beyond the current edge and overlap on the perpendicular axis; ties go to the closest centre. -1 if none.
    internal static int Neighbor(IReadOnlyList<Native.Rect> rects, int index, TileDir dir)
    {
        if (index < 0 || index >= rects.Count)
            return -1;
        var c = rects[index];
        int best = -1;
        long bestScore = long.MaxValue;
        for (int i = 0; i < rects.Count; i++)
        {
            if (i == index)
                continue;
            var o = rects[i];
            long gap;
            bool overlap;
            long off;
            switch (dir)
            {
                case TileDir.Left:
                    gap = c.Left - o.Right;
                    overlap = Math.Min(c.Bottom, o.Bottom) > Math.Max(c.Top, o.Top);
                    off = Math.Abs((c.Top + c.Bottom) - (o.Top + o.Bottom));
                    break;
                case TileDir.Right:
                    gap = o.Left - c.Right;
                    overlap = Math.Min(c.Bottom, o.Bottom) > Math.Max(c.Top, o.Top);
                    off = Math.Abs((c.Top + c.Bottom) - (o.Top + o.Bottom));
                    break;
                case TileDir.Up:
                    gap = c.Top - o.Bottom;
                    overlap = Math.Min(c.Right, o.Right) > Math.Max(c.Left, o.Left);
                    off = Math.Abs((c.Left + c.Right) - (o.Left + o.Right));
                    break;
                default:
                    gap = o.Top - c.Bottom;
                    overlap = Math.Min(c.Right, o.Right) > Math.Max(c.Left, o.Left);
                    off = Math.Abs((c.Left + c.Right) - (o.Left + o.Right));
                    break;
            }
            if (gap < -2 || !overlap)
                continue;
            long score = Math.Max(0, gap) * 100000 + off;
            if (score < bestScore)
            {
                bestScore = score;
                best = i;
            }
        }
        return best;
    }

    internal static int TileAt(IReadOnlyList<Native.Rect> rects, int x, int y, int except = -1)
    {
        for (int i = 0; i < rects.Count; i++)
            if (i != except && x >= rects[i].Left && x < rects[i].Right && y >= rects[i].Top && y < rects[i].Bottom)
                return i;
        return -1;
    }

    internal static List<string> ParseExclusions(string? text) =>
        (text ?? "")
            .Split('\n')
            .Select(Normalize)
            .Where(s => s.Length > 0)
            .Distinct()
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

    internal static string Normalize(string? name)
    {
        var s = (name ?? "").Trim().ToLowerInvariant();
        if (s.EndsWith(".exe"))
            s = s[..^4];
        return s;
    }
}

// Stable tile order: new windows go last, focus never reorders, only an explicit swap does.
internal sealed class TileOrder
{
    readonly List<IntPtr> items = new();
    internal IReadOnlyList<IntPtr> Items => items;

    internal void Sync(IEnumerable<IntPtr> live)
    {
        var set = new HashSet<IntPtr>(live);
        items.RemoveAll(h => !set.Contains(h));
        foreach (var h in live)
            if (!items.Contains(h))
                items.Add(h);
    }

    internal bool Swap(IntPtr a, IntPtr b)
    {
        int i = items.IndexOf(a),
            j = items.IndexOf(b);
        if (i < 0 || j < 0 || i == j)
            return false;
        (items[i], items[j]) = (items[j], items[i]);
        return true;
    }

    internal IntPtr[] Filter(IEnumerable<IntPtr> subset)
    {
        var set = new HashSet<IntPtr>(subset);
        return items.Where(set.Contains).ToArray();
    }

    internal void Clear() => items.Clear();
}
