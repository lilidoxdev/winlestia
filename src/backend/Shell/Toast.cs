// C# 12, fichier Toast.cs, cible Windows 11 / .NET 8 WPF, confirmation de verre éphémère au-dessus de la barre des tâches
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace PanneauxGlass;
// Un toast à la fois, jamais activé, fermé automatiquement. Il ne fait que confirmer une action déjà effectuée.
internal static class Toast
{
    // Durée d'affichage : le temps de lire une courte phrase.
    const int DisplayMs = 2400;
    static GlassWindow? window; static DispatcherTimer? timer;
    internal static void Show(string text, Window? anchor = null)
    {
        if (System.Windows.Application.Current == null || string.IsNullOrWhiteSpace(text))
            return;
        Dismiss();
        try
        {
            var message = UI.Text(text, 13, UI.White);
            message.TextTrimming = TextTrimming.CharacterEllipsis;
            message.MaxWidth = 420;
            message.VerticalAlignment = VerticalAlignment.Center;
            message.Measure(new Size(420, 200));
            double width = Math.Clamp(message.DesiredSize.Width + 64, 150, 460), height = 48;
            var toast = new GlassWindow("Winlestia — confirmation") { Radius = 20 };
            toast.ContentHost.Margin = new Thickness(17, 0, 17, 0);
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var glyph = UI.Glyph("\uE73E", 14);
            glyph.Foreground = UI.Brush("#B7F0D8");
            glyph.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(glyph);
            row.Children.Add(message);
            toast.ContentHost.Children.Add(row);
            var screen = anchor != null && new WindowInteropHelper(anchor).Handle != IntPtr.Zero
                ? Forms.Screen.FromHandle(new WindowInteropHelper(anchor).Handle) : Forms.Screen.PrimaryScreen!;
            double scale = anchor != null ? VisualTreeHelper.GetDpi(anchor).DpiScaleX : 1;
            var area = screen.WorkingArea;
            toast.MoveInto((area.Left + area.Width / 2) / scale - width / 2, (area.Bottom - 28) / scale - height, width, height, 0, true);
            window = toast;
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(DisplayMs) };
            timer.Tick += (_, _) => Dismiss();
            timer.Start();
        }
        catch (Exception ex) { Store.Log("Toast: " + ex.Message); Dismiss(); }
    }
    internal static void Dismiss()
    {
        timer?.Stop();
        timer = null;
        var toast = window;
        window = null;
        if (toast == null)
            return;
        Safe.Try(() =>
        {
            toast.HideImmediately();
            toast.Close();

        });
    }
}
