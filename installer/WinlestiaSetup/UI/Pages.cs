using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WinlestiaSetup;

static class Pages
{
    public static string SelectedFeature = "taskbar";
    static ContentControl? detailHost;
    static StackPanel? featureList;

    public static FrameworkElement Build(MainWindow w, string id)
    {
        var root = id switch
        {
            "welcome" => Welcome(w),
            "license" => License(),
            "features" => FeaturesPage(w),
            "updates" => Updates(),
            "summary" => Summary(),
            "progress" => Progress(w),
            "done" => Done(),
            "uconfirm" => UConfirm(),
            _ => new Grid(),
        };
        return root;
    }

    static Grid Page(UIElement header, UIElement body)
    {
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition());
        g.Children.Add(header); Grid.SetRow(body, 1); g.Children.Add(body);
        return g;
    }

    // ---- 1. Accueil --------------------------------------------------------------------------------------------------------------
    static FrameworkElement Welcome(MainWindow w)
    {
        var info = Ctx.Info;
        var body = new StackPanel();
        if (Ctx.Demo) { var p = Ui.Pill(Loc.T("demo.note")); p.Margin = new Thickness(0, 0, 0, 16); body.Children.Add(p); }
        if (info.Exists)
        {
            var same = string.Equals(info.Version, Ctx.Version, StringComparison.OrdinalIgnoreCase);
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            head.Children.Add(Ui.Tx(info.Version, 22, "TextDimBrush", FontWeights.SemiBold, false));
            var arrow = Ui.Icon("", 16); arrow.Margin = new Thickness(14, 2, 14, 0); head.Children.Add(arrow);
            head.Children.Add(Ui.Tx(Ctx.Version, 22, "AccentBrush", FontWeights.SemiBold, false));
            var card = new StackPanel(); card.Children.Add(head);
            card.Children.Add(Ui.Tx(Loc.F("welcome.found", info.Dir), 13, "TextDimBrush"));
            body.Children.Add(Ui.Card(card, 16, new Thickness(20, 16, 20, 16)));
            var opts = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            void Opt(Mode m, string title, string desc)
            {
                var r = new RadioButton { Style = (Style)Application.Current.FindResource("OptionStyle"), GroupName = "mode",
                    IsChecked = Ctx.Mode == m, Margin = new Thickness(0, 0, 0, 10) };
                var s = new StackPanel(); s.Children.Add(Ui.Tx(title, 15, "TextBrush", FontWeights.SemiBold));
                var d = Ui.Tx(desc, 12.5, "TextDimBrush"); d.Margin = new Thickness(0, 2, 0, 0); s.Children.Add(d);
                r.Content = s; r.Checked += (_, _) => w.SetMode(m);
                opts.Children.Add(r);
            }
            Opt(Mode.Update, Loc.T(same ? "welcome.reinstall" : "welcome.update"), Loc.T("welcome.update.d"));
            Opt(Mode.Modify, Loc.T("welcome.modify"), Loc.T("welcome.modify.d"));
            Opt(Mode.Uninstall, Loc.T("welcome.uninstall"), Loc.T("welcome.uninstall.d"));
            body.Children.Add(opts);
            return Page(Ui.Title(Loc.T("welcome.title.installed"), Loc.T("welcome.sub.installed")), body);
        }
        body.Children.Add(Ui.Bullet("", Loc.T("welcome.b1"), 15));
        body.Children.Add(Ui.Bullet("", Loc.T("welcome.b2"), 15));
        body.Children.Add(Ui.Bullet("", Loc.T("welcome.b3"), 15));
        body.Children.Add(Ui.Bullet("", Loc.T("welcome.b4"), 15));
        return Page(Ui.Title(Loc.T("welcome.title"), Loc.T("welcome.sub")), body);
    }

    // ---- 2. Licence (informative, aucune acceptation requise : GPL-3.0 section 9) --------------------------------------------------
    static string? licenseText;
    static string LicenseText()
    {
        if (licenseText != null) return licenseText;
        using var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("LICENSE");
        licenseText = s == null ? "GNU General Public License v3.0 - https://www.gnu.org/licenses/gpl-3.0.html"
            : new StreamReader(s).ReadToEnd();
        return licenseText;
    }

    static FrameworkElement License()
    {
        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition());
        var top = new StackPanel();
        var sum = new StackPanel();
        sum.Children.Add(Ui.Bullet("", Loc.T("license.b1"), 13.5));
        sum.Children.Add(Ui.Bullet("", Loc.T("license.b2"), 13.5));
        sum.Children.Add(Ui.Bullet("", Loc.T("license.b3"), 13.5));
        sum.Children.Add(Ui.Bullet("", Loc.T("license.b4"), 13.5));
        top.Children.Add(Ui.Card(sum, 16, new Thickness(18, 16, 18, 6)));
        var tb = new TextBox
        {
            Text = LicenseText(), IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11.5,
            BorderThickness = new Thickness(0), Background = Brushes.Transparent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(0, 0, 12, 0),
        };
        tb.SetResourceReference(Control.ForegroundProperty, "TextDimBrush");
        var box = Ui.Card(tb, 16, new Thickness(18, 14, 8, 14)); box.Margin = new Thickness(0, 14, 0, 0);
        Grid.SetRow(box, 1);
        // Le titre et le résumé occupent la ligne 0, le texte remplit le reste.
        var hdr = new StackPanel(); hdr.Children.Add(Ui.Title(Loc.T("license.title"), Loc.T("license.sub"))); hdr.Children.Add(top);
        g.Children.Add(hdr); g.Children.Add(box);
        return g;
    }

    // ---- 3. Fonctionnalités ------------------------------------------------------------------------------------------------------
    static FrameworkElement FeaturesPage(MainWindow w)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(296) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        featureList = new StackPanel();
        var sv = new ScrollViewer { Content = featureList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 8, 0) };
        detailHost = new ContentControl { VerticalAlignment = VerticalAlignment.Top };
        Grid.SetColumn(detailHost, 2);
        RebuildFeatureList(w);
        var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(296) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); body.ColumnDefinitions.Add(new ColumnDefinition());
        body.Children.Add(sv); body.Children.Add(detailHost);
        detailHost.Content = Detail(Features.All.First(f => f.Id == SelectedFeature));
        return Page(Ui.Title(Loc.T("features.title"), Loc.T("features.sub")), body);
    }

    static void RebuildFeatureList(MainWindow w)
    {
        featureList!.Children.Clear();
        foreach (var f in Features.All)
        {
            var f0 = f; bool sel = f.Id == SelectedFeature;
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var ic = Ui.Icon(f.Glyph, 18); ic.HorizontalAlignment = HorizontalAlignment.Left;
            var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            txt.Children.Add(Ui.Tx(Loc.T("feat." + f.Id + ".name"), 14, "TextBrush", FontWeights.SemiBold, false));
            bool on = Ctx.Features[f.Id]; bool locked = f.Kind == FeatureKind.Locked;
            txt.Children.Add(Ui.Tx(Loc.T(locked ? "state.always" : on ? "state.on" : "state.off"), 12, "TextDimBrush", null, false));
            Grid.SetColumn(txt, 1);
            var sw = Ui.Switch(locked || on, !locked); Grid.SetColumn(sw, 2);
            sw.Click += (_, _) => { Ctx.Features[f0.Id] = sw.IsChecked == true; SelectedFeature = f0.Id; RebuildFeatureList(w);
                detailHost!.Content = Detail(f0); };
            row.Children.Add(ic); row.Children.Add(txt); row.Children.Add(sw);
            var card = Ui.Card(row, 14, new Thickness(14, 11, 14, 11), sel ? "CardHoverBrush" : "CardBrush");
            card.Cursor = System.Windows.Input.Cursors.Hand;
            if (sel) { card.BorderThickness = new Thickness(1.5); card.SetResourceReference(Border.BorderBrushProperty, "AccentBrush"); }
            card.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is DependencyObject d
                && IsInside<System.Windows.Controls.Primitives.ToggleButton>(d)) return; SelectedFeature =
                f0.Id; RebuildFeatureList(w); detailHost!.Content = Detail(f0); };
            card.Margin = new Thickness(0, 0, 0, 8); row.Margin = new Thickness(0);
            featureList.Children.Add(card);
        }
    }

    static bool IsInside<T>(DependencyObject d) where T : DependencyObject
    {
        for (var c = d; c != null; c = VisualTreeHelper.GetParent(c)) if (c is T) return true;
        return false;
    }

    static FrameworkElement Detail(Feature f)
    {
        bool on = Ctx.Features[f.Id], locked = f.Kind == FeatureKind.Locked;
        var p = new StackPanel();
        FrameworkElement pic;
        var brush = f.Preview != null ? Ui.Preview(f.Preview) : null;
        if (brush != null) pic = new Border { Height = 162, CornerRadius = new CornerRadius(14), Background = brush };
        else pic = Drawn.For(f.Id);
        p.Children.Add(pic);
        var head = new Grid { Margin = new Thickness(0, 12, 0, 6) };
        head.Children.Add(Ui.Tx(Loc.T("feat." + f.Id + ".name"), 17, "TextBrush", FontWeights.SemiBold, false));
        var pill = Ui.Pill(Loc.T(locked ? "state.always" : on ? "state.on" : "state.off"));
        pill.HorizontalAlignment = HorizontalAlignment.Right; head.Children.Add(pill);
        p.Children.Add(head);
        p.Children.Add(Ui.Tx(Loc.T("feat." + f.Id + ".desc"), 12.5, "TextDimBrush"));
        if (locked)
        {
            var c = Ui.Card(Ui.Tx(Loc.T("feat." + f.Id + ".locked"), 12.5, "TextBrush"), 12, new Thickness(14, 10, 14, 10),
                "CardHoverBrush"); c.Margin = new Thickness(0, 10, 0, 0); p.Children.Add(c);
        }
        else
        {
            p.Children.Add(Change(Loc.T("when.on"), Loc.T("feat." + f.Id + ".on"), on));
            p.Children.Add(Change(Loc.T("when.off"), Loc.T("feat." + f.Id + ".off"), !on));
        }
        var key = f.Kind == FeatureKind.Pref ? Loc.F("feat.key", string.Join(", ", f.Keys)) : f.Kind == FeatureKind.Action
            ? Loc.T("feat.action") : Loc.T("feat.nokey");
        var k = Ui.Tx(key, 11, "TextDimBrush"); k.Margin = new Thickness(0, 8, 0, 0); k.Opacity = 0.85; p.Children.Add(k);
        return p;
    }

    static FrameworkElement Change(string label, string text, bool active)
    {
        var s = new StackPanel();
        s.Children.Add(Ui.Tx(label, 11.5, active ? "AccentBrush" : "TextDimBrush", FontWeights.SemiBold, false));
        var t = Ui.Tx(text, 12.5, "TextBrush"); t.Margin = new Thickness(0, 2, 0, 0); s.Children.Add(t);
        var c = Ui.Card(s, 12, new Thickness(14, 9, 14, 10), active ? "CardHoverBrush" : "CardBrush"); c.Margin = new Thickness(0, 8, 0, 0);
        c.Opacity = active ? 1 : 0.62;
        return c;
    }

    // ---- 4. Mises à jour ---------------------------------------------------------------------------------------------------------
    static FrameworkElement Updates()
    {
        var b = new StackPanel();
        var how = new StackPanel();
        how.Children.Add(Ui.Tx(Loc.T("updates.how"), 15, "TextBrush", FontWeights.SemiBold));
        ((TextBlock)how.Children[0]).Margin = new Thickness(0, 0, 0, 12);
        how.Children.Add(Ui.Bullet("", Loc.T("updates.s1"), 13.5));
        how.Children.Add(Ui.Bullet("", Loc.T("updates.s2"), 13.5));
        how.Children.Add(Ui.Bullet("", Loc.T("updates.s3"), 13.5));
        how.Children.Add(Ui.Bullet("", Loc.T("updates.s4"), 13.5));
        b.Children.Add(Ui.Card(how, 16, new Thickness(20, 16, 20, 8)));
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var t = new StackPanel();
        var h = new StackPanel { Orientation = Orientation.Horizontal };
        h.Children.Add(Ui.Tx(Loc.T("updates.notify"), 14.5, "TextBrush", FontWeights.SemiBold, false));
        var pill = Ui.Pill(Loc.T("updates.pending")); pill.Margin = new Thickness(10, 0, 0, 0); h.Children.Add(pill);
        t.Children.Add(h);
        var d = Ui.Tx(Loc.T("updates.notify.d"), 12.5, "TextDimBrush"); d.Margin = new Thickness(0, 4, 24, 0); t.Children.Add(d);
        var sw = Ui.Switch(false, false); Grid.SetColumn(sw, 1);
        row.Children.Add(t); row.Children.Add(sw);
        var card = Ui.Card(row, 16, new Thickness(20, 16, 20, 16)); card.Margin = new Thickness(0, 14, 0, 0);
        b.Children.Add(card);
        return Page(Ui.Title(Loc.T("updates.title"), Loc.T("updates.sub")), b);
    }

    // ---- 5. Récapitulatif --------------------------------------------------------------------------------------------------------
    static FrameworkElement Summary()
    {
        var b = new StackPanel();
        var c = new StackPanel();
        string mode = Ctx.Mode switch { Mode.Install => "summary.install", Mode.Update => "summary.update", _ => "summary.modify" };
        c.Children.Add(Ui.Tx(Loc.T(mode), 15, "TextBrush", FontWeights.SemiBold));
        var loc = Ui.Tx(Loc.F("summary.where", Ctx.InstallDir), 12.5, "TextDimBrush"); loc.Margin = new Thickness(0, 4, 0, 12);
        c.Children.Add(loc);
        var grid = new UniformGrid2();
        foreach (var f in Features.All)
        {
            bool on = f.Kind == FeatureKind.Locked || Ctx.Features[f.Id];
            var r = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 7) };
            var ic = Ui.Icon(on ? "" : "", 12, on ? "AccentBrush" : "TextDimBrush"); ic.Margin = new Thickness(0, 0, 10, 0);
            r.Children.Add(ic); r.Children.Add(Ui.Tx(Loc.T("feat." + f.Id + ".name"), 13, on ? "TextBrush" : "TextDimBrush", null, false));
            grid.Children.Add(r);
        }
        c.Children.Add(grid);
        b.Children.Add(Ui.Card(c, 16, new Thickness(20, 16, 20, 10)));
        var n = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        if (Ctx.Mode is Mode.Install or Mode.Update)
        {
            n.Children.Add(Ui.Bullet("", Loc.T(Ctx.Info.Exists ? "summary.backup" : "summary.fresh"), 13));
            n.Children.Add(Ui.Bullet("", Loc.T("summary.close"), 13));
        }
        else n.Children.Add(Ui.Bullet("", Loc.T("summary.close"), 13));
        n.Children.Add(Ui.Bullet("", Loc.T("summary.nothing"), 13));
        b.Children.Add(n);
        return Page(Ui.Title(Loc.T("summary.title"), Loc.T("summary.sub")), b);
    }

    // ---- 5b. Progression ---------------------------------------------------------------------------------------------------------
    static List<string> stepKeys = new();
    static StackPanel? stepRows; static Border? fill; static TextBlock? logLine; static TextBlock? percent;
    const double TrackWidth = 560;

    public static void SetSteps(List<string> keys) { stepKeys = keys; }
    public static void MockSteps() { stepKeys = Installer.Steps().Select(s => s.Key).ToList(); }

    static FrameworkElement Progress(MainWindow w)
    {
        var b = new StackPanel();
        var track = new Border { Height = 10, Width = TrackWidth, CornerRadius = new CornerRadius(5),
            HorizontalAlignment = HorizontalAlignment.Left };
        track.SetResourceReference(Border.BackgroundProperty, "CardBrush");
        var g = new Grid { Width = TrackWidth, HorizontalAlignment = HorizontalAlignment.Left };
        fill = new Border { Height = 10, Width = 0, CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left };
        fill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
        g.Children.Add(track); g.Children.Add(fill);
        percent = Ui.Tx("0 %", 13, "TextDimBrush", null, false); percent.Margin = new Thickness(0, 8, 0, 0);
        stepRows = new StackPanel { Margin = new Thickness(0, 24, 0, 0) };
        logLine = Ui.Tx("", 11.5, "TextDimBrush", null, false); logLine.Margin = new Thickness(0, 14, 0, 0);
        logLine.TextTrimming = TextTrimming.CharacterEllipsis; logLine.Width = TrackWidth;
        b.Children.Add(g); b.Children.Add(percent); b.Children.Add(stepRows); b.Children.Add(logLine);
        if (Ctx.Screenshot) { MockSteps(); OnStep(3, 0.42); }
        else { if (stepKeys.Count == 0) MockSteps(); OnStep(0);
            w.Dispatcher.BeginInvoke(new Action(w.StartRun), System.Windows.Threading.DispatcherPriority.Background); }
        return Page(Ui.Title(Loc.T(Ctx.Mode == Mode.Uninstall ? "progress.title.u" : "progress.title"),
            Loc.T(Ctx.Mode == Mode.Uninstall ? "progress.sub.u" : "progress.sub")), b);
    }

    public static void OnStep(int i, double? frac = null)
    {
        if (stepRows == null || fill == null) return;
        int n = Math.Max(1, stepKeys.Count);
        double target = Math.Clamp(frac ?? (double)i / n, 0, 1);
        stepRows.Children.Clear();
        for (int k = 0; k < stepKeys.Count; k++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 9) };
            string glyph = k < i ? "" : k == i ? "" : ""; string brush = k < i ? "AccentBrush" : k == i ? "AccentBrush" : "OutlineBrush";
            var ic = Ui.Icon(glyph, 12, brush); ic.Width = 22; row.Children.Add(ic);
            row.Children.Add(Ui.Tx(Loc.T("step." + stepKeys[k]), 14, k == i ? "TextBrush" : k < i ? "TextDimBrush" : "TextDimBrush", k == i
                ? FontWeights.SemiBold : FontWeights.Normal, false));
            stepRows.Children.Add(row);
        }
        percent!.Text = (int)Math.Round(target * 100) + " %";
        if (Ctx.Screenshot || !MainWindow.Motion) fill.Width = TrackWidth * target;
        else
        {
            var a = new DoubleAnimation(TrackWidth * target, TimeSpan.FromMilliseconds(320)) {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            fill.BeginAnimation(FrameworkElement.WidthProperty, a);
            // La seule animation en boucle de tout le setup : une pulsation lente sur la barre pendant la modification des fichiers.
            if (i < n && fill.Opacity == 1)
                fill.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.7, TimeSpan.FromMilliseconds(900)) {
                    AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
            if (i >= n) fill.BeginAnimation(UIElement.OpacityProperty, null);
        }
        if (Ctx.Screenshot) logLine!.Text = "[dry-run] would: create scheduled task WinlestiaShell (logon, current user, no elevation)";
        else { lock (Log.Lines) logLine!.Text = Log.Lines.Count > 0 ? Log.Lines[^1].Substring(Math.Min(14, Log.Lines[^1].Length)) : ""; }
    }

    // ---- 6. Terminé --------------------------------------------------------------------------------------------------------------
    static FrameworkElement Done()
    {
        bool uninstall = Ctx.Mode == Mode.Uninstall; bool failed = Runner.Failure != null;
        var b = new StackPanel();
        if (Ctx.Demo && !uninstall) { var p = Ui.Pill(Loc.T("done.demo")); p.Margin = new Thickness(0, 0, 0, 14); b.Children.Add(p); }
        else if (Ctx.O.DryRun) { var p = Ui.Pill(Loc.T("done.dry")); p.Margin = new Thickness(0, 0, 0, 14); b.Children.Add(p); }
        if (failed) b.Children.Add(Ui.Card(Ui.Tx(Loc.F("done.failed.d", Runner.Failure!), 13.5, "TextBrush"), 16, null, "CardHoverBrush"));
        foreach (var w in Runner.Warnings) { var c = Ui.Card(Ui.Bullet("", w, 13), 14, new Thickness(16, 12, 16, 2));
            c.Margin = new Thickness(0, 0, 0, 8); b.Children.Add(c); }
        if (uninstall)
        {
            var s = new StackPanel();
            s.Children.Add(Ui.Bullet("", Loc.T(Sys.TrayPresent() || Ctx.Screenshot ? "done.u.taskbar" : "done.u.taskbar.no"), 13.5));
            s.Children.Add(Ui.Bullet("", Loc.T(Ctx.O.RemoveData ? "done.u.data.removed" : "done.u.data.kept"), 13.5));
            s.Children.Add(Ui.Bullet("", Loc.T("done.u.bin"), 13.5));
            b.Children.Add(Ui.Card(s, 16, new Thickness(20, 16, 20, 6)));
        }
        else if (!failed)
        {
            var cb = new CheckBox { Style = (Style)Application.Current.FindResource("CheckStyle"), IsChecked = Ctx.LaunchAfter,
                Content = Loc.T("done.launch"), Margin = new Thickness(0, 4, 0, 16) };
            cb.Click += (_, _) => Ctx.LaunchAfter = cb.IsChecked == true;
            b.Children.Add(cb);
            var dc = Ui.DiscordCard(); dc.Margin = new Thickness(0, 0, 0, 14); b.Children.Add(dc);
            var s = new StackPanel();
            s.Children.Add(Ui.Tx(Loc.T("safety.title"), 15, "TextBrush", FontWeights.SemiBold));
            ((TextBlock)s.Children[0]).Margin = new Thickness(0, 0, 0, 10);
            s.Children.Add(Ui.Bullet("", Loc.T("safety.s1"), 13));
            s.Children.Add(Ui.Bullet("", Loc.T("safety.s2"), 13));
            s.Children.Add(Ui.Bullet("", Loc.F("safety.s3",
                @"%LOCALAPPDATA%\Programs\Winlestia\Glass-v5\Panneaux.Glass.exe --restore-taskbar"), 13));
            s.Children.Add(Ui.Bullet("", Loc.T("safety.s4"), 13));
            b.Children.Add(Ui.Card(s, 16, new Thickness(20, 16, 20, 6)));
        }
        string title = failed ? "done.failed" : uninstall ? "done.u.title" : Ctx.Mode == Mode.Modify ? "done.modify" : "done.title";
        return Page(Ui.Title(Loc.T(title), Loc.T(failed ? "done.failed.sub" : uninstall ? "done.u.sub" : "done.sub")), b);
    }

    // ---- Confirmation de désinstallation -----------------------------------------------------------------------------------------
    static FrameworkElement UConfirm()
    {
        var b = new StackPanel();
        var g = new StackPanel();
        g.Children.Add(Ui.Tx(Loc.T("uconfirm.guarantees"), 14.5, "TextBrush", FontWeights.SemiBold));
        ((TextBlock)g.Children[0]).Margin = new Thickness(0, 0, 0, 10);
        g.Children.Add(Ui.Bullet("", Loc.T("uconfirm.g1"), 13));
        g.Children.Add(Ui.Bullet("", Loc.T("uconfirm.g2"), 13));
        g.Children.Add(Ui.Bullet("", Loc.F("uconfirm.g3", @"%LOCALAPPDATA%\Programs\Winlestia"), 13));
        g.Children.Add(Ui.Bullet("", Loc.T("uconfirm.g4"), 13));
        b.Children.Add(Ui.Card(g, 16, new Thickness(20, 16, 20, 6)));
        var opts = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        void Opt(bool remove, string title, string desc)
        {
            var r = new RadioButton { Style = (Style)Application.Current.FindResource("OptionStyle"), GroupName = "data",
                IsChecked = Ctx.O.RemoveData == remove, Margin = new Thickness(0, 0, 0, 8) };
            var s = new StackPanel(); s.Children.Add(Ui.Tx(title, 14, "TextBrush", FontWeights.SemiBold));
            var d = Ui.Tx(desc, 12, "TextDimBrush"); d.Margin = new Thickness(0, 2, 0, 0); s.Children.Add(d);
            r.Content = s; r.Checked += (_, _) => Ctx.O.RemoveData = remove; opts.Children.Add(r);
        }
        Opt(false, Loc.T("uconfirm.keep"), Loc.T("uconfirm.keep.d"));
        Opt(true, Loc.T("uconfirm.remove"), Loc.T("uconfirm.remove.d"));
        b.Children.Add(opts);
        return Page(Ui.Title(Loc.T("uconfirm.title"), Loc.T("uconfirm.sub")), b);
    }
}

