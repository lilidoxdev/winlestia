// Virtualizing wrap panel for the large-icons view: fixed cells, row-based pixel scrolling, only the rows in view hold containers (recycled).
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace WinlestiaFiles;

public sealed class TilePanel : VirtualizingPanel, IScrollInfo {
    internal const double CellW = 132, CellH = 156;
    ScrollViewer? owner;
    Size extent, viewport;
    double offsetY;
    int cols = 1;

    ItemsControl? Owner => ItemsControl.GetItemsOwner(this);

    protected override Size MeasureOverride(Size available) {
        _ = InternalChildren;                                   // creates the generator
        double w = double.IsInfinity(available.Width) ? CellW : available.Width;
        double h = double.IsInfinity(available.Height) ? CellH * 4 : available.Height;
        var owner = Owner; int n = owner?.Items.Count ?? 0;
        cols = Math.Max(1, (int)(w / CellW));
        var newExtent = new Size(w, Math.Ceiling(n / (double)cols) * CellH);
        var newViewport = new Size(w, h);
        bool changed = newExtent != extent || newViewport != viewport;
        extent = newExtent; viewport = newViewport;
        offsetY = Math.Clamp(offsetY, 0, Math.Max(0, extent.Height - viewport.Height));
        if (changed) this.owner?.InvalidateScrollInfo();

        int firstRow = (int)(offsetY / CellH), lastRow = (int)((offsetY + h - 0.5) / CellH);
        int first = Math.Min(firstRow * cols, n), last = Math.Min((lastRow + 1) * cols, n) - 1;
        Realize(first, last, owner);
        return new Size(w, h);
    }

    void Realize(int first, int last, ItemsControl? owner) {
        var gen = ItemContainerGenerator;
        bool recycle = gen is IRecyclingItemContainerGenerator && owner != null && GetVirtualizationMode(owner) == VirtualizationMode.Recycling;
        for (int i = InternalChildren.Count - 1; i >= 0; i--) {
            var pos = new GeneratorPosition(i, 0);
            int index = gen.IndexFromGeneratorPosition(pos);
            if (index >= first && index <= last) continue;
            if (recycle) ((IRecyclingItemContainerGenerator)gen).Recycle(pos, 1); else gen.Remove(pos, 1);
            RemoveInternalChildRange(i, 1);
        }
        if (last < first) return;
        var start = gen.GeneratorPositionFromIndex(first);
        int child = start.Offset == 0 ? start.Index : start.Index + 1;
        using (gen.StartAt(start, GeneratorDirection.Forward, true)) {
            for (int index = first; index <= last; index++, child++) {
                var element = (UIElement)gen.GenerateNext(out bool isNew);
                if (isNew) {
                    if (child >= InternalChildren.Count) AddInternalChild(element); else InsertInternalChild(child, element);
                    gen.PrepareItemContainer(element);
                }
                element.Measure(new Size(CellW, CellH));
            }
        }
    }

    protected override Size ArrangeOverride(Size finalSize) {
        var gen = ItemContainerGenerator;
        for (int i = 0; i < InternalChildren.Count; i++) {
            int index = gen.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            InternalChildren[i].Arrange(new Rect((index % cols) * CellW, (index / cols) * CellH - offsetY, CellW, CellH));
        }
        return finalSize;
    }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args) {
        switch (args.Action) {
            case System.Collections.Specialized.NotifyCollectionChangedAction.Remove:
            case System.Collections.Specialized.NotifyCollectionChangedAction.Replace:
            case System.Collections.Specialized.NotifyCollectionChangedAction.Move:
                RemoveInternalChildRange(args.Position.Index, args.ItemUICount);
                break;
        }
        InvalidateMeasure();
    }

    protected override void BringIndexIntoView(int index) {
        double top = index / cols * CellH, bottom = top + CellH;
        if (top < offsetY) SetVerticalOffset(top);
        else if (bottom > offsetY + viewport.Height) SetVerticalOffset(bottom - viewport.Height);
    }

    // ---- IScrollInfo (vertical pixels only) --------------------------------------------------------------------
    public bool CanVerticallyScroll { get; set; } = true;
    public bool CanHorizontallyScroll { get; set; }
    public double ExtentWidth => extent.Width;
    public double ExtentHeight => extent.Height;
    public double ViewportWidth => viewport.Width;
    public double ViewportHeight => viewport.Height;
    public double HorizontalOffset => 0;
    public double VerticalOffset => offsetY;
    public ScrollViewer? ScrollOwner { get => owner; set => owner = value; }

    public void SetVerticalOffset(double offset) {
        offset = Math.Clamp(offset, 0, Math.Max(0, extent.Height - viewport.Height));
        if (offset == offsetY) return;
        offsetY = offset;
        InvalidateMeasure();
        owner?.InvalidateScrollInfo();
    }
    public void SetHorizontalOffset(double offset) { }
    public void LineUp() => SetVerticalOffset(offsetY - 48);
    public void LineDown() => SetVerticalOffset(offsetY + 48);
    public void PageUp() => SetVerticalOffset(offsetY - viewport.Height);
    public void PageDown() => SetVerticalOffset(offsetY + viewport.Height);
    public void MouseWheelUp() => SetVerticalOffset(offsetY - CellH * 0.75);
    public void MouseWheelDown() => SetVerticalOffset(offsetY + CellH * 0.75);
    public void LineLeft() { }
    public void LineRight() { }
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }

    public Rect MakeVisible(Visual visual, Rect rectangle) {
        if (visual is UIElement element) {
            int i = InternalChildren.IndexOf(element);
            if (i >= 0) BringIndexIntoView(ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0)));
        }
        return rectangle;
    }
}
