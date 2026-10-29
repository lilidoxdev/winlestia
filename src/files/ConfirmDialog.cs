// Unified modal confirmation (M3 card, error-coloured action). Used for the permanent delete (Shift+Del) only.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PanneauxGlass;

namespace WinlestiaFiles;

internal static class ConfirmDialog {
    internal static bool Ask(Window owner, string title, string message, string confirmLabel) {
        bool ok = false;
        var win = new Window {
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.WidthAndHeight, ShowInTaskbar = false,
            AllowsTransparency = true, Background = System.Windows.Media.Brushes.Transparent, Owner = owner, WindowStartupLocation = WindowStartupLocation.CenterOwner, FontFamily = M3.Body };
        var heading = UI.Text(title, M3.FontTitle, UI.White, true);
        var body = UI.Text(message, M3.FontSmall, UI.Label); body.TextWrapping = TextWrapping.Wrap; body.Margin = new Thickness(0, M3.Sp2, 0, M3.Sp4);
        var cancel = UI.Flat("Annuler", 36, new CornerRadius(M3.Small)); cancel.Padding = new Thickness(M3.Sp4, 0, M3.Sp4, 0);
        cancel.Click += (_, _) => win.Close();
        var confirm = UI.Flat(confirmLabel, 36, new CornerRadius(M3.Small), UI.T(M3Role.Error)); confirm.Padding = new Thickness(M3.Sp4, 0, M3.Sp4, 0); confirm.Foreground = UI.T(M3Role.OnError);
        confirm.Margin = new Thickness(M3.Pad1, 0, 0, 0);
        confirm.Click += (_, _) => { ok = true; win.Close(); };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel); buttons.Children.Add(confirm);
        var stack = new StackPanel(); stack.Children.Add(heading); stack.Children.Add(body); stack.Children.Add(buttons);
        win.Content = new Border { Background = UI.T(M3Role.SurfaceHigh), CornerRadius = new CornerRadius(M3.Normal), BorderBrush = UI.Hairline, BorderThickness = new Thickness(1),
            Padding = new Thickness(M3.Sp5), MinWidth = 340, MaxWidth = 460, Margin = new Thickness(12), Child = stack };
        win.KeyDown += (_, e) => { if (e.Key == Key.Escape) win.Close(); };
        win.ShowDialog();
        return ok;
    }
}
