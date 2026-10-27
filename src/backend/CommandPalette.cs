// language: C# 12, file: CommandPalette.cs, target: Windows 11 / .NET 8 WPF, quick tabs over one glass field
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PanneauxGlass;
internal sealed record PaletteResult(string Title,string Detail,string Glyph,Action Run,Entry? Entry=null);

// Opened by a global hotkey, with or without the launcher. It closes on Escape, on a click outside
// (the window loses activation) and on its own close button, so it can never trap the pointer.
internal sealed class CommandPalette:GlassWindow {
    readonly Coordinator owner;readonly TextBox query=new();readonly StackPanel list=new();readonly WrapPanel tabs=new(){Margin=new Thickness(0,14,0,0)};readonly ScrollViewer scroll;readonly TextBlock hint,footer;
    readonly List<PaletteResult> results=new();readonly List<Button> buttons=new();readonly double maxHeight;int selected,headers;bool suppress;
    internal CommandPalette(Coordinator coordinator):base("Panneaux Glass — commande rapide"){
        owner=coordinator;ShowActivated=true;Width=780;maxHeight=Math.Min(620,SystemParameters.WorkArea.Height-90);Height=Math.Min(520,maxHeight);WindowStartupLocation=WindowStartupLocation.CenterScreen;Radius=36;
        ContentHost.Margin=new Thickness(24,22,24,16);ContentHost.RowDefinitions.Add(new(){Height=GridLength.Auto});ContentHost.RowDefinitions.Add(new(){Height=GridLength.Auto});ContentHost.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});ContentHost.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var bar=new Grid();bar.ColumnDefinitions.Add(new(){Width=new GridLength(33)});bar.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});bar.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var glyph=UI.Glyph("\uE721",19);glyph.HorizontalAlignment=HorizontalAlignment.Left;bar.Children.Add(glyph);
        var field=new Grid();Grid.SetColumn(field,1);field.Children.Add(query);bar.Children.Add(field);
        query.FontSize=19;query.Background=Brushes.Transparent;query.BorderThickness=new Thickness(0);query.Padding=new Thickness(6,8,6,10);System.Windows.Automation.AutomationProperties.SetName(query,"Commande rapide");
        hint=UI.Text("Tape une application, un calcul, une action…",17,UI.Brush("#8FB4CBDD"));hint.Margin=new Thickness(13,0,0,1);hint.VerticalAlignment=VerticalAlignment.Center;hint.IsHitTestVisible=false;field.Children.Add(hint);
        var close=UI.IconButton("\uE711","Fermer la commande rapide",Close);close.Width=32;close.Height=32;close.Margin=new Thickness(8,0,4,0);Grid.SetColumn(close,2);bar.Children.Add(close);
        var plate=new Border{CornerRadius=new CornerRadius(20),Background=UI.Brush("#12FFFFFF"),BorderBrush=UI.Brush("#26FFFFFF"),BorderThickness=new Thickness(1),Child=bar,Height=58};ContentHost.Children.Add(plate);
        Grid.SetRow(tabs,1);ContentHost.Children.Add(tabs);
        scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,10,0,10),Padding=new Thickness(0,0,6,0),Content=list};
        Grid.SetRow(scroll,2);ContentHost.Children.Add(scroll);
        var footerRow=new Grid{Margin=new Thickness(13,0,4,0)};footerRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});footerRow.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        footer=UI.Text("↑ ↓ naviguer     ↵ ouvrir     Échap fermer",11,UI.Dim);footerRow.Children.Add(footer);
        var shortcuts=UI.Text("Onglets rapides · Modifier",11,UI.Dim);Grid.SetColumn(shortcuts,1);footerRow.Children.Add(shortcuts);
        Grid.SetRow(footerRow,3);ContentHost.Children.Add(footerRow);
        query.TextChanged+=(_,_)=>{hint.Visibility=query.Text.Length==0?Visibility.Visible:Visibility.Collapsed;Update();};
        query.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Down){Move(1);e.Handled=true;return;}if(e.Key==Key.Up){Move(-1);e.Handled=true;return;}if(e.Key==Key.Enter){var choice=results.Count==0?null:results[Math.Clamp(selected,0,results.Count-1)];if(choice!=null)Run(choice);e.Handled=true;}};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){Close();e.Handled=true;}};
        Deactivated+=(_,_)=>{if(!suppress)Close();};Closing+=(_,_)=>suppress=true;
        Loaded+=(_,_)=>{query.Focus();Animate();};Closed+=(_,_)=>{buttons.Clear();owner.Icons.CancelScope("palette");};
        BuildTabs();Update();
    }
    void Animate(){
        var scale=new ScaleTransform(.965,.965);ContentHost.RenderTransformOrigin=new Point(.5,.42);ContentHost.RenderTransform=scale;
        var ease=new QuinticEase{EasingMode=EasingMode.EaseOut};
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(.965,1,TimeSpan.FromMilliseconds(220)){EasingFunction=ease});
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(.965,1,TimeSpan.FromMilliseconds(220)){EasingFunction=ease});
        ContentHost.BeginAnimation(OpacityProperty,new DoubleAnimation(.2,1,TimeSpan.FromMilliseconds(170)){EasingFunction=ease});
    }
    void BuildTabs(){
        tabs.Children.Clear();
        foreach(var tab in Store.State.QuickTabs.Take(10)){
            var chip=UI.Button("",()=>RunTab(tab));chip.Padding=new Thickness(12,7,12,7);chip.Margin=new Thickness(0,0,7,7);chip.FontSize=12;chip.ToolTip=tab.Target;
            System.Windows.Automation.AutomationProperties.SetName(chip,tab.Name);
            var row=new StackPanel{Orientation=Orientation.Horizontal};var icon=UI.Glyph(tab.Glyph,14);icon.Margin=new Thickness(0,0,8,0);row.Children.Add(icon);row.Children.Add(UI.Text(tab.Name,12));chip.Content=row;
            tabs.Children.Add(chip);
        }
        var edit=UI.Button("",EditTabs);edit.Padding=new Thickness(12,7,12,7);edit.Margin=new Thickness(0,0,7,7);edit.FontSize=12;edit.ToolTip="Choisir les onglets rapides";
        var editRow=new StackPanel{Orientation=Orientation.Horizontal};var pencil=UI.Glyph("\uE70F",13);pencil.Margin=new Thickness(0,0,7,0);editRow.Children.Add(pencil);editRow.Children.Add(UI.Text("Modifier",12));edit.Content=editRow;tabs.Children.Add(edit);
    }
    internal void ReloadTabs(){BuildTabs();Update();}
    // Used by the off-screen render: the rows are filled without a keystroke.
    internal void Prefill(string text){query.Text=text;query.CaretIndex=text.Length;query.Focus();Update();}
    void EditTabs(){suppress=true;try{owner.EditQuickTabs(this);}finally{suppress=false;}if(IsVisible){Activate();BuildTabs();Update();}}
    void RunTab(WindowsShortcut tab){Close();Dispatcher.BeginInvoke((Action)(()=>owner.RunQuickTab(tab)));}
    void Move(int by){if(results.Count==0)return;selected=(selected+by+results.Count)%results.Count;Highlight();}
    void Highlight(){for(int i=0;i<buttons.Count;i++)buttons[i].Background=i==selected?UI.Brush("#2BD4EEFF"):Brushes.Transparent;if(selected>=0&&selected<buttons.Count)buttons[selected].BringIntoView();}
    void Run(PaletteResult item){Close();Dispatcher.BeginInvoke((Action)item.Run);}
    void Header(string title){headers++;var label=UI.Text(title.ToUpperInvariant(),10,UI.Brush("#9FC4D8E6"),true);label.Margin=new Thickness(13,12,0,5);list.Children.Add(label);}
    void Add(PaletteResult item){
        int index=results.Count;results.Add(item);
        var button=UI.Button("",()=>Run(item));button.Height=46;button.Margin=new Thickness(0,2,0,2);button.Padding=new Thickness(12,0,12,0);button.BorderThickness=new Thickness(0);button.Background=Brushes.Transparent;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
        System.Windows.Automation.AutomationProperties.SetName(button,item.Title);
        var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(40)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        if(item.Entry is Entry entry){
            // A rounded plate behind every logo: real app icons stay legible on glass and on paper.
            var plate=new Border{Width=30,Height=30,CornerRadius=new CornerRadius(9),Background=UI.Alpha(UI.White.Color,12),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,9,0)};
            var image=new Image{Width=23,Height=23,Stretch=Stretch.Uniform};RenderOptions.SetBitmapScalingMode(image,BitmapScalingMode.HighQuality);
            plate.Child=image;owner.Icons.Load(entry,source=>image.Source=source,"palette");row.Children.Add(plate);
        }else{
            var icon=UI.Glyph(item.Glyph,17);icon.HorizontalAlignment=HorizontalAlignment.Left;icon.Foreground=UI.Dim;icon.Margin=new Thickness(0,0,10,0);row.Children.Add(icon);
        }
        var title=UI.Text(item.Title,13);title.TextTrimming=TextTrimming.CharacterEllipsis;Grid.SetColumn(title,1);row.Children.Add(title);
        if(!string.IsNullOrEmpty(item.Detail)){var detail=UI.Text(item.Detail,11,UI.Dim);detail.Margin=new Thickness(16,0,4,0);detail.MaxWidth=340;detail.TextTrimming=TextTrimming.CharacterEllipsis;detail.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(detail,2);row.Children.Add(detail);}
        button.Content=row;button.MouseEnter+=(_,_)=>{selected=index;Highlight();};list.Children.Add(button);buttons.Add(button);
    }
    static string Short(string value)=>value.Length<=52?value:value[..51]+"…";
    static bool Matches(string haystack,string query)=>Store.Fold(query).Split(' ',StringSplitOptions.RemoveEmptyEntries).All(word=>Store.Fold(haystack).Contains(word));
    // Pure entry matching, also used by the integration checks.
    internal static List<Entry> Match(string query,int limit)=>Store.Sorted(Store.Visible("Tous",query)).Take(limit).ToList();
    void Update(){
        results.Clear();buttons.Clear();list.Children.Clear();headers=0;string text=query.Text.Trim();bool empty=text.Length==0;
        tabs.Visibility=empty?Visibility.Visible:Visibility.Collapsed;
        if(!empty&&Calculator.TryEvaluate(text,out double value))Add(new PaletteResult("= "+Calculator.Format(value),"Entrée copie le résultat","\uE8EF",()=>SystemActions.Copy(owner,value.ToString(CultureInfo.InvariantCulture),"Résultat")));
        if(!empty){
            var entries=Match(text,10);
            if(entries.Count>0){Header("Accès");foreach(var entry in entries)AddEntry(entry);}
            var sites=Store.State.Websites.Where(s=>Matches(s.Name+" "+s.Url,text)).Take(4).ToList();
            if(sites.Count>0){Header("Sites");foreach(var site in sites)AddSite(site);}
            AddCommands(text,6);
            if(entries.Count==0&&sites.Count==0&&!Commands(text).Any())Add(new PaletteResult("Rechercher « "+Short(text)+" » sur le web","DuckDuckGo · nouvel onglet","\uE774",()=>owner.WebSearch(text)));
        }else{
            var recent=Store.RecentEntries().Take(6).ToList();
            if(recent.Count>0){Header("Reprendre");foreach(var entry in recent)AddEntry(entry);}
            AddCommands(text,6);
            if(Store.State.Websites.Count>0){Header("Sites");foreach(var site in Store.State.Websites.Take(5))AddSite(site);}
            if(recent.Count==0&&Store.State.Websites.Count==0){var note=UI.Text("Tes onglets rapides sont au-dessus, tes derniers accès apparaîtront ici.",12,UI.Dim);note.Margin=new Thickness(14,8,14,16);note.TextWrapping=TextWrapping.Wrap;list.Children.Add(note);}
        }
        selected=0;Highlight();Height=Math.Clamp((empty?300:232)+results.Count*48+headers*28,260,maxHeight);
    }
    void AddEntry(Entry entry){string board=Store.State.Boards.FirstOrDefault(b=>b.Id==Store.BoardFor(entry))?.Title??"Accès";Add(new PaletteResult(entry.Name,board+" · "+Short(entry.Target),HomeWindow.BoardGlyph(Store.BoardFor(entry)),()=>owner.Launch(entry),entry));}
    void AddSite(WebShortcut site)=>Add(new PaletteResult(site.Name,"Site · "+Short(site.Url),"\uE774",()=>owner.WebSearch(site.Url,true),string.IsNullOrWhiteSpace(site.IconPath)?null:new Entry{Id="web:"+site.Id,IconPath=site.IconPath,Target=site.IconPath}));
    // Actions live in the palette so one field covers apps, sites and the everyday system gestures.
    // One field for every everyday gesture: the launcher, the workshop tools, and the system switches.
    List<PaletteResult> Commands(string text)=>new List<PaletteResult>{
        new("Accueil","Retour à l’espace du jour","\uE8A9",()=>owner.ShowHome()),
        new("Atelier","Tous les outils du quotidien, au même endroit","\uE74C",()=>owner.OpenTools()),
        new("Tout mon espace","Les quatre panneaux","\uE7C3",()=>owner.ShowWorkspace()),
        new("Capture annotée","Encadrer, dessiner, copier ou épingler","\uE722",()=>owner.OpenCapture()),
        new("Texte d’une image","Encadrer un texte, il est copié","\uE8A5",()=>owner.OpenCapture(textOnly:true)),
        new("Papier pense-bête","Une feuille à coller sur l’écran, gardée au redémarrage","\uE70B",()=>owner.OpenMemo()),
        new("Pipette de couleur","Code hexadécimal sous le curseur","\uE790",()=>owner.OpenColorPicker()),
        new("Règle pixel","Mesurer une distance à l’écran","\uE7A3",()=>owner.OpenRuler()),
        new("Presse-papiers","Tout ce qui a été copié, prêt à revenir","\uE8C8",()=>owner.OpenClipboard()),
        new("Son et micro","Changer de casque ou couper le micro","\uE7F5",()=>owner.OpenAudio()),
        new("Profils d’espace","Rouvrir des applications et une disposition","\uE7C3",()=>owner.OpenWorkspaces()),
        new("Gros fichiers","Ce qui remplit le disque","\uE8B7",()=>owner.OpenLargeFiles()),
        new("Épingler au premier plan","La fenêtre active reste devant · "+owner.PinShortcutLabel,"\uE840",()=>owner.ToggleAlwaysOnTop()),
        new("Notifications",SystemTools.NotificationsOn?"Couper les notifications Windows":"Rétablir les notifications","\uE7ED",()=>owner.ToggleNotifications()),
        new("Redémarrer l’Explorateur","Quand la barre des tâches se bloque","\uE72C",()=>owner.RestartExplorer()),
        new("Note rapide","Bloc-notes persistant","\uE70B",()=>owner.OpenNotes()),
        new("Minuteur","Régler une durée de focus","\uE917",()=>owner.OpenFocus()),
        new("Aide et raccourcis","Les gestes de Panneaux, en une page","\uE897",()=>owner.OpenHelp()),
        new("Onglets rapides","Modifier cette commande rapide","\uE70F",()=>owner.EditQuickTabs()),
        new("Personnaliser","Thèmes, verre, raccourcis, quotidien","\uE713",()=>owner.Settings()),
        new("Commandes Windows","Connexions, son, luminosité","\uE7F4",()=>owner.ToggleControls()),
        new("État du PC","Processeur, mémoire, disques","\uE950",()=>owner.TogglePc()),
        new("Calculatrice","Ouvrir la calculatrice de Windows","\uE8EF",()=>owner.LaunchWindowsShortcut(new WindowsShortcut{Name="Calculatrice",Target="calc.exe"})),
        new("Verrouiller le PC","Verrouille la session","\uE72E",()=>SystemActions.Lock(owner)),
        new("Gestionnaire des tâches","Processus et démarrage","\uE7C4",()=>SystemActions.TaskManager(owner))
    }.Where(item=>string.IsNullOrWhiteSpace(text)||Matches(item.Title+" "+item.Detail,text)).Take(6).ToList();
    void AddCommands(string text,int limit){var found=Commands(text).Take(limit).ToList();if(found.Count==0)return;Header("Actions");foreach(var item in found)Add(item);}
}
