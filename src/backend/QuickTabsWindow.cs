// language: C# 12, file: QuickTabsWindow.cs, target: Windows 11 / .NET 8 WPF, quick tabs and palette hotkey
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace PanneauxGlass;
// One editor for both halves of the palette: the tabs above the field and the global key that
// summons it. Every change is written straight to preferences, no separate save step.
internal sealed class QuickTabsWindow:GlassWindow {
    readonly Coordinator owner;readonly StackPanel list=new();readonly TextBlock status;
    internal static readonly (string Target,string Name,string Glyph)[] Views={
        ("glass:home","Accueil","\uE8A9"),("glass:apps","Applications","\uE71D"),("glass:games","Jeux","\uE7FC"),
        ("glass:projects","Projets","\uE943"),("glass:documents","Documents","\uE8B7"),("glass:workspace","Les quatre panneaux","\uE7C3"),
        ("glass:note","Note rapide","\uE70B"),("glass:focus","Minuteur","\uE917"),("glass:settings","Personnalisation","\uE713"),
        ("glass:controls","Commandes Windows","\uE7F4"),("glass:pc","État du PC","\uE950"),("glass:palette","Commande rapide","\uE721"),
        ("glass:tools","Atelier","\uE74C"),("glass:capture","Capture annotée","\uE722"),("glass:text","Texte d’une image","\uE8A5"),
        ("glass:memo","Papier pense-bête","\uE70B"),("glass:clipboard","Presse-papiers","\uE8C8"),("glass:audio","Son et micro","\uE7F5"),("glass:workspaces","Profils d’espace","\uE7C3"),
        ("glass:files","Gros fichiers","\uE8B7"),("glass:help","Aide","\uE897"),("glass:pin","Épingler au premier plan","\uE840")
    };
    static readonly (string Glyph,string Name)[] Icons={
        ("\uE71D","Applications"),("\uE7FC","Jeux"),("\uE8B7","Dossier"),("\uE70B","Note"),("\uE917","Minuteur"),("\uE713","Réglages"),
        ("\uE721","Recherche"),("\uE950","Moniteur"),("\uE722","Capture"),("\uE8D6","Musique"),("\uE774","Web"),("\uE734","Favori"),
        ("\uE756","Terminal"),("\uE7F4","Contrôles"),("\uE8A9","Espace"),("\uE7C3","Grille")
    };
    internal QuickTabsWindow(Coordinator coordinator):base("Panneaux Glass — onglets rapides"){
        owner=coordinator;Width=760;Height=Math.Min(820,SystemParameters.WorkArea.Height-80);WindowStartupLocation=WindowStartupLocation.CenterOwner;Radius=34;ShowActivated=true;
        ContentHost.Margin=new Thickness(28,24,28,20);ContentHost.RowDefinitions.Add(new(){Height=GridLength.Auto});ContentHost.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});ContentHost.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new DockPanel{Margin=new Thickness(0,0,0,6)};var close=UI.IconButton("\uE711","Fermer les onglets rapides",Close);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        var titles=new StackPanel();titles.Children.Add(UI.Text("Tes onglets rapides.",26,null,true));
        var sub=UI.Text("Ce que tu vois en haut de la commande rapide · renomme, change l’icône, réordonne",12,UI.Dim);sub.Margin=new Thickness(0,7,0,0);titles.Children.Add(sub);header.Children.Add(titles);ContentHost.Children.Add(header);
        var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,18,0,10),Content=list,Padding=new Thickness(0,0,6,0)};Grid.SetRow(scroll,1);ContentHost.Children.Add(scroll);
        status=UI.Text("",12,UI.Dim);status.TextWrapping=TextWrapping.Wrap;Grid.SetRow(status,2);ContentHost.Children.Add(status);
        Rebuild();
        Loaded+=(_,_)=>ApplyGlass();
    }
    void Rebuild(){
        list.Children.Clear();var tabs=Store.State.QuickTabs;
        for(int index=0;index<tabs.Count;index++){
            var tab=tabs[index];int position=index;
            var card=new Border{CornerRadius=new CornerRadius(18),Padding=new Thickness(10),Margin=new Thickness(0,0,0,8),Background=UI.T(M3Role.OnSurface,0x0C),BorderBrush=UI.T(M3Role.OnSurface,0x20),BorderThickness=new Thickness(1)};
            var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(46)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            Button icon=UI.Button("",()=>{ });
            icon.Content=UI.Glyph(tab.Glyph,16);icon.Width=38;icon.Height=38;icon.Padding=new Thickness(0);icon.ToolTip="Changer l’icône";System.Windows.Automation.AutomationProperties.SetName(icon,"Icône de "+tab.Name);
            Button glyphTarget=icon;icon.Click+=(_,_)=>PickGlyph(glyphTarget,tab);row.Children.Add(icon);
            var name=new TextBox{Text=tab.Name,FontSize=13,Padding=new Thickness(10,6,10,6),Margin=new Thickness(0,0,8,0),VerticalContentAlignment=VerticalAlignment.Center};System.Windows.Automation.AutomationProperties.SetName(name,"Nom de l’onglet "+tab.Name);
            name.TextChanged+=(_,_)=>{if(string.IsNullOrWhiteSpace(name.Text))return;tab.Name=name.Text.Trim();Store.Save();owner.RefreshPaletteLabels();};Grid.SetColumn(name,1);row.Children.Add(name);
            Button target=UI.Button(Label(tab.Target),()=>{ });
            target.Padding=new Thickness(12,7,12,7);target.FontSize=12;target.MaxWidth=250;target.ToolTip=tab.Target;System.Windows.Automation.AutomationProperties.SetName(target,"Cible de "+tab.Name);Grid.SetColumn(target,2);row.Children.Add(target);
            Button targetAnchor=target;target.Click+=(_,_)=>PickTarget(targetAnchor,tab);
            var tools=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(8,0,0,0)};Grid.SetColumn(tools,3);row.Children.Add(tools);
            var up=UI.IconButton("\uE74A","Monter "+tab.Name,()=>Move(position,-1));up.Width=32;up.Height=32;up.Margin=new Thickness(0,0,4,0);tools.Children.Add(up);
            var down=UI.IconButton("\uE74B","Descendre "+tab.Name,()=>Move(position,1));down.Width=32;down.Height=32;down.Margin=new Thickness(0,0,4,0);tools.Children.Add(down);
            var remove=UI.IconButton("\uE74D","Retirer "+tab.Name,()=>{tabs.Remove(tab);Store.Save();Rebuild();owner.RefreshPaletteLabels();});remove.Width=32;remove.Height=32;tools.Children.Add(remove);
            card.Child=row;list.Children.Add(card);
        }
        var add=UI.Button("+  Ajouter un onglet",Add);add.HorizontalAlignment=HorizontalAlignment.Left;add.Padding=new Thickness(16,9,16,9);add.Margin=new Thickness(0,6,0,22);list.Children.Add(add);
        list.Children.Add(UI.Text("OUVRIR LA COMMANDE RAPIDE",10,UI.Label,true));
        var note=UI.Text("Un raccourci global, actif même launcher fermé, depuis n’importe quelle application. Ctrl F2 par défaut.",12,UI.Dim);note.Margin=new Thickness(0,8,0,12);note.TextWrapping=TextWrapping.Wrap;list.Children.Add(note);
        BuildShortcut();
    }
    static readonly (string Prefix,string Name)[] Protocols={
        ("ms-screenclip:","Capture d’écran"),("ms-settings:","Réglages Windows"),("ms-windows-store:","Microsoft Store"),
        ("ms-gamebar:","Barre de jeu"),("shell:","Dossier Windows"),("explorer:","Explorateur"),("ms-","Commande Windows")
    };
    static string Label(string target){
        var view=Views.FirstOrDefault(v=>string.Equals(v.Target,target,StringComparison.OrdinalIgnoreCase));
        if(!string.IsNullOrEmpty(view.Name))return view.Name;
        if(target.StartsWith("glass:",StringComparison.OrdinalIgnoreCase))return target[6..];
        foreach(var protocol in Protocols)if(target.StartsWith(protocol.Prefix,StringComparison.OrdinalIgnoreCase))return protocol.Name;
        try{return Path.GetFileNameWithoutExtension(target);}catch{return target;}
    }
    void PickGlyph(Button anchor,WindowsShortcut tab)=>GlassMenu.Open(owner,this,anchor,"Choisir une icône",Icons.Select(i=>new MenuAction(i.Name,i.Glyph,()=>{tab.Glyph=i.Glyph;Store.Save();Rebuild();owner.RefreshPaletteLabels();},tab.Glyph==i.Glyph)).ToArray());
    void PickTarget(Button anchor,WindowsShortcut tab){
        var actions=new List<MenuAction>();
        foreach(var view in Views)actions.Add(new MenuAction(view.Name,view.Glyph,()=>{tab.Target=view.Target;Store.Save();Rebuild();owner.RefreshPaletteLabels();},string.Equals(tab.Target,view.Target,StringComparison.OrdinalIgnoreCase)));
        actions.Add(new MenuAction("Choisir une application…","\uE8B7",()=>PickApplication(tab),true));
        GlassMenu.Open(owner,this,anchor,"Cible de l’onglet",actions.ToArray());
    }
    void PickApplication(WindowsShortcut tab){
        var picker=new OpenFileDialog{Title="Choisir l’application de cet onglet",Filter="Applications et raccourcis|*.exe;*.lnk;*.bat;*.cmd;*.url|Tous les fichiers|*.*"};
        if(picker.ShowDialog(this)!=true)return;
        tab.Target=picker.FileName;
        if(string.IsNullOrWhiteSpace(tab.Name)||Views.Any(v=>v.Name==tab.Name))tab.Name=Path.GetFileNameWithoutExtension(picker.FileName);
        Store.Save();Rebuild();owner.RefreshPaletteLabels();
    }
    void Add(){
        if(Store.State.QuickTabs.Count>=12){status.Text="Douze onglets au maximum : au-delà, la palette devient un menu.";return;}
        Store.State.QuickTabs.Add(new WindowsShortcut{Name="Nouvel onglet",Target="glass:home",Glyph="\uE8A9"});
        Store.Save();Rebuild();owner.RefreshPaletteLabels();
    }
    void Move(int index,int by){
        var tabs=Store.State.QuickTabs;int next=index+by;if(next<0||next>=tabs.Count)return;
        var tab=tabs[index];tabs.RemoveAt(index);tabs.Insert(next,tab);Store.Save();Rebuild();owner.RefreshPaletteLabels();
    }
    void BuildShortcut(){
        var current=Store.State.PaletteShortcut;uint key=current.Key;
        var row=new WrapPanel{Margin=new Thickness(0,0,0,6)};
        CheckBox ctrl=new(){Content="Ctrl",IsChecked=(current.Modifiers&2)!=0,Margin=new Thickness(0,0,18,8)},alt=new(){Content="Alt",IsChecked=(current.Modifiers&1)!=0,Margin=new Thickness(0,0,18,8)},shift=new(){Content="Maj",IsChecked=(current.Modifiers&4)!=0,Margin=new Thickness(0,0,18,8)};
        foreach(var check in new[]{ctrl,alt,shift})row.Children.Add(check);
        Button? keyButton=null;
        keyButton=UI.Button(GlobalShortcut.KeyName(key)+"  ⌄",()=>GlassMenu.Open(owner,this,keyButton!,"Touche de la commande rapide",GlobalShortcut.Keys.Select(k=>new MenuAction(GlobalShortcut.KeyName(k),"\uE765",()=>{key=k;keyButton!.Content=GlobalShortcut.KeyName(k)+"  ⌄";},key==k))));
        keyButton.MinWidth=84;keyButton.Margin=new Thickness(0,0,14,8);System.Windows.Automation.AutomationProperties.SetName(keyButton,"Choisir la touche de la commande rapide");row.Children.Add(keyButton);
        uint Modifiers()=>(ctrl.IsChecked==true?2u:0)|(alt.IsChecked==true?1u:0)|(shift.IsChecked==true?4u:0);
        var apply=UI.Button("Appliquer",()=>{
            if(!owner.SetPaletteShortcut(Modifiers(),key,out var error)){status.Text=error;status.Foreground=UI.Brush("#F3CF91");return;}
            status.Text="Actif : "+owner.PaletteLabel+" ouvre la commande rapide, même launcher fermé.";status.Foreground=UI.Brush("#A0E7CF");
        });
        apply.Padding=new Thickness(14,7,14,7);apply.Margin=new Thickness(0,0,0,8);System.Windows.Automation.AutomationProperties.SetName(apply,"Appliquer le raccourci de la commande rapide");row.Children.Add(apply);
        list.Children.Add(row);
        status.Text=string.IsNullOrEmpty(owner.PaletteStatus)?(Store.State.QuickTabs.Count)+" onglet"+(Store.State.QuickTabs.Count==1?"":"s")+" · actif : "+owner.PaletteLabel:owner.PaletteStatus;status.Foreground=UI.Dim;
    }
}
