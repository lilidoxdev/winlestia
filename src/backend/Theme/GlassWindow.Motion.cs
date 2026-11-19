// GlassWindow : déplacements, transitions et animations de la fenêtre.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Input;

namespace PanneauxGlass;

internal partial class GlassWindow
{
    static DoubleAnimation Tween(double from, double to, double milliseconds)
        => new(from, to, Motion.Span(milliseconds * Store.State.Motion))
        {
            EasingFunction = Motion.Ease,
            FillBehavior = FillBehavior.Stop
        };
    internal void TransitionTo(Rect rect, int index, bool animate)
    {
        if (TopAttached && IsVisible && animate && Motion.On && Math.Abs(Height - rect.Height) > .5)
        {
            double from = Height;
            CancelMotion();
            Left = TargetLeft = rect.X;
            Top = TargetTop = rect.Y;
            Width = rect.Width;
            Height = rect.Height;
            BeginAnimation(HeightProperty, Tween(from, rect.Height, 250), HandoffBehavior.SnapshotAndReplace);
            ApplyGlass();
            return;
        }
        if (Offscreen)
            rect.Offset(CaptureOffset.X, CaptureOffset.Y);
        if (!IsVisible || Math.Abs(Width - rect.Width) > .5 || Math.Abs(Height - rect.Height) > .5)
        {
            MoveInto(rect.X, rect.Y, rect.Width, rect.Height, index, animate);
            return;
        }
        if (Math.Abs(TargetLeft - rect.X) < .5 && Math.Abs(TargetTop - rect.Y) < .5)
        {
            ApplyGlass();
            return;
        }
        double x = Left, y = Top;
        CancelMotion();
        TargetLeft = rect.X;
        TargetTop = rect.Y;
        Left = rect.X;
        Top = rect.Y;
        int version = motionVersion;
        if (animate && Motion.On)
        {
            void Axis(DependencyProperty property, double from, double to)
            {
                if (Math.Abs(from - to) < .5)
                    return;
                var slide = Tween(from, to, 175);
                slide.Completed += (_, _) => { if (version == motionVersion) BeginAnimation(property, null); };
                BeginAnimation(property, slide, HandoffBehavior.SnapshotAndReplace);
            }
            Axis(LeftProperty, x, rect.X);
            Axis(TopProperty, y, rect.Y);
        }
        ApplyGlass();
    }
    internal void HideImmediately()
    {
        CancelMotion();
        if (IsVisible)
            Hide();
    }
    internal void MoveInto(double x, double y, double width, double height, int index, bool animate)
    {
        if (Offscreen)
        {
            x += CaptureOffset.X;
            y += CaptureOffset.Y;
        }
        bool opening = !IsVisible;
        bool unchanged = Math.Abs(Left - x) < .5 && Math.Abs(Top - y) < .5 && Math.Abs(Width - width) < .5
            && Math.Abs(Height - height) < .5;
        if (!opening && unchanged)
        {
            ApplyGlass();
            return;
        }
        CancelMotion();
        TargetLeft = x;
        TargetTop = y;
        Width = width;
        Height = height;
        Top = y;
        // Une fenêtre déjà en cours de fermeture (arrêt de l'application, espace qui se replie) ne doit pas être réaffichée.
        Left = x;
        if (opening)
        {
            try
            {
                Show();
            }
            catch (InvalidOperationException) { return; }
        }
        ApplyGlass();
        if (opening && animate && Motion.On && TopAttached)
        {
            drawerSlide.BeginAnimation(TranslateTransform.YProperty, Tween(-height, 0, 300), HandoffBehavior.SnapshotAndReplace);
            Surface.BeginAnimation(UIElement.OpacityProperty, Tween(.25, 1, 190), HandoffBehavior.SnapshotAndReplace);
            return;
        }
        if (opening && animate && Motion.On)
        {
            int version = motionVersion;
            var slide = Tween(14, 0, 160);
            slide.Completed += (_, _) =>
            {
                if (version != motionVersion)
                    return;
                motion.BeginAnimation(TranslateTransform.XProperty, null);
                ContentHost.BeginAnimation(UIElement.OpacityProperty, null);
            };
            motion.BeginAnimation(TranslateTransform.XProperty, slide, HandoffBehavior.SnapshotAndReplace);
            ContentHost.BeginAnimation(UIElement.OpacityProperty, Tween(.65, 1, 160), HandoffBehavior.SnapshotAndReplace);
            Motion.BlurReveal(ContentHost);
        }
    }
    internal void SlideOut(int index, Action completed)
    {
        if (!IsVisible)
        {
            completed();
            return;
        }
        if (TopAttached)
        {
            double from = drawerSlide.Y;
            CancelMotion();
            if (!Motion.On)
            {
                Hide();
                completed();
                return;
            }
            int expected = motionVersion;
            var animation = Tween(from, -ActualHeight, 190);
            animation.Completed += (_, _) =>
            {
                if (expected == motionVersion)
                {
                    HideImmediately();
                    completed();
                }
            };
            drawerSlide.BeginAnimation(TranslateTransform.YProperty, animation, HandoffBehavior.SnapshotAndReplace);
            return;
        }
        double current = motion.X, opacity = ContentHost.Opacity;
        CancelMotion();
        if (!Motion.On)
        {
            Hide();
            completed();
            return;
        }
        int version = motionVersion;
        var slide = Tween(current, 12, 100);
        slide.Completed += (_, _) =>
        {
            if (version != motionVersion)
                return;
            HideImmediately();
            completed();
        };
        motion.BeginAnimation(TranslateTransform.XProperty, slide, HandoffBehavior.SnapshotAndReplace);
        ContentHost.BeginAnimation(UIElement.OpacityProperty, Tween(opacity, .3, 100), HandoffBehavior.SnapshotAndReplace);
    }
}
