// MainWindow : barre d'adresse (fil d'Ariane, saisie, résolution du chemin).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using PanneauxGlass;

namespace WinlestiaFiles;

public sealed partial class MainWindow
{
    // ---- address ---------------------------------------------------------------------------------------------

    void BuildCrumbs(string path)
    {
        crumbs.Children.Clear();
        if (ThisPc.Is(path))
        {
            var pcb = UI.Flat(ThisPc.Label, 28, new CornerRadius(14));
            pcb.Padding = new Thickness(M3.Pad3, 0, M3.Pad3, 0);
            pcb.Foreground = UI.Accent;
            crumbs.Children.Add(pcb);
            return;
        }
        string root = System.IO.Path.GetPathRoot(path) ?? "";
        var parts = new List<(string label, string target)>();
        if (root.Length > 0)
            parts.Add((root.TrimEnd('\\'), root.EndsWith('\\') ? root : root + "\\"));
        string walk = root;
        foreach (var segment in path.Substring(Math.Min(root.Length, path.Length)).Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            walk = System.IO.Path.Combine(walk, segment);
            parts.Add((segment, walk));
        }
        for (int i = 0; i < parts.Count; i++)
        {
            var (label, target) = parts[i];
            if (i > 0)
            {
                var sep = UI.Glyph("", 10);
                sep.Foreground = UI.Label;
                crumbs.Children.Add(sep);
            }
            var b = UI.Flat(label, 28, new CornerRadius(14));
            b.Padding = new Thickness(M3.Pad3, 0, M3.Pad3, 0);
            b.Click += (_, _) => Active.Go(target);
            if (i == parts.Count - 1)
                b.Foreground = UI.Accent;
            crumbs.Children.Add(b);
        }
    }

    void BeginEdit()
    {
        addressBox.Text = Active.Location;
        crumbScroll.Visibility = Visibility.Collapsed;
        addressBox.Visibility = Visibility.Visible;
        addressBox.UpdateLayout();
        addressBox.Focus();
        addressBox.SelectAll();
    }

    void EndEdit()
    {
        if (addressBox.Visibility != Visibility.Visible)
            return;
        addressBox.Visibility = Visibility.Collapsed;
        crumbScroll.Visibility = Visibility.Visible;
    }

    // Les variables d'environnement sont développées, le disque est sondé hors du thread UI. Un chemin inexistant laisse la zone en mode
    // édition avec un message clair.
    enum AddressKind
    {
        Folder,
        File,
        Shell,
        Invalid
    }

    static (AddressKind Kind, string Value) ResolveAddress(string text)
    {
        string value = Environment.ExpandEnvironmentVariables((text ?? "").Trim().Trim('"').Trim());
        if (value.Length == 0)
            return (AddressKind.Invalid, "");
        if (value.Equals(ThisPc.Label, StringComparison.OrdinalIgnoreCase) || value.Equals("This PC", StringComparison.OrdinalIgnoreCase)
            || ThisPc.Is(value))
            return (AddressKind.Folder, ThisPc.Path);
        if (value.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || value.StartsWith("::{", StringComparison.Ordinal))
            return (AddressKind.Shell, value);
        try
        {
            if (value.Length == 2 && value[1] == ':' && char.IsAsciiLetter(value[0]))
                value += "\\";
            string full = System.IO.Path.GetFullPath(value);
            if (System.IO.Directory.Exists(full))
            {
                string root = System.IO.Path.GetPathRoot(full) ?? "";
                if (full.Length > root.Length)
                    full = full.TrimEnd('\\', '/');
                return (AddressKind.Folder, full);
            }
            if (System.IO.File.Exists(full))
                return (AddressKind.File, full);
            return (AddressKind.Invalid, value);
        }
        catch { return (AddressKind.Invalid, value); }
    }

    async void SubmitAddress(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            EndEdit();
            list.Focus();
            return;
        }
        var tab = Active;
        try
        {
            var (kind, value) = await Task.Run(() => ResolveAddress(text));
            if (kind == AddressKind.Invalid)
            {
                Toast("Chemin introuvable : " + value);
                addressBox.Focus();
                return;
            }
            EndEdit();
            list.Focus();
            if (kind == AddressKind.Folder)
                tab.Go(value);
            else if (kind == AddressKind.File)
                Open(new FileItem(System.IO.Path.GetFileName(value), value, false, 0, DateTime.Now, ""));
            else
                PathRouter.LaunchExplorer(value);
        }
        catch (Exception ex) { App.CrashLog(ex); }
    }

    // Libellés des segments cliquables, de gauche à droite (auto-test).
    internal List<string> CrumbLabels() => crumbs.Children.OfType<Button>().Select(b => b.Content as string ?? "").ToList();
}
