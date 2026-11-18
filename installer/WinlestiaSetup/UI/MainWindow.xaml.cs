using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace WinlestiaSetup;

internal partial class MainWindow : Window
{
    public static readonly DependencyProperty TProperty = DependencyProperty.Register("T", typeof(double), typeof(MainWindow),
        new PropertyMetadata(1.0, (d, e) => ((MainWindow)d).ApplyT((double)e.NewValue)));

    List<string> flow = new();
    int idx;
    FrameworkElement? currentEl, outgoing, incoming;
    int dir = 1;
    bool running;
    public string PageId => flow.Count > idx ? flow[idx] : "";

    public static bool Motion => SystemParameters.ClientAreaAnimation && !Ctx.O.NoMotion && !Ctx.Screenshot;

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = Loc.F("version", Ctx.Version);
        if (Ctx.O.DryRun || Ctx.Demo)
        {
            DryBadge.Visibility = Visibility.Visible;
            DryText.Text = Ctx.O.DryRun ? Loc.T("badge.dry") : Loc.T("badge.demo");
        }
        MouseLeftButtonDown += (s, e) => { if (e.ButtonState == MouseButtonState.Pressed && e.GetPosition(this).Y < 56) try {
            DragMove(); } catch { } };
        SourceInitialized += (s, e) => { if (!Ctx.Screenshot) Native.ApplyGlass(this); };
        KeyDown += (s, e) => { if (e.Key == Key.Escape && !running) OnClose(null, null!); };
        flow = FlowFor(Ctx.Mode);
        Loaded += (s, e) => { if (!Ctx.Screenshot) Go(0, false); };
    }

    // ---- déroulement ------------------------------------------------------------------------------------------------------------
    List<string> FlowFor(Mode m)
    {
        if (Ctx.O.Uninstall && m == Mode.Uninstall) return new() { "uconfirm", "progress", "done" };
        return m switch
        {
            Mode.Install => new() { "welcome", "license", "features", "updates", "summary", "progress", "done" },
            Mode.Update => new() { "welcome", "updates", "summary", "progress", "done" },
            Mode.Modify => new() { "welcome", "features", "summary", "progress", "done" },
            _ => new() { "welcome", "uconfirm", "progress", "done" },
        };
    }

    public void SetMode(Mode m)
    {
        Ctx.Mode = m;
        var id = PageId; flow = FlowFor(m); idx = Math.Max(0, flow.IndexOf(id));
        BuildSidebar();
        UpdateButtons();
    }

    static string Group(string id) => id switch
    {
        "welcome" => "nav.welcome", "license" => "nav.license", "features" => "nav.features", "updates" => "nav.updates",
        "uconfirm" => "nav.confirm", "done" => "nav.done",
        _ => Ctx.Mode == Mode.Uninstall ? "nav.uninstall" : "nav.install",
    };

    void BuildSidebar()
    {
        StepsPanel.Children.Clear();
        var groups = flow.Select(Group).Distinct().ToList(); var active = Group(PageId);
        foreach (var g in groups)
        {
            bool on = g == active;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
            var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 1, 14, 0) };
            dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, on ? "AccentBrush" : "OutlineBrush");
            var t = Ui.Tx(Loc.T(g), 14, on ? "TextBrush" : "TextDimBrush", on ? FontWeights.SemiBold : FontWeights.Normal);
            row.Children.Add(dot); row.Children.Add(t); StepsPanel.Children.Add(row);
        }
    }

    // ---- navigation -------------------------------------------------------------------------------------------------------------
    public void Go(int newIdx, bool animate = true, bool hold = false)
    {
        int d = newIdx >= idx ? 1 : -1; idx = newIdx;
        BuildSidebar();
        var el = Pages.Build(this, PageId);
        Begin(el, d, animate, hold);
        UpdateButtons();
    }

    public void GoTo(string id, bool animate = false, bool hold = false) { var i = flow.IndexOf(id); if (i >= 0) Go(i, animate, hold); }

    void OnNext(object s, RoutedEventArgs e) { if (idx < flow.Count - 1) Go(idx + 1); else Close(); }
    void OnBack(object s, RoutedEventArgs e) { if (idx > 0) Go(idx - 1); }
    void OnClose(object? s, RoutedEventArgs e) { if (!running) Close(); }

    public void Refresh() => UpdateButtons();

    void UpdateButtons()
    {
        var id = PageId;
        BackButton.Content = Loc.T("btn.back");
        BackButton.Visibility = idx == 0 || id is "progress" or "done" ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Visibility = id == "progress" ? Visibility.Collapsed : Visibility.Visible;
        NextButton.Content = id switch
        {
            "summary" => Loc.T(Ctx.Mode == Mode.Install ? "btn.install" : Ctx.Mode == Mode.Update ? "btn.update" : "btn.apply"),
            "uconfirm" => Loc.T("btn.uninstall"),
            "done" => Loc.T("btn.finish"),
            _ => Loc.T("btn.next"),
        };
        NextButton.IsEnabled = !running;
        CloseButton.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
    }

    // Passer à « progress » lance l'exécution ; les pages ne font que la décrire.
    protected override void OnPreviewMouseDown(MouseButtonEventArgs e) { base.OnPreviewMouseDown(e); }

    public async void StartRun()
    {
        running = true; UpdateButtons();
        var steps = Ctx.Mode == Mode.Uninstall ? Uninstaller.Steps() : Installer.Steps();
        Pages.SetSteps(steps.Select(s => s.Key).ToList());
        await Runner.Run(steps, Ctx.Mode != Mode.Uninstall, (i, k) => Dispatcher.Invoke(() => Pages.OnStep(i)));
        await Task.Delay(Motion ? 650 : 100);
        running = false; Go(idx + 1);
    }

    // ---- transition : glissement + fondu, pilotée par une seule propriété adoucie pour rendre les images de façon déterministe ----
    public void ApplyT(double t)
    {
        if (incoming?.RenderTransform is TranslateTransform ti) { incoming.Opacity = Math.Clamp(t * 1.15, 0, 1); ti.X = dir * 36 * (1 - t);
            }
        if (outgoing?.RenderTransform is TranslateTransform to) { outgoing.Opacity = Math.Clamp(1 - t * 1.8, 0, 1); to.X = -dir * 24 * t; }
    }

    void Begin(FrameworkElement next, int d, bool animate, bool hold)
    {
        FinishTransition();
        outgoing = currentEl; incoming = next; dir = d; currentEl = next;
        next.RenderTransform = new TranslateTransform();
        if (outgoing != null) { outgoing.RenderTransform = new TranslateTransform(); outgoing.IsHitTestVisible = false; }
        PageHost.Children.Add(next);
        if (hold) { ApplyT(0); return; }
        if (!animate || !Motion || outgoing == null) { FinishTransition(); return; }
        ApplyT(0);
        var a = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)) {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        a.Completed += (s, e) => FinishTransition();
        BeginAnimation(TProperty, a);
    }

    public void FinishTransition()
    {
        var inc = incoming; var outg = outgoing; incoming = null; outgoing = null;
        BeginAnimation(TProperty, null);
        if (outg != null) PageHost.Children.Remove(outg);
        if (inc != null) { inc.Opacity = 1; if (inc.RenderTransform is TranslateTransform t) t.X = 0; }
    }

    public void SetFrame(double t) => ApplyT(t);
    public FrameworkElement RenderRoot => Chrome;
    public Grid Backdrop => BackdropSim;
}
