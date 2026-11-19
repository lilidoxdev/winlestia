// Barre latérale : réordonnancement au pointeur des entrées d'accès rapide
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using PanneauxGlass;
namespace WinlestiaFiles;

internal sealed partial class Sidebar
{
    // Réordonnancement au pointeur par capture (pas d'OLE : la cible de dépôt de la fenêtre reste intacte). Un appui sans déplacement
    // est un clic.
    Entry? dragFrom, dragTarget; Point dragStart; bool dragging;

    Entry? QuickAt(Point inHost)
    {
        var quick = entries.Where(x => x.Quick).ToList();
        if (quick.Count == 0)
            return null;
        foreach (var x in quick)
        {
            double top = x.Pill.TranslatePoint(new Point(0, 0), host).Y;
            if (inHost.Y >= top && inHost.Y < top + x.Pill.ActualHeight)
                return x;
        }
        double firstTop = quick[0].Pill.TranslatePoint(new Point(0, 0), host).Y;
        return inHost.Y < firstTop ? quick[0] : quick[^1];
    }

    void SetDragTarget(Entry? target)
    {
        if (ReferenceEquals(target, dragTarget))
            return;
        if (dragTarget != null)
        {
            dragTarget.Drop = false;
            Paint(dragTarget);
        }
        dragTarget = target;
        if (target != null)
        {
            target.Drop = true;
            Paint(target);
        }
    }

    void PointerDown(Entry e, MouseButtonEventArgs a)
    {
        dragFrom = e;
        dragStart = a.GetPosition(host);
        dragging = false;
        e.Pill.CaptureMouse();
    }

    void PointerMove(Entry e, MouseEventArgs a)
    {
        if (!ReferenceEquals(dragFrom, e) || a.LeftButton != MouseButtonState.Pressed)
            return;
        var pos = a.GetPosition(host);
        if (!dragging && Math.Abs(pos.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        dragging = true;
        SetDragTarget(QuickAt(pos));
    }

    void PointerUp(Entry e)
    {
        if (!ReferenceEquals(dragFrom, e))
            return;
        var target = dragTarget;
        bool moved = dragging;
        e.Pill.ReleaseMouseCapture();
        dragFrom = null;
        dragging = false;
        SetDragTarget(null);
        if (moved)
        {
            if (target != null && !ReferenceEquals(target, e))
                QuickAccess.MoveTo(QuickAccess.IndexOf(e.Path), QuickAccess.IndexOf(target.Path));
        }
        else if (e.Pill.IsMouseOver)
            ActivateQuick(e);
    }
}
