// language: C# 12, file: HomeWindow.cs, target: Windows 11 / .NET 8 WPF
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PanneauxGlass;
// The home surface: favourites, the last things opened, and the daily sites. No search field —
// the global palette owns search now. The tile geometry follows the active theme (grid, cards, list).
internal sealed partial class HomeWindow:GlassWindow {
    readonly Coordinator owner;readonly StackPanel body=new();readonly StackPanel sites=new(){Orientation=Orientation.Horizontal};readonly TextBlock date;
    string key="";int page;
    readonly RadialGradientBrush sheen=new(){RadiusX=.5,RadiusY=1.6};double lastX=-1,lastY=-1;
    internal HomeWindow(Coordinator coordinator):base("Panneaux Glass — accueil"){
        owner=coordinator;System.Windows.Automation.AutomationProperties.SetName(this,"Panneaux Glass, accueil");Radius=38;ContentHost.Margin=new Thickness(34,28,34,27);
        foreach(var height in new[]{GridLength.Auto,new GridLength(1,GridUnitType.Star),GridLength.Auto})ContentHost.RowDefinitions.Add(new(){Height=height});
        var header=new DockPanel();var actions=new StackPanel{Orientation=Orientation.Horizontal};DockPanel.SetDock(actions,Dock.Right);header.Children.Add(actions);
        var palette=UI.Button(owner.PaletteLabel,()=>owner.TogglePalette());palette.Padding=new Thickness(11,7,11,7);palette.FontSize=12;palette.Margin=new Thickness(0,0,6,0);palette.VerticalAlignment=VerticalAlignment.Center;palette.ToolTip="Commande rapide · "+owner.PaletteLabel;System.Windows.Automation.AutomationProperties.SetName(palette,"Ouvrir la commande rapide");actions.Children.Add(palette);
        var atelier=UI.Button("Atelier",()=>owner.OpenTools());atelier.Padding=new Thickness(11,7,11,7);atelier.FontSize=12;atelier.Margin=new Thickness(0,0,6,0);atelier.VerticalAlignment=VerticalAlignment.Center;atelier.ToolTip="Capture, pipette, presse-papiers, son, espaces de travail";System.Windows.Automation.AutomationProperties.SetName(atelier,"Ouvrir l’atelier");actions.Children.Add(atelier);
        actions.Children.Add(UI.IconButton("\uE713","Personnaliser l’espace",()=>owner.Settings()));actions.Children.Add(UI.IconButton("\uE711","Replier l’accueil",owner.Collapse));
        var brand=new StackPanel{Orientation=Orientation.Horizontal};var logo=UI.Glyph("\uE8A9",28);logo.Foreground=UI.Accent;logo.Margin=new Thickness(0,0,16,0);brand.Children.Add(logo);
        var titles=new StackPanel();titles.Children.Add(UI.Text("Ton espace, dj",29,null,true));date=UI.Text("",12,UI.Dim);date.Margin=new Thickness(0,5,0,0);titles.Children.Add(date);brand.Children.Add(titles);header.Children.Add(brand);ContentHost.Children.Add(header);
        var scroll=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,9,0),Margin=new Thickness(0,22,0,0)};Grid.SetRow(scroll,1);ContentHost.Children.Add(scroll);
        var footer=new DockPanel{Margin=new Thickness(0,23,0,0)};var all=UI.Button("Tout mon espace   ›",()=>owner.ShowWorkspace());all.Padding=new Thickness(22,13,22,13);all.Background=UI.Alpha(UI.Accent.Color,46);System.Windows.Automation.AutomationProperties.SetName(all,"Ouvrir les quatre panneaux");DockPanel.SetDock(all,Dock.Right);footer.Children.Add(all);footer.Children.Add(sites);Grid.SetRow(footer,2);ContentHost.Children.Add(footer);
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape)owner.Collapse();};
        var highlight=new Border{Background=sheen,IsHitTestVisible=false,Opacity=.55};Panel.SetZIndex(highlight,99);Surface.Children.Add(highlight);
        sheen.GradientStops.Add(new GradientStop(Color.FromArgb(30,255,255,255),0));sheen.GradientStops.Add(new GradientStop(Color.FromArgb(12,190,238,255),.5));sheen.GradientStops.Add(new GradientStop(Colors.Transparent,1));
        sheen.Center=sheen.GradientOrigin=new Point(.3,.2);
        PreviewMouseMove+=(_,e)=>{var point=e.GetPosition(this);if(ActualWidth<1||ActualHeight<1)return;double x=Math.Clamp(point.X/ActualWidth,0,1),y=Math.Clamp(point.Y/ActualHeight,0,1);if(Math.Abs(x-lastX)<.02&&Math.Abs(y-lastY)<.02)return;lastX=x;lastY=y;var origin=new Point(x,y);sheen.Center=origin;sheen.GradientOrigin=origin;};
        MouseLeave+=(_,_)=>{lastX=lastY=-1;var origin=new Point(.3,.2);sheen.Center=origin;sheen.GradientOrigin=origin;};
        highlight.Visibility=Visibility.Collapsed;InitializeShell(header,scroll);
    }
    internal void Reset(){page=0;}
    internal void FocusSearch()=>owner.ShowPalette();
    internal void Refresh(){
        ShellRefresh();
        string mode=Theme.Current.Tiles;
        date.Text=Store.State.Favorites.Count+" favoris · "+Store.Entries.Count+" accès · "+Store.State.Websites.Count+" sites";
        string signature=Theme.Current.Id+"/"+mode+"/"+Store.State.IconSize+"/"+page+"/"+Store.State.HomeScope+"/"+string.Join("|",Store.Entries.Select(e=>e.Id+e.Name+e.IconPath+Store.BoardFor(e)))+string.Join("|",Store.State.Favorites)+string.Join("|",Store.State.Hidden)+string.Join("|",Store.State.Recent)+string.Join("|",Store.State.Websites.Select(s=>s.Id+s.Name+s.IconPath))+string.Join("|",Store.State.Boards.Select(b=>b.Title+b.Accent));
        if(key==signature)return;key=signature;body.Children.Clear();sites.Children.Clear();BuildDashboard();
        var entries=Store.HomeFavorites(Store.State.HomeScope).ToList();
        int pageSize=mode switch{"cards"=>8,"list"=>10,"bento"=>6,"index"=>9,"stack"=>8,_=>14};
        int pages=Math.Max(1,(entries.Count+pageSize-1)/pageSize);page=Math.Clamp(page,0,pages-1);
        var titleRow=new DockPanel();var count=UI.Text(entries.Count+" favori"+(entries.Count==1?"":"s"),12,UI.Dim);count.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(count,Dock.Right);titleRow.Children.Add(count);titleRow.Children.Add(UI.Text("Tes favoris",20,null,true));body.Children.Add(titleRow);
        var scopes=new WrapPanel{Margin=new Thickness(0,15,0,14)};
        foreach(string id in new[]{"Tous","apps","games","projects","documents"}){
            string label=id=="Tous"?"Tout":Store.State.Boards.First(b=>b.Id==id).Title;
            var chip=UI.Button(label,()=>{Store.State.HomeScope=id;page=0;Store.Save();Refresh();});
            chip.Padding=new Thickness(14,7,14,7);chip.FontSize=12;chip.Margin=new Thickness(0,0,8,5);
            chip.Background=Store.State.HomeScope==id?UI.Alpha(UI.Accent.Color,58):UI.Alpha(UI.White.Color,15);
            scopes.Children.Add(chip);
        }
        body.Children.Add(scopes);
        var favorites=new UniformGrid{MinHeight=mode switch{"cards"=>276,"list"=>238,"bento"=>266,"index"=>300,"stack"=>250,_=>116}};
        if(mode=="cards")favorites.Columns=4;else if(mode=="list")favorites.Columns=2;
        else if(mode=="bento")favorites.Columns=3;
        else if(mode=="index"||mode=="stack")favorites.Columns=1;
        else{favorites.Columns=7;favorites.Rows=entries.Count>7?2:1;}
        foreach(var entry in entries.Skip(page*pageSize).Take(pageSize))favorites.Children.Add(Tile(entry,mode));
        body.Children.Add(favorites);
        if(entries.Count==0){
            var empty=new StackPanel{Margin=new Thickness(20,8,20,18),HorizontalAlignment=HorizontalAlignment.Center};
            empty.Children.Add(UI.Text("Tes favoris commencent ici",16,null,true));
            var explain=UI.Text("Dans les panneaux, clic droit sur un accès → Ajouter aux favoris.",13,UI.Dim);explain.Margin=new Thickness(0,9,0,13);empty.Children.Add(explain);
            empty.Children.Add(UI.Button("Explorer mes accès   ›",()=>owner.ShowWorkspace(Store.State.HomeScope=="Tous"?null:Store.State.HomeScope)));
            body.Children.Remove(favorites);body.Children.Add(empty);
        }
        if(pages>1){
            var navigation=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,4,0,0)};
            var prev=UI.IconButton("\uE76B","Favoris précédents",()=>{page--;Refresh();});prev.IsEnabled=page>0;navigation.Children.Add(prev);
            var number=UI.Text((page+1)+" / "+pages,12,UI.Dim);number.VerticalAlignment=VerticalAlignment.Center;number.Margin=new Thickness(10,0,10,0);navigation.Children.Add(number);
            var next=UI.IconButton("\uE76C","Favoris suivants",()=>{page++;Refresh();});next.IsEnabled=page<pages-1;navigation.Children.Add(next);body.Children.Add(navigation);
        }
        var recent=new StackPanel{Margin=new Thickness(0,25,0,0)};
        var recentHead=new DockPanel();var recentCount=UI.Text("",12,UI.Dim);recentCount.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(recentCount,Dock.Right);recentHead.Children.Add(recentCount);recentHead.Children.Add(UI.Text("Reprendre",19,null,true));recent.Children.Add(recentHead);
        var sub=UI.Text("Tes dernières ouvertures depuis Panneaux, la plus récente en premier",12,UI.Dim);sub.Margin=new Thickness(0,5,0,14);recent.Children.Add(sub);
        var history=Store.RecentEntries(Store.State.HomeScope).Take(10).ToList();recentCount.Text=history.Count==0?"":history.Count+" récent"+(history.Count==1?"":"s");
        if(history.Count>0){var grid=new UniformGrid{Columns=2};foreach(var entry in history)grid.Children.Add(Recent(entry));recent.Children.Add(grid);}
        else{var empty=UI.Text("Tes prochains accès ouverts apparaîtront ici.",13,UI.Dim);empty.TextWrapping=TextWrapping.Wrap;empty.Margin=new Thickness(0,10,0,0);recent.Children.Add(empty);}
        body.Children.Add(recent);
        var siteLabel=UI.Text("Tes sites",12,UI.Dim);siteLabel.VerticalAlignment=VerticalAlignment.Center;siteLabel.Margin=new Thickness(0,0,12,0);sites.Children.Add(siteLabel);
        foreach(var site in Store.State.Websites.Take(6)){
            var button=UI.Button("",()=>{try{Process.Start(new ProcessStartInfo(site.Url){UseShellExecute=true});owner.CollapseImmediately();}catch(Exception ex){owner.Message(ex.Message);}});
            button.Width=42;button.Height=42;button.Padding=new Thickness(8);button.Margin=new Thickness(0,0,6,0);if(!site.ShowGlass){button.Background=Brushes.Transparent;button.BorderBrush=Brushes.Transparent;}button.ToolTip=site.Name;System.Windows.Automation.AutomationProperties.SetName(button,"Site "+site.Name);
            var icon=new Image{Width=26,Height=26,Stretch=Stretch.Uniform};owner.Icons.Load(new Entry{Id="web:"+site.Id,IconPath=site.IconPath,Target=site.IconPath},source=>UI.SetIcon(icon,source));button.Content=icon;
            button.MouseRightButtonUp+=(_,e)=>{owner.AddWebsite(site);e.Handled=true;};sites.Children.Add(button);
        }
        sites.Children.Add(UI.IconButton("\uE710","Ajouter un site internet",()=>owner.AddWebsite()));
    }
    internal static string BoardGlyph(string id)=>id switch{"apps"=>"\uE71D","games"=>"\uE7FC","projects"=>"\uE943",_=>"\uE8B7"};
    string Category(Entry entry)=>Store.State.Boards.First(b=>b.Id==Store.BoardFor(entry)).Title;
    Button Tile(Entry entry,string mode){
        var button=UI.Button("",()=>owner.Launch(entry));
        System.Windows.Automation.AutomationProperties.SetName(button,entry.Name);
        button.ToolTip=entry.Name+"\n"+Category(entry);
        button.MouseRightButtonUp+=(_,e)=>{EntryMenu(entry,button);e.Handled=true;};
        if(mode=="cards"){
            button.Background=UI.Alpha(UI.White.Color,16);button.BorderBrush=UI.Alpha(UI.White.Color,34);button.Padding=new Thickness(15,14,15,13);button.Margin=new Thickness(4,4,4,4);
            button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.VerticalContentAlignment=VerticalAlignment.Top;button.MinHeight=124;
            var card=new StackPanel();var icon=new Image{Width=Math.Clamp(Store.State.IconSize,34,48),Height=Math.Clamp(Store.State.IconSize,34,48),Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left};owner.Icons.Load(entry,source=>UI.SetIcon(icon,source));RenderOptions.SetBitmapScalingMode(icon,BitmapScalingMode.HighQuality);card.Children.Add(icon);
            var name=UI.Text(entry.Name,13,null,true);name.TextWrapping=TextWrapping.Wrap;name.TextTrimming=TextTrimming.CharacterEllipsis;name.MaxHeight=36;name.Margin=new Thickness(0,12,0,0);card.Children.Add(name);
            var category=UI.Text(string.IsNullOrWhiteSpace(entry.Category)?Category(entry):entry.Category,11,UI.Dim);category.Margin=new Thickness(0,4,0,0);category.TextTrimming=TextTrimming.CharacterEllipsis;card.Children.Add(category);button.Content=card;return button;
        }
        if(mode=="list"){
            button.Background=UI.Alpha(UI.White.Color,10);button.BorderBrush=UI.Alpha(UI.White.Color,22);button.Padding=new Thickness(12,8,12,8);button.Margin=new Thickness(4,3,4,3);
            button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.VerticalContentAlignment=VerticalAlignment.Center;
            var row=new DockPanel();var icon=new Image{Width=26,Height=26,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,12,0)};owner.Icons.Load(entry,source=>UI.SetIcon(icon,source));row.Children.Add(icon);
            var text=new StackPanel();var name=UI.Text(entry.Name,13,null,true);name.TextTrimming=TextTrimming.CharacterEllipsis;text.Children.Add(name);text.Children.Add(UI.Text(Category(entry),10,UI.Dim));row.Children.Add(text);button.Content=row;return button;
        }
        if(mode=="bento"){
            button.Background=UI.Alpha(UI.White.Color,13);button.BorderBrush=UI.Alpha(UI.White.Color,26);button.Padding=new Thickness(16,14,15,13);button.Margin=new Thickness(5);button.MinHeight=116;
            button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.VerticalContentAlignment=VerticalAlignment.Stretch;
            var cell=new Grid();cell.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});cell.RowDefinitions.Add(new(){Height=GridLength.Auto});
            var picture=new Image{Width=Math.Clamp(Store.State.IconSize*.72,34,50),Height=Math.Clamp(Store.State.IconSize*.72,34,50),Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};owner.Icons.Load(entry,source=>UI.SetIcon(picture,source));cell.Children.Add(picture);
            var foot=new StackPanel{VerticalAlignment=VerticalAlignment.Bottom};Grid.SetRow(foot,1);
            var name=UI.Text(entry.Name,13,null,true);name.TextWrapping=TextWrapping.Wrap;name.MaxHeight=34;name.TextTrimming=TextTrimming.CharacterEllipsis;foot.Children.Add(name);
            var category=UI.Text(Category(entry).ToUpperInvariant(),9,UI.Dim,true);category.Margin=new Thickness(0,4,0,0);foot.Children.Add(category);cell.Children.Add(foot);button.Content=cell;return button;
        }
        if(mode=="index"){
            button.Background=Brushes.Transparent;button.BorderBrush=UI.Alpha(UI.White.Color,46);button.BorderThickness=new Thickness(0,0,0,1);button.Padding=new Thickness(2,10,8,10);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.VerticalContentAlignment=VerticalAlignment.Center;
            var row=new DockPanel();var category=UI.Text(Category(entry).ToUpperInvariant(),9,UI.Dim);category.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(category,Dock.Right);row.Children.Add(category);
            var picture=new Image{Width=24,Height=24,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,14,0)};owner.Icons.Load(entry,source=>UI.SetIcon(picture,source));DockPanel.SetDock(picture,Dock.Left);row.Children.Add(picture);
            var name=UI.Text(entry.Name,15,null,true);name.TextTrimming=TextTrimming.CharacterEllipsis;name.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(name);button.Content=row;return button;
        }
        if(mode=="stack"){
            button.Background=Brushes.Transparent;button.BorderBrush=UI.Alpha(UI.White.Color,32);button.BorderThickness=new Thickness(0,0,0,1);button.Padding=new Thickness(10,11,12,11);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.VerticalContentAlignment=VerticalAlignment.Center;
            var row=new DockPanel();var chevron=UI.Glyph("\uE76C",12);chevron.Foreground=UI.Dim;DockPanel.SetDock(chevron,Dock.Right);row.Children.Add(chevron);
            var picture=new Image{Width=34,Height=34,Stretch=Stretch.Uniform,Margin=new Thickness(4,0,16,0),VerticalAlignment=VerticalAlignment.Center};owner.Icons.Load(entry,source=>UI.SetIcon(picture,source));DockPanel.SetDock(picture,Dock.Left);row.Children.Add(picture);
            var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center};var name=UI.Text(entry.Name,15,null,true);name.TextTrimming=TextTrimming.CharacterEllipsis;text.Children.Add(name);var category=UI.Text(Category(entry),11,UI.Dim);category.Margin=new Thickness(0,3,0,0);text.Children.Add(category);row.Children.Add(text);button.Content=row;return button;
        }
        button.Background=Brushes.Transparent;button.BorderBrush=Brushes.Transparent;button.Padding=new Thickness(6,12,6,10);button.Margin=new Thickness(3);
        var stack=new StackPanel();var image=new Image{Width=Math.Clamp(Store.State.IconSize,40,64),Height=Math.Clamp(Store.State.IconSize,40,64),Stretch=Stretch.Uniform};owner.Icons.Load(entry,source=>UI.SetIcon(image,source));stack.Children.Add(image);
        var label=UI.Text(entry.Name,12);label.TextAlignment=TextAlignment.Center;label.TextWrapping=TextWrapping.Wrap;label.TextTrimming=TextTrimming.CharacterEllipsis;label.MaxHeight=34;label.Margin=new Thickness(0,10,0,0);stack.Children.Add(label);button.Content=stack;return button;
    }
    Button Recent(Entry entry){
        var b=UI.Button("",()=>owner.Launch(entry));System.Windows.Automation.AutomationProperties.SetName(b,"Reprendre "+entry.Name);
        b.HorizontalContentAlignment=HorizontalAlignment.Stretch;b.Padding=new Thickness(12,10,12,10);b.Margin=new Thickness(0,0,8,7);
        var row=new DockPanel();var icon=new Image{Width=32,Height=32,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,12,0)};owner.Icons.Load(entry,source=>UI.SetIcon(icon,source));row.Children.Add(icon);
        var text=new StackPanel();var name=UI.Text(entry.Name,13,null,true);name.TextTrimming=TextTrimming.CharacterEllipsis;text.Children.Add(name);text.Children.Add(UI.Text(Category(entry),11,UI.Dim));row.Children.Add(text);b.Content=row;b.ToolTip=entry.Target;
        b.MouseRightButtonUp+=(_,e)=>{EntryMenu(entry,b);e.Handled=true;};return b;
    }
    void EntryMenu(Entry entry,Button anchor)=>GlassMenu.Open(owner,this,anchor,entry.Name,new[]{new MenuAction("Ouvrir","\uE8A7",()=>owner.Launch(entry)),new MenuAction(Store.State.Favorites.Contains(entry.Id)?"Retirer des favoris":"Ajouter aux favoris","\uE734",()=>{if(!Store.State.Favorites.Remove(entry.Id))Store.State.Favorites.Add(entry.Id);Store.Save();owner.RefreshBoards();}),new MenuAction(Store.State.TaskbarPins.Contains(entry.Id)?"Détacher de la barre":"Épingler à la barre","\uE718",()=>owner.ToggleTaskbarPin(entry)),new MenuAction("Personnaliser cet accès…","\uE70F",()=>owner.Edit(entry),Separator:true),new MenuAction("Afficher l’emplacement","\uE8B7",()=>owner.Reveal(entry))},true);
}
