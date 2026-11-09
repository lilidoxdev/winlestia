// Page "Ce PC" : une carte par lecteur avec une barre utilisé / libre (pinceaux de la palette ; rôle Error au-delà de 90 % utilisé).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PanneauxGlass;

namespace WinlestiaFiles;

public sealed partial class MainWindow
{
    ScrollViewer? thisPcPage; WrapPanel? thisPcWrap;
    int thisPcGeneration;
    // Ce que la page affiche (auto-test) : racine du lecteur, fraction utilisée, barre dans le rôle Error.
    internal readonly List<(string Root, double Used, bool Alert)> ThisPcCards = new();

    static string Gb(long bytes) => bytes >= 1L << 40 ? (bytes / (double)(1L << 40)).ToString("0.#") + " To"
        : (bytes / (double)(1L << 30)).ToString("0.#") + " Go";

    FrameworkElement ThisPcPageView()
    {
        if (thisPcPage == null)
        {
            thisPcWrap = new WrapPanel { Margin = new Thickness(M3.Sp3) };
            thisPcPage = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = thisPcWrap };
        }
        FillThisPc();
        return thisPcPage;
    }

    async void FillThisPc()
    {
        int gen = ++thisPcGeneration;
        try
        {
            var cards = await Task.Run(ThisPc.Read);
            if (gen != thisPcGeneration || thisPcWrap == null || closed)
                return;
            BuildCards(cards);
        }
        catch (Exception ex) { App.CrashLog(ex); }
    }

    internal void BuildCards(List<ThisPc.DriveCard> cards)
    {
        thisPcWrap!.Children.Clear();
        ThisPcCards.Clear();
        foreach (var d in cards)
        {
            double used = d.Total > 0 ? 1.0 - d.Free / (double)d.Total : 0;
            bool alert = d.Total > 0 && used > 0.9;
            ThisPcCards.Add((d.Root, used, alert));
            var icon = UI.Symbol("", "", 30);
            icon.Margin = new Thickness(0, 0, M3.Sp2, 0);
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Foreground = UI.Accent;
            string name = (d.Label.Length > 0 ? d.Label : d.Kind) + " (" + d.Root.TrimEnd('\\') + ")";
            var title = UI.Text(name, M3.FontSmall, UI.White);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            var bar = new Grid { Height = 8, Margin = new Thickness(0, 6, 0, 6) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(used, 0.0001), GridUnitType.Star) });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - used, 0.0001), GridUnitType.Star) });
            var track = new Border { CornerRadius = new CornerRadius(4), Background = UI.T(M3Role.SurfaceHigh) };
            Grid.SetColumnSpan(track, 2);
            var fill = new Border { CornerRadius = new CornerRadius(4), Background = UI.T(alert ? M3Role.Error : M3Role.Primary) };
            bar.Children.Add(track);
            bar.Children.Add(fill);
            var sub = UI.Text(d.Ready && d.Total > 0 ? $"{Gb(d.Free)} libres sur {Gb(d.Total)}" : "Non disponible", 11, UI.Label);
            var texts = new StackPanel();
            texts.Children.Add(title);
            texts.Children.Add(bar);
            texts.Children.Add(sub);
            var row = new DockPanel();
            DockPanel.SetDock(icon, Dock.Left);
            row.Children.Add(icon);
            row.Children.Add(texts);
            var root = d.Root;
            var card = new Border
            {
                Width = 300,
                Padding = new Thickness(M3.Sp3),
                Margin = new Thickness(0, 0, M3.Sp2, M3.Sp2),
                CornerRadius = new CornerRadius(M3.Normal),
                Background = UI.Tint,
                Child = row,
                Cursor = Cursors.Hand,
                ToolTip = root
            };
            card.MouseLeftButtonUp += (_, e) => { if (e.ClickCount <= 1) Active.Go(root); };
            card.MouseRightButtonUp += (_, e) => { ShowContextMenu(null, root); e.Handled = true; };
            thisPcWrap.Children.Add(card);
        }
    }
}
