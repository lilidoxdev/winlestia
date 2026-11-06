// Selection rectangle: dragging from an empty area of the list selects what the rectangle touches, in every view (Ctrl adds to the selection).
// Hit-testing is arithmetic on the item grid (rows of Cell.H, Cols() columns), so it works with virtualization and costs nothing at idle.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PanneauxGlass;

namespace WinlestiaFiles;

public sealed partial class MainWindow
{
    Border? rubberBox;
    bool rubbing, rubberMoved, rubberAdd;
    Point rubberFrom;                        // content coordinates (x, y + scrolled pixels)
    HashSet<FileItem> rubberBase = new();
    List<FileItem> rubberSet = new();

    void WireRubber()
    {
        rubberBox = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = UI.Accent,
            Background = UI.Tint,
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        Grid.SetRowSpan(rubberBox, 2);
        System.Windows.Controls.Panel.SetZIndex(rubberBox, 11);
        listHost.Children.Add(rubberBox);
        list.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (renaming != null || e.ClickCount != 1 || e.OriginalSource is not DependencyObject src)
                return;
            if (ItemsControl.ContainerFromElement(list, src) is ListBoxItem)
                return;
            for (DependencyObject? d = src; d != null && !ReferenceEquals(d, list); d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(d) : null)
                if (d is System.Windows.Controls.Primitives.ScrollBar)
                    return;
            var p = e.GetPosition(list);
            RubberStart(p, (Keyboard.Modifiers & ModifierKeys.Control) != 0);
            list.Focus();
            list.CaptureMouse();
            e.Handled = true;
        };
        list.PreviewMouseMove += (_, e) => { if (rubbing) { if (e.LeftButton != MouseButtonState.Pressed) { list.ReleaseMouseCapture(); RubberEnd(); } else RubberMove(e.GetPosition(list)); } };
        list.PreviewMouseLeftButtonUp += (_, _) => { if (rubbing) { list.ReleaseMouseCapture(); RubberEnd(); } };
        list.LostMouseCapture += (_, _) => { if (rubbing) RubberEnd(); };
    }

    double ScrolledPixels()
    {
        var sv = ListScroller();
        if (sv == null)
            return 0;
        return gridMode ? sv.VerticalOffset : sv.VerticalOffset * Cell.H;
    }

    // p: position in the list's own coordinates (viewport).
    internal void RubberStart(Point p, bool add)
    {
        rubbing = true;
        rubberMoved = false;
        rubberAdd = add;
        rubberFrom = new Point(p.X, p.Y + ScrolledPixels());
        rubberBase = add ? list.SelectedItems.Cast<FileItem>().ToHashSet() : new HashSet<FileItem>();
        rubberSet = rubberBase.ToList();
    }

    internal void RubberMove(Point p)
    {
        if (!rubbing)
            return;
        double top = ScrolledPixels();
        var to = new Point(p.X, p.Y + top);
        if (!rubberMoved && Math.Abs(to.X - rubberFrom.X) < 4 && Math.Abs(to.Y - rubberFrom.Y) < 4)
            return;
        rubberMoved = true;
        double x1 = Math.Min(rubberFrom.X, to.X), x2 = Math.Max(rubberFrom.X, to.X), y1 = Math.Min(rubberFrom.Y, to.Y), y2 = Math.Max(rubberFrom.Y, to.Y);
        if (rubberBox != null)
        {
            var host = listHost;
            double offY = list.TranslatePoint(new Point(0, 0), host).Y;
            rubberBox.Margin = new Thickness(x1, y1 - top + offY, 0, 0);
            rubberBox.Width = Math.Max(1, x2 - x1);
            rubberBox.Height = Math.Max(1, y2 - y1);
            rubberBox.Visibility = Visibility.Visible;
        }
        var hit = RubberHit(x1, y1, x2, y2);
        var next = rubberBase.Union(hit).ToList();
        if (next.Count == rubberSet.Count && next.All(rubberSet.Contains))
            return;
        rubberSet = next;
        list.Select(next);
    }

    internal void RubberEnd()
    {
        if (!rubbing)
            return;
        rubbing = false;
        if (rubberBox != null)
            rubberBox.Visibility = Visibility.Collapsed;
        if (!rubberMoved && !rubberAdd)
            list.UnselectAll();      // a plain click on empty space deselects
        UpdateStatus();
    }

    // Items touched by the rectangle (content coordinates).
    internal List<FileItem> RubberHit(double x1, double y1, double x2, double y2)
    {
        var items = Active.Items;
        var result = new List<FileItem>();
        if (items.Count == 0 || y2 < 0)
            return result;
        double ch = Cell.H, cw = Cell.W;
        int cols = Cols(), r1 = Math.Max(0, (int)(y1 / ch)), r2 = Math.Max(0, (int)(y2 / ch));
        int c1 = 0, c2 = 0;
        if (gridMode)
        {
            c1 = Math.Clamp((int)(x1 / cw), 0, cols - 1);
            c2 = Math.Clamp((int)(x2 / cw), 0, cols - 1);
            if (x2 < 0 || x1 >= cols * cw)
                return result;
        }
        for (int r = r1; r <= r2; r++)
            for (int c = c1; c <= c2; c++)
            {
                int index = gridMode ? r * cols + c : r;
                if (index >= items.Count)
                    break;
                result.Add(items[index]);
            }
        return result;
    }
}