// Panneau à deux colonnes avec retour à la ligne pour la liste du récapitulatif.
sealed class UniformGrid2 : System.Windows.Controls.Primitives.UniformGrid { public UniformGrid2() { Columns = 2; } }

// Aperçus dessinés en XAML là où le dépôt n'a pas de capture (Winjets, rails latéraux). Mêmes jetons que l'app : pinceaux de palette
// uniquement.
static class Drawn
{
    static Border Box(double x, double y, double w, double h, string bg, double r = 12)
    {
        var b = new Border { Width = w, Height = h, CornerRadius = new CornerRadius(r) };
        b.SetResourceReference(Border.BackgroundProperty, bg); Canvas.SetLeft(b, x); Canvas.SetTop(b, y); return b;
    }

    public static FrameworkElement For(string id)
    {
        var c = new Canvas { Width = 394, Height = 162, ClipToBounds = true };
        var bg = new Border { Width = 394, Height = 162, CornerRadius = new CornerRadius(14) };
        bg.SetResourceReference(Border.BackgroundProperty, "SidebarBrush");
        var glow = Box(220, -60, 240, 200, "AccentSoftBrush", 100);
        c.Children.Add(bg);
        if (id == "winjets")
        {
            var clock = Box(22, 22, 128, 72, "CardHoverBrush"); c.Children.Add(clock);
            var t = Ui.Tx("10:09", 30, "TextBrush", FontWeights.SemiBold, false); Canvas.SetLeft(t, 40); Canvas.SetTop(t, 34);
            c.Children.Add(t);
            var d = Ui.Tx("Tue 17 Nov", 11, "TextDimBrush", null, false); Canvas.SetLeft(d, 42); Canvas.SetTop(d, 72); c.Children.Add(d);
            c.Children.Add(Box(164, 22, 100, 100, "CardHoverBrush"));
            for (int r = 0; r < 5; r++) for (int k = 0; k < 7; k++)
            {
                var dot = Box(176 + k * 12, 38 + r * 14, 6, 6, r == 2 && k == 3 ? "AccentBrush" : "OutlineBrush", 3); c.Children.Add(dot);
            }
            c.Children.Add(Box(278, 22, 94, 72, "CardHoverBrush"));
            double[] hs = { 22, 34, 18, 40, 28 };
            for (int k = 0; k < hs.Length; k++) c.Children.Add(Box(292 + k * 15, 82 - hs[k], 9, hs[k], k == 3 ? "AccentBrush"
                : "OutlineBrush", 4));
            c.Children.Add(Box(22, 106, 128, 38, "CardBrush"));
            c.Children.Add(Box(278, 106, 94, 38, "CardBrush"));
        }
        else // rails latéraux
        {
            c.Children.Add(Box(14, 14, 40, 134, "CardHoverBrush", 20));
            for (int k = 0; k < 4; k++) c.Children.Add(Box(22, 26 + k * 30, 24, 24, k == 1 ? "AccentBrush" : "OutlineBrush", 12));
            c.Children.Add(Box(70, 30, 150, 100, "CardHoverBrush", 14));
            for (int k = 0; k < 3; k++) { c.Children.Add(Box(84, 46 + k * 28, 20, 20, k == 0 ? "AccentBrush" : "OutlineBrush", 10));
                c.Children.Add(Box(114, 52 + k * 28, 90 - k * 14, 8, "OutlineBrush", 4)); }
        }
        c.HorizontalAlignment = HorizontalAlignment.Center;
        var outer = new Border { Height = 162, CornerRadius = new CornerRadius(14), Child = c, ClipToBounds = true };
        outer.SetResourceReference(Border.BackgroundProperty, "SidebarBrush");
        return outer;
    }
}
