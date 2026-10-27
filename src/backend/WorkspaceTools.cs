// language: C# 12, file: WorkspaceTools.cs, target: Windows 11 / .NET 8 WPF, workspace profiles and name prompt
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PanneauxGlass;
internal sealed class PromptWindow:GlassWindow {
    readonly TextBox field;readonly TextBlock error;string? answer;
    internal static string? Ask(Coordinator owner,string title,string label,string value="",Window? parent=null){
        var window=new PromptWindow(owner,title,label,value){Owner=parent??owner.Anchor,ShowInTaskbar=owner.PreviewMode};
        return window.ShowDialog()==true?window.answer:null;
    }
    PromptWindow(Coordinator owner,string title,string label,string value):base("Panneaux Glass — "+title){
        Width=520;Height=300;WindowStartupLocation=WindowStartupLocation.CenterOwner;Radius=30;ShowActivated=true;
        ContentHost.Margin=new Thickness(26,24,26,20);
        var root=new StackPanel();ContentHost.Children.Add(root);
        var header=new DockPanel();var close=UI.IconButton("\uE711","Annuler",()=>{DialogResult=false;});DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);header.Children.Add(UI.Text(title,23,null,true));root.Children.Add(header);
        var explain=UI.Text(label,12,UI.Dim);explain.TextWrapping=TextWrapping.Wrap;explain.Margin=new Thickness(0,8,0,16);root.Children.Add(explain);
        field=new TextBox{Text=value,FontSize=15,Padding=new Thickness(12,9,12,9)};System.Windows.Automation.AutomationProperties.SetName(field,label);root.Children.Add(field);
        error=UI.Text("",11,UI.Brush("#F3CF91"));error.Margin=new Thickness(2,8,0,0);root.Children.Add(error);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0)};
        actions.Children.Add(UI.Button("Annuler",()=>DialogResult=false));
        var ok=UI.Button("Valider",Accept);ok.MinWidth=110;ok.Margin=new Thickness(10,0,0,0);ok.Background=UI.Brush("#33BFE9F7");actions.Children.Add(ok);root.Children.Add(actions);
        field.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter){Accept();e.Handled=true;}};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){DialogResult=false;e.Handled=true;}};
        Loaded+=(_,_)=>{ApplyGlass();field.Focus();field.SelectAll();};
    }
    void Accept(){string text=field.Text.Trim();if(text.Length==0){error.Text="Un nom est nécessaire.";return;}answer=text;DialogResult=true;}
}
internal static class WorkspaceService {
    internal static WorkspaceProfile Capture(Coordinator owner,string name){
        var targets=new List<string>();
        foreach(var process in Process.GetProcesses()){
            if(process.Id==Environment.ProcessId||process.MainWindowHandle==IntPtr.Zero)continue;
            string id=process.ProcessName.ToLowerInvariant();
            if(id is "explorer" or "textinputhost" or "shellexperiencehost" or "searchhost" or "startmenuexperiencehost" or "applicationframehost")continue;
            try{string? path=process.MainModule?.FileName;if(string.IsNullOrWhiteSpace(path)||!path.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))continue;if(!targets.Any(existing=>string.Equals(existing,path,StringComparison.OrdinalIgnoreCase)))targets.Add(path);}catch{}
        }
        var profile=new WorkspaceProfile{Name=name,Targets=targets.Take(16).ToList()};
        foreach(var board in Store.State.Boards)profile.Layout[board.Id]=board.Placement is {Valid:true} placement?new PanelPlacement{X=placement.X,Y=placement.Y,Width=placement.Width,Height=placement.Height}:null;
        return profile;
    }
    internal static void Open(Coordinator owner,WorkspaceProfile profile){
        int launched=0,failed=0;
        foreach(string target in profile.Targets){
            try{if(!System.IO.File.Exists(target)){failed++;continue;}Process.Start(new ProcessStartInfo(target){UseShellExecute=true});launched++;}catch{failed++;}
        }
        foreach(var board in Store.State.Boards){
            if(!profile.Layout.TryGetValue(board.Id,out var placement))continue;
            board.Placement=placement is null?null:new PanelPlacement{X=placement.X,Y=placement.Y,Width=placement.Width,Height=placement.Height};
        }
        Store.Save();owner.Arrange(false);owner.RefreshBoards();
        owner.ShowToast(profile.Name+" · "+launched+" application"+(launched==1?"":"s")+(failed>0?" · "+failed+" introuvable"+(failed==1?"":"s"):""));
    }
}
internal sealed class WorkspaceWindow:GlassWindow {
    readonly Coordinator owner;readonly StackPanel list=new();readonly TextBlock status;
    internal WorkspaceWindow(Coordinator coordinator):base("Panneaux Glass — profils d’espace"){
        owner=coordinator;Width=600;Height=Math.Min(640,SystemParameters.WorkArea.Height-80);WindowStartupLocation=WindowStartupLocation.CenterOwner;Radius=32;ShowActivated=true;
        ContentHost.Margin=new Thickness(26,22,26,18);ContentHost.RowDefinitions.Add(new(){Height=GridLength.Auto});ContentHost.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});ContentHost.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new DockPanel();var close=UI.IconButton("\uE711","Fermer",Close);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        var titles=new StackPanel();titles.Children.Add(UI.Text("Tes espaces de travail.",25,null,true));var sub=UI.Text("Ouvre d’un clic les applications d’un moment et la disposition qui va avec",11,UI.Dim);sub.TextWrapping=TextWrapping.Wrap;sub.Margin=new Thickness(0,6,0,0);titles.Children.Add(sub);header.Children.Add(titles);ContentHost.Children.Add(header);
        var scroll=new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,16,0,10)};Grid.SetRow(scroll,1);ContentHost.Children.Add(scroll);
        var footer=new DockPanel();var create=UI.Button("+  Enregistrer l’espace actuel",Create);create.Padding=new Thickness(15,9,15,9);DockPanel.SetDock(create,Dock.Right);footer.Children.Add(create);
        status=UI.Text("",11,UI.Dim);status.VerticalAlignment=VerticalAlignment.Center;status.TextTrimming=TextTrimming.CharacterEllipsis;footer.Children.Add(status);Grid.SetRow(footer,2);ContentHost.Children.Add(footer);
        Loaded+=(_,_)=>{ApplyGlass();Refresh();};KeyDown+=(_,e)=>{if(e.Key==Key.Escape)Close();};
    }
    void Refresh(){
        list.Children.Clear();var profiles=Store.State.Workspaces;
        if(profiles.Count==0){var empty=UI.Text("Aucun espace pour l’instant. Ouvre tes applications, range tes panneaux, puis enregistre l’espace actuel.",12,UI.Dim);empty.TextWrapping=TextWrapping.Wrap;empty.Margin=new Thickness(4,20,4,10);list.Children.Add(empty);return;}
        int index=0;foreach(var profile in profiles.ToList()){list.Children.Add(Row(profile,index++));}
    }
    FrameworkElement Row(WorkspaceProfile profile,int index){
        var card=new Border{CornerRadius=new CornerRadius(17),Background=UI.Brush("#0CFFFFFF"),BorderBrush=UI.Brush("#1CFFFFFF"),BorderThickness=new Thickness(1),Padding=new Thickness(12),Margin=new Thickness(0,0,0,8)};
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(38)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var glyph=UI.Glyph("\uE7C3",17);glyph.HorizontalAlignment=HorizontalAlignment.Left;grid.Children.Add(glyph);
        var text=new StackPanel{Margin=new Thickness(4,0,10,0),VerticalAlignment=VerticalAlignment.Center};var name=UI.Text(profile.Name,13,null,true);name.TextTrimming=TextTrimming.CharacterEllipsis;text.Children.Add(name);
        var detail=UI.Text(profile.Targets.Count+" application"+(profile.Targets.Count==1?"":"s"),10,UI.Dim);detail.Margin=new Thickness(0,3,0,0);text.Children.Add(detail);Grid.SetColumn(text,1);grid.Children.Add(text);
        var tools=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        var open=UI.Button("Ouvrir",()=>{WorkspaceService.Open(owner,profile);Close();});open.FontSize=12;open.Padding=new Thickness(12,7,12,7);tools.Children.Add(open);
        var update=UI.IconButton("\uE72C","Remplacer par l’espace actuel",()=>{var captured=WorkspaceService.Capture(owner,profile.Name);profile.Targets=captured.Targets;profile.Layout=captured.Layout;Store.Save();Refresh();});update.Width=30;update.Height=30;update.Margin=new Thickness(6,0,0,0);update.Background=Brushes.Transparent;update.BorderBrush=Brushes.Transparent;tools.Children.Add(update);
        var remove=UI.IconButton("\uE74D","Supprimer cet espace",()=>{if(!owner.Confirm("Supprimer l’espace « "+profile.Name+" » ?\n\nLes applications ne sont pas fermées."))return;Store.State.Workspaces.Remove(profile);Store.Save();Refresh();});remove.Width=30;remove.Height=30;remove.Margin=new Thickness(2,0,0,0);remove.Background=Brushes.Transparent;remove.BorderBrush=Brushes.Transparent;tools.Children.Add(remove);
        Grid.SetColumn(tools,2);grid.Children.Add(tools);card.Child=grid;Motion.Stagger(card,index);return card;
    }
    void Create(){
        string? name=PromptWindow.Ask(owner,"Nommer cet espace","Un nom court, comme « Dev », « Musique » ou « Matin ».","Mon espace",this);
        if(string.IsNullOrWhiteSpace(name))return;
        if(Store.State.Workspaces.Any(w=>string.Equals(w.Name,name,StringComparison.CurrentCultureIgnoreCase))){status.Text="Un espace porte déjà ce nom.";return;}
        var profile=WorkspaceService.Capture(owner,name.Trim());
        if(profile.Targets.Count==0){status.Text="Aucune application visible à enregistrer.";return;}
        Store.State.Workspaces.Insert(0,profile);Store.Save();Refresh();status.Text=profile.Targets.Count+" applications enregistrées";
    }
}
