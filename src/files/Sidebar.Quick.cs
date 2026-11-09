// Barre latérale : entrées d'accès rapide (étiquettes, menus, relocalisation, vérification des cibles)
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
    // ---- quick access --------------------------------------------------------------------------------------------

    string DefaultLabel(string path)
    {
        if (known.TryGetValue(Key(path), out var place))
            return place.Label;
        string leaf = System.IO.Path.GetFileName(Key(path));
        return leaf.Length > 0 ? leaf : path;
    }

    void BuildQuick()
    {
        active = null;
        dropEntry = null;
        dragTarget = null;
        dragFrom = null;
        entries.RemoveAll(e => e.Quick);
        quickHost.Children.Clear();
        foreach (var q in QuickAccess.Entries.ToList())
        {
            known.TryGetValue(Key(q.Path), out var place);
            string label = !string.IsNullOrEmpty(q.Label) ? q.Label : DefaultLabel(q.Path);
            quickHost.Children.Add(Add(q.Path, label, null, place?.Material ?? FolderMat, place?.Fluent ?? FolderFluent, true));
        }
        if (quickHost.Children.Count == 0)
        {
            var hint = UI.Text("Clic droit sur un élément : Épingler dans Accès rapide", 11, UI.Label);
            hint.TextWrapping = TextWrapping.Wrap;
            hint.Margin = new Thickness(M3.Sp2, M3.Pad1, M3.Sp2, M3.Pad1);
            quickHost.Children.Add(hint);
        }
        Highlight(current);
        _ = Recheck();
    }

    // Existence de chaque cible épinglée, hors du thread UI, chaque sonde avec un délai (un lecteur réseau déconnecté bloquerait
    // Directory.Exists). Pas de scrutation : appelé quand
    // la liste s'affiche, quand un lecteur arrive ou part (message de changement de périphérique de la fenêtre) et quand la fenêtre
    // est réactivée.
    internal async Task Recheck()
    {
        int gen = ++generation;
        lastCheck = Stopwatch.GetTimestamp();
        var targets = entries.Where(e => e.Quick).ToList();
        try
        {
            var kinds = await Task.WhenAll(targets.Select(e => QuickAccess.Probe(e.Path)));
            if (gen != generation)
                return;
            for (int i = 0; i < targets.Count; i++)
                if (entries.Contains(targets[i]) && targets[i].Kind != kinds[i])
                {
                    targets[i].Kind = kinds[i];
                    ApplyState(targets[i]);
                }
        }
        catch (Exception ex) { App.CrashLog(ex); }
    }

    // At most once every 3 s (window activation).
    internal void RecheckThrottled()
    {
        if (Stopwatch.GetTimestamp() - lastCheck > 3 * Stopwatch.Frequency)
            _ = Recheck();
    }

    // Arrivée / retrait de lecteur : les rafales de messages se fondent en une vérification (minuteur à usage unique, vivant seulement
    // pendant qu'un événement est en attente).
    internal void ScheduleRecheck()
    {
        recheckTimer ??= new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RecheckDebounceMs) };
        recheckTimer.Tick -= OnRecheckTick;
        recheckTimer.Tick += OnRecheckTick;
        recheckTimer.Stop();
        recheckTimer.Start();
    }
    void OnRecheckTick(object? sender, EventArgs e)
    {
        recheckTimer?.Stop();
        _ = DevicesChanged();
    }

    void SetIcon(Entry e, TextBlock icon)
    {
        if (ReferenceEquals(e.Icon, icon))
            return;
        e.Row.Children.Remove(e.Icon);
        e.Row.Children.Add(icon);
        e.Icon = icon;
    }

    // Cible cassée : étiquette grisée, icône d'avertissement du rôle d'erreur de la palette, info-bulle avec le chemin. Une cible
    // fichier reçoit l'icône de fichier.
    void ApplyState(Entry e)
    {
        if (e.Kind == TargetKind.Missing)
            SetIcon(e, e.WarnIcon ??= UI.Symbol(WarnMat, WarnFluent, 20));
        else if (e.Kind == TargetKind.File)
            SetIcon(e, e.FileIcon ??= UI.Symbol(FileMat, FileFluent, 20));
        else
            SetIcon(e, e.BaseIcon);
        e.Pill.ToolTip = e.Kind == TargetKind.Missing ? "Introuvable : " + e.Path : e.Path;
        Paint(e);
    }

    // Ce que la liste affiche, pour l'auto-test.
    internal List<(string Path, string Label, TargetKind Kind, string Tip)> QuickView() =>
        entries.Where(e => e.Quick).Select(e => (e.Path, e.Text.Text, e.Kind, e.Pill.ToolTip as string ?? "")).ToList();

    IntPtr Hwnd => Window.GetWindow(Root) is { } w ? new WindowInteropHelper(w).Handle : IntPtr.Zero;

    async void ActivateQuick(Entry e)
    {
        try
        {
            var kind = e.Kind == TargetKind.Folder ? TargetKind.Folder : await QuickAccess.Probe(e.Path);
            if (entries.Contains(e) && e.Kind != kind)
            {
                e.Kind = kind;
                ApplyState(e);
            }
            if (kind == TargetKind.Folder)
                go(e.Path, false);
            else if (kind == TargetKind.File)
                openFile?.Invoke(e.Path);
            else
                ShowBrokenMenu(e.Path);
        }
        catch (Exception ex) { App.CrashLog(ex); }
    }

    const uint CmdRelocate = 1, CmdRemove = 2, CmdUp = 3, CmdDown = 4, CmdRename = 5, CmdLocation = 6;

    void ShowBrokenMenu(string path)
    {
        uint id = NativeMenu.Show(Hwnd, new NativeMenu.Item?[] { new(CmdRemove, "Retirer le raccourci"), new(CmdRelocate,
            "Retrouver la cible…") });
        Perform(path, id);
    }

    void ShowMenu(Entry e)
    {
        string path = e.Path;
        int i = QuickAccess.IndexOf(path);
        if (i < 0)
            return;
        bool missing = e.Kind == TargetKind.Missing;
        int n = QuickAccess.Entries.Count;
        var items = new List<NativeMenu.Item?>();
        if (missing)
        {
            items.Add(new(CmdRemove, "Retirer le raccourci"));
            items.Add(new(CmdRelocate, "Retrouver la cible…"));
            items.Add(null);
        }
        items.Add(new(CmdUp, "Monter", false, i == 0));
        items.Add(new(CmdDown, "Descendre", false, i == n - 1));
        items.Add(new(CmdRename, "Renommer le libellé"));
        if (!missing)
            items.Add(new(CmdRemove, "Retirer de l'Accès rapide"));
        items.Add(new(CmdLocation, "Ouvrir l'emplacement", false, ParentOf(path) == null));
        Perform(path, NativeMenu.Show(Hwnd, items));
    }

    static string? ParentOf(string path)
    {
        try
        {
            return System.IO.Path.GetDirectoryName(Key(path));
        }
        catch { return null; }
    }

    // Chaque action modifie la liste (ou navigue) ; aucune ne touche la cible sur disque.
    void Perform(string path, uint id)
    {
        int i = QuickAccess.IndexOf(path);
        switch (id)
        {
            case CmdUp:
                QuickAccess.Move(i, -1);
                break;
            case CmdDown:
                QuickAccess.Move(i, 1);
                break;
            case CmdRemove:
                QuickAccess.RemoveAt(i);
                break;
            case CmdRename:
                BeginRenameLabel(path);
                break;
            case CmdRelocate:
                Relocate(path);
                break;
            case CmdLocation:
                if (ParentOf(path) is { } parent)
                    go(parent, false);
                break;
        }
    }

    // Boîte de dialogue dossier ou fichier (selon l'allure de l'ancien nom) dont le choix remplace le chemin, en gardant étiquette et
    // position. Déjà épinglé ailleurs : l'entrée cassée est supprimée à la place.
    void Relocate(string path)
    {
        int i = QuickAccess.IndexOf(path);
        if (i < 0)
            return;
        string ext = System.IO.Path.GetExtension(Key(path));
        bool file = ext.Length > 1 && ext.Skip(1).Any(char.IsLetter);
        string title = "Retrouver la cible de « " + DefaultLabel(path) + " »";
        string? chosen = null;
        var owner = Window.GetWindow(Root);
        if (file)
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = title, CheckFileExists = true };
            if (d.ShowDialog(owner) == true)
                chosen = d.FileName;
        }
        else
        {
            var d = new Microsoft.Win32.OpenFolderDialog { Title = title };
            if (d.ShowDialog(owner) == true)
                chosen = d.FolderName;
        }
        if (string.IsNullOrEmpty(chosen))
            return;
        int j = QuickAccess.IndexOf(chosen);
        if (j >= 0 && j != i)
            QuickAccess.RemoveAt(i);
        else
            QuickAccess.Replace(i, chosen);
    }

    // Éditeur d'étiquette en ligne : Entrée ou la perte du focus la conserve, Échap annule, vide = retour au nom par défaut.
    TextBox? labelBox;
    internal bool Editing => labelBox != null && labelBox.IsKeyboardFocusWithin;

    internal void BeginRenameLabel(string path)
    {
        var e = entries.FirstOrDefault(x => x.Quick && string.Equals(Key(x.Path), Key(path), StringComparison.OrdinalIgnoreCase));
        if (e == null)
            return;
        var box = new TextBox
        {
            Text = e.Text.Text,
            FontSize = M3.FontSmall,
            FontFamily = M3.Body,
            Foreground = UI.White,
            CaretBrush = UI.White,
            Background = UI.T(M3Role.SurfaceHigh),
            BorderBrush = UI.T(M3Role.Primary),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(4, 0, 4, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            Height = 26,
            VerticalAlignment = VerticalAlignment.Center
        };
        e.Texts.Children.Remove(e.Text);
        e.Texts.Children.Insert(0, box);
        labelBox = box;
        bool done = false;
        void Finish(bool commit)
        {
            if (done)
                return;
            done = true;
            labelBox = null;
            string text = box.Text.Trim();
            e.Texts.Children.Remove(box);
            e.Texts.Children.Insert(0, e.Text);
            if (!commit)
                return;
            string? label = text.Length == 0 || text == DefaultLabel(path) ? null : text;
            int i = QuickAccess.IndexOf(path);
            if (i >= 0 && label != QuickAccess.Entries[i].Label)
                QuickAccess.SetLabel(i, label);
        }
        box.KeyDown += (_, k) =>
        {
            if (k.Key == System.Windows.Input.Key.Enter)
            {
                Finish(true);
                k.Handled = true;
            }
            else if (k.Key == System.Windows.Input.Key.Escape)
            {
                Finish(false);
                k.Handled = true;
            }
        };
        box.LostKeyboardFocus += (_, _) => Finish(true);
        box.MouseDown += (_, m) => m.Handled = true;
        box.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
    }
}
