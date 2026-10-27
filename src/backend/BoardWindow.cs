// language: C# 12, file: BoardWindow.cs, target: Windows 11 / .NET 8 WPF, four configurable icon carousels
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Controls.Primitives;

namespace PanneauxGlass;
internal sealed class BoardWindow:GlassWindow {
    readonly Coordinator owner;internal BoardState Model;readonly TextBlock heading,caption,pageText;readonly Button selector,previous,next,star;
    readonly StackPanel filters=new(){Orientation=Orientation.Horizontal};readonly Grid itemsHost=new();readonly Border emblem=new();
    List<Entry> filtered=new();int page,capacity=24,columns=6,rows=3;string quick="",renderKey="",quickKey="~";bool rendering;Point dragStart;DateTime blockClick;
    internal BoardWindow(Coordinator coordinator,BoardState model):base("Panneaux Glass — "+model.Title){
        owner=coordinator;Model=model;ContentHost.Margin=new Thickness(28,23,28,19);
        ContentHost.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});ContentHost.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});ContentHost.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});ContentHost.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
        var header=new Grid();header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});header.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});header.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var sectionGlyph=UI.Glyph(model.Id switch{"apps"=>"\uE71D","games"=>"\uE7FC","projects"=>"\uE943",_=>"\uE8B7"},22);
        emblem.Child=sectionGlyph;emblem.Width=48;emblem.Height=48;emblem.CornerRadius=new CornerRadius(17);emblem.Background=UI.Brush("#17FFFFFF");emblem.BorderBrush=UI.Brush("#28FFFFFF");emblem.BorderThickness=new Thickness(1);emblem.Margin=new Thickness(0,0,15,0);header.Children.Add(emblem);
        var titleHit=new Grid{Background=Brushes.Transparent};var titles=new StackPanel{VerticalAlignment=VerticalAlignment.Center,IsHitTestVisible=false};heading=UI.Text(model.Title,25,null,true);caption=UI.Text("",12,UI.Dim);caption.Margin=new Thickness(0,5,0,0);titles.Children.Add(heading);titles.Children.Add(caption);titleHit.Children.Add(titles);Grid.SetColumn(titleHit,1);header.Children.Add(titleHit);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Top};star=UI.IconButton("\uE734","Favoris · "+model.Title,()=>{Model.FavoritesOnly=!Model.FavoritesOnly;page=0;Store.Save();Refresh();});star.Margin=new Thickness(0,0,7,0);actions.Children.Add(star);var add=UI.IconButton("\uE710","Ajouter un accès à "+model.Title,()=>owner.AddAccess(model.Id));add.Margin=new Thickness(0,0,7,0);actions.Children.Add(add);actions.Children.Add(UI.IconButton("\uE713","Personnaliser "+model.Title,()=>owner.Settings(model.Id)));Grid.SetColumn(actions,2);header.Children.Add(actions);ContentHost.Children.Add(header);
        var filterRow=new DockPanel{LastChildFill=false,Margin=new Thickness(0,23,0,17)};Grid.SetRow(filterRow,1);ContentHost.Children.Add(filterRow);
        selector=UI.Button("Toutes les catégories  ⌄",OpenCategories);selector.Padding=new Thickness(12,7,12,7);selector.FontSize=12;selector.Margin=new Thickness(0,0,9,0);filterRow.Children.Add(selector);filterRow.Children.Add(filters);
        Grid.SetRow(itemsHost,2);ContentHost.Children.Add(itemsHost);itemsHost.ClipToBounds=true;
        var footer=new Grid{Margin=new Thickness(0,12,0,0)};footer.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});footer.ColumnDefinitions.Add(new(){Width=GridLength.Auto});Grid.SetRow(footer,3);ContentHost.Children.Add(footer);
        var note=UI.Text("Glisser le titre · Étirer les bords",11,UI.Brush("#91B9C9DB"));note.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(note);
        var pages=new StackPanel{Orientation=Orientation.Horizontal};Grid.SetColumn(pages,1);footer.Children.Add(pages);previous=UI.IconButton("\uE76B","Page précédente",()=>Turn(-1));previous.Width=30;previous.Height=28;pages.Children.Add(previous);pageText=UI.Text("1 / 1",11,UI.Dim);pageText.VerticalAlignment=VerticalAlignment.Center;pageText.Margin=new Thickness(11,0,11,0);pages.Children.Add(pageText);next=UI.IconButton("\uE76C","Page suivante",()=>Turn(1));next.Width=30;next.Height=28;pages.Children.Add(next);
        itemsHost.SizeChanged+=(_,_)=>{if(!rendering)RenderItems(false,1);};new PanelInteraction(this,owner,titleHit);PreviewMouseWheel+=(_,e)=>{if(e.Delta!=0){Turn(e.Delta<0?1:-1);e.Handled=true;}};
        AllowDrop=true;DragOver+=(_,e)=>{e.Effects=e.Data.GetDataPresent("PanneauxEntry")?DragDropEffects.Move:e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Link:DragDropEffects.None;e.Handled=true;};Drop+=(_,e)=>HandleDrop(e,null);
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape)owner.Collapse();if(e.Key==Key.PageDown)Turn(1);if(e.Key==Key.PageUp)Turn(-1);};
        IsVisibleChanged+=(_,_)=>{if(!IsVisible){owner.Icons.CancelScope(Model.Id);renderKey="";}};
    }
    bool MatchQuick(Entry entry){string text=Store.Fold(entry.Name+" "+entry.Category);return quick switch{"IA"=>entry.Category=="IA et assistants de code"||entry.Category=="Intelligence artificielle","Dev"=>text.Contains("developpement")||text.Contains("python")||text.Contains("compilation"),"Création"=>text.Contains("3d")||text.Contains("conception")||text.Contains("graphisme")||text.Contains("video"),"Canada"=>Store.Fold(entry.Target).Contains("canada"),"Médias"=>entry.Section=="Médias","Cheats"=>text.Contains("cheat")||text.Contains("my ch"),"Favoris"=>Store.State.Favorites.Contains(entry.Id),_=>true};}
    internal void Refresh(bool animate=true,int direction=1){if(rendering)return;rendering=true;try{
        heading.Text=Model.Title;emblem.BorderBrush=UI.Brush(Model.Accent);if(emblem.Child is TextBlock glyph)glyph.Foreground=UI.Brush(Model.Accent);
        var selection=Store.Entries.Where(e=>!Store.State.Hidden.Contains(e.Id)&&Store.BoardFor(e)==Model.Id);
        star.Background=Model.FavoritesOnly?UI.Brush("#45C5E8FF"):UI.Brush("#0FFFFFFF");star.Content=UI.Glyph(Model.FavoritesOnly?"\uE735":"\uE734");
        if(owner.FavoritesOnly||Model.FavoritesOnly)selection=selection.Where(e=>Store.State.Favorites.Contains(e.Id));
        var all=selection.ToList();string filter=Model.Filter;
        if(!Model.FavoritesOnly&&filter=="Sélection"&&Model.Id=="apps"){string[] preferred={"google chrome","firefox","brave","microsoft edge","chatgpt","opencode","antigravity","hermes","freebuff","cursor","visual studio code","blender 5.2","solidworks 2025","obs studio","recordly","discord","telegram","spotify","docker desktop","word","excel","proton mail","7-zip file manager","winrar"};selection=selection.Where(e=>preferred.Contains(Store.Fold(e.Name))||Store.State.Favorites.Contains(e.Id));}
        else if(!Model.FavoritesOnly&&filter!="Tous"&&filter!="Sélection")selection=selection.Where(e=>e.Category==filter);
        if(!Model.FavoritesOnly)selection=selection.Where(MatchQuick);filtered=Store.Sorted(selection).ToList();
        caption.Text=Model.Id switch{"apps"=>all.Count+" logiciels · tes outils au même endroit","games"=>all.Count+" accès · trouve ton prochain jeu","projects"=>all.Count+" espaces · reprends là où tu en étais",_=>all.Count+" dossiers · le quotidien à portée de main"};
        selector.Content=Model.FavoritesOnly?"Favoris · "+Model.Title+"  ⌄":(filter=="Tous"?"Toutes les catégories":filter)+"  ⌄";
        BuildQuick();RenderItems(animate,direction);
    }finally{rendering=false;}}
    void BuildQuick(){if(quickKey==quick)return;quickKey=quick;filters.Children.Clear();string[] options=Model.Id switch{"apps"=>new[]{"IA","Dev","Création"},"projects"=>new[]{"Cheats","IA"},"documents"=>new[]{"Canada","Médias"},_=>Array.Empty<string>()};foreach(string text in options){var b=UI.Button(text,()=>{Model.FavoritesOnly=false;quick=quick==text?"":text;page=0;Refresh();});b.Padding=new Thickness(12,7,12,7);b.FontSize=12;b.Margin=new Thickness(0,0,7,0);b.Background=quick==text?UI.Brush("#45C5E8FF"):UI.Brush("#09FFFFFF");filters.Children.Add(b);}}
    void OpenCategories(){var actions=new List<MenuAction>{new("Toutes les catégories","\uE8A9",()=>SetFilter("Tous"),Model.Filter=="Tous")};if(Model.Id=="apps")actions.Add(new("Sélection du quotidien","\uE734",()=>SetFilter("Sélection"),Model.Filter=="Sélection"));foreach(var cat in Store.Entries.Where(e=>Store.BoardFor(e)==Model.Id&&!Store.State.Hidden.Contains(e.Id)).Select(e=>e.Category).Distinct().OrderBy(x=>x)){string local=cat;actions.Add(new(cat,GlassMenu.CategoryGlyph(cat),()=>SetFilter(local),Model.Filter==cat,actions.Count==1));}GlassMenu.Open(owner,this,selector,"Catégories · "+Model.Title,actions);}
    void SetFilter(string filter){Model.FavoritesOnly=false;Model.Filter=filter;quick="";page=0;Store.Save();Refresh();}
    void Turn(int by){int pages=Math.Max(1,(int)Math.Ceiling(filtered.Count/(double)capacity));int target=Math.Clamp(page+by,0,pages-1);if(target==page)return;page=target;RenderItems(true,by);}
    void RenderItems(bool animate,int direction){
        double width=itemsHost.ActualWidth>20?itemsHost.ActualWidth:Math.Max(400,Width-56),height=itemsHost.ActualHeight>20?itemsHost.ActualHeight:Math.Max(180,Height-190);double icon=Store.State.IconSize;string mode=Theme.Current.Tiles;
        if(mode=="list"){columns=1;rows=Math.Max(1,(int)Math.Floor(height/58));}
        else if(mode=="cards"){columns=Math.Clamp((int)Math.Floor(width/250),2,5);rows=Math.Max(1,(int)Math.Floor(height/(icon+104)));}
        else if(mode=="bento"){columns=3;rows=Math.Max(1,(int)Math.Floor(height/146));}
        else if(mode=="index"){columns=1;rows=Math.Max(1,(int)Math.Floor(height/50));}
        else if(mode=="stack"){columns=1;rows=Math.Max(1,(int)Math.Floor(height/76));}
        else{columns=Math.Max(2,(int)Math.Floor(width/(icon+76)));rows=Math.Max(1,(int)Math.Floor(height/(icon+61)));}
        capacity=columns*rows;int pages=Math.Max(1,(int)Math.Ceiling(filtered.Count/(double)capacity));page=Math.Clamp(page,0,pages-1);
        string nextKey=Theme.Current.Id+"/"+filtered.Count+"/"+mode+"/"+columns+"/"+rows+"/"+page+"/"+icon+"/"+Model.Accent+"/"+string.Join("|",filtered.Skip(page*capacity).Take(capacity).Select(e=>e.Id+e.Name+e.IconPath+Store.State.Favorites.Contains(e.Id)));if(renderKey==nextKey)return;renderKey=nextKey;var grid=new UniformGrid{Columns=columns,Rows=rows,VerticalAlignment=VerticalAlignment.Stretch};itemsHost.Children.Clear();itemsHost.Children.Add(grid);
        if(filtered.Count==0){var empty=new StackPanel{VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};var g=UI.Glyph("\uE721",29);g.Opacity=.6;empty.Children.Add(g);var label=UI.Text("Aucun accès dans cette vue",15,UI.Dim);label.Margin=new Thickness(0,16,0,0);empty.Children.Add(label);itemsHost.Children.Add(empty);        }else{int ordinal=page*capacity;foreach(var entry in filtered.Skip(page*capacity).Take(capacity)){ordinal++;grid.Children.Add(CreateTile(entry,icon,ordinal));}}
        pageText.Text=(page+1)+" / "+pages;previous.IsEnabled=page>0;next.IsEnabled=page<pages-1;if(animate)UI.Animate(grid,direction);
    }
    Button CreateTile(Entry entry,double icon,int number=0){
        string mode=Theme.Current.Tiles;
        var button=new Button{Background=Brushes.Transparent,BorderBrush=Brushes.Transparent,BorderThickness=new Thickness(1),Padding=new Thickness(6),Margin=new Thickness(4,3,4,3),HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch,ToolTip=entry.Name+"\n"+entry.Category+"\n"+entry.Target};
        System.Windows.Automation.AutomationProperties.SetName(button,entry.Name);
        FrameworkElement content;
        if(mode=="list"){
            button.Background=UI.Alpha(UI.White.Color,10);button.BorderBrush=UI.Alpha(UI.White.Color,20);button.Padding=new Thickness(12,7,12,7);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.VerticalContentAlignment=VerticalAlignment.Center;
            var row=new DockPanel();var picture=new Image{Width=28,Height=28,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,13,0)};RenderOptions.SetBitmapScalingMode(picture,BitmapScalingMode.HighQuality);owner.Icons.Load(entry,source=>picture.Source=source,Model.Id);row.Children.Add(picture);
            var text=new StackPanel();var label=UI.Text(entry.Name,13,null,true);label.TextTrimming=TextTrimming.CharacterEllipsis;text.Children.Add(label);var category=UI.Text(entry.Category,10,UI.Dim);category.TextTrimming=TextTrimming.CharacterEllipsis;text.Children.Add(category);row.Children.Add(text);content=row;
        }else if(mode=="bento"){
            // Bento: an upright compartment, colour block behind the logo, name at the foot.
            button.Background=number%2==0?UI.Alpha(UI.White.Color,13):UI.Alpha(UI.Accent.Color,23);button.BorderBrush=UI.Alpha(UI.White.Color,26);button.BorderThickness=new Thickness(1);button.Padding=new Thickness(17,15,16,14);button.Margin=new Thickness(5);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.VerticalContentAlignment=VerticalAlignment.Stretch;button.MinHeight=118;
            var cell=new Grid();cell.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});cell.RowDefinitions.Add(new(){Height=GridLength.Auto});
            var picture=new Image{Width=Math.Clamp(icon*.72,34,50),Height=Math.Clamp(icon*.72,34,50),Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top};RenderOptions.SetBitmapScalingMode(picture,BitmapScalingMode.HighQuality);owner.Icons.Load(entry,source=>picture.Source=source,Model.Id);cell.Children.Add(picture);
            var foot=new StackPanel{VerticalAlignment=VerticalAlignment.Bottom};Grid.SetRow(foot,1);
            var name=UI.Text(entry.Name,13,null,true);name.TextWrapping=TextWrapping.Wrap;name.TextTrimming=TextTrimming.CharacterEllipsis;name.MaxHeight=34;foot.Children.Add(name);
            var category=UI.Text(entry.Category.ToUpperInvariant(),9,UI.Dim,true);category.TextTrimming=TextTrimming.CharacterEllipsis;category.Margin=new Thickness(0,4,0,0);foot.Children.Add(category);cell.Children.Add(foot);
            content=cell;
        }else if(mode=="index"){
            // Suisse: a printed index — number, logo, name, category, one hairline under each row.
            button.Background=Brushes.Transparent;button.BorderBrush=UI.Alpha(UI.White.Color,48);button.BorderThickness=new Thickness(0,0,0,1);button.Padding=new Thickness(2,9,8,9);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.VerticalContentAlignment=VerticalAlignment.Center;
            var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(42)});row.ColumnDefinitions.Add(new(){Width=new GridLength(32)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var ordinal=UI.Text(number.ToString("00"),11,UI.Dim);ordinal.FontFamily=UI.Mono;ordinal.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(ordinal);
            var picture=new Image{Width=22,Height=22,Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center};RenderOptions.SetBitmapScalingMode(picture,BitmapScalingMode.HighQuality);owner.Icons.Load(entry,source=>picture.Source=source,Model.Id);Grid.SetColumn(picture,1);row.Children.Add(picture);
            var label=UI.Text(entry.Name,14,null,true);label.TextTrimming=TextTrimming.CharacterEllipsis;label.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(label,2);row.Children.Add(label);
            var category=UI.Text(entry.Category.ToUpperInvariant(),9,UI.Dim);category.VerticalAlignment=VerticalAlignment.Center;category.Margin=new Thickness(14,0,0,0);Grid.SetColumn(category,3);row.Children.Add(category);content=row;
        }else if(mode=="stack"){
            // Zen: a wide, quiet row with room around the logo and a chevron at the end.
            button.Background=Brushes.Transparent;button.BorderBrush=UI.Alpha(UI.White.Color,34);button.BorderThickness=new Thickness(0,0,0,1);button.Padding=new Thickness(10,11,12,11);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.VerticalContentAlignment=VerticalAlignment.Center;
            var row=new DockPanel();var chevron=UI.Glyph("\uE76C",12);chevron.Foreground=UI.Dim;chevron.Opacity=.8;DockPanel.SetDock(chevron,Dock.Right);row.Children.Add(chevron);
            var picture=new Image{Width=34,Height=34,Stretch=Stretch.Uniform,Margin=new Thickness(4,0,16,0),VerticalAlignment=VerticalAlignment.Center};RenderOptions.SetBitmapScalingMode(picture,BitmapScalingMode.HighQuality);owner.Icons.Load(entry,source=>picture.Source=source,Model.Id);DockPanel.SetDock(picture,Dock.Left);row.Children.Add(picture);
            var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center};var label=UI.Text(entry.Name,15,null,true);label.TextTrimming=TextTrimming.CharacterEllipsis;text.Children.Add(label);var category=UI.Text(entry.Category,11,UI.Dim);category.TextTrimming=TextTrimming.CharacterEllipsis;category.Margin=new Thickness(0,3,0,0);text.Children.Add(category);row.Children.Add(text);content=row;
        }else if(mode=="cards"){
            button.Background=UI.Alpha(UI.White.Color,15);button.BorderBrush=UI.Alpha(UI.White.Color,30);button.BorderThickness=new Thickness(Theme.Current.BorderWeight);button.Padding=new Thickness(14,13,14,12);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.VerticalContentAlignment=VerticalAlignment.Top;
            var card=new StackPanel();var picture=new Image{Width=Math.Clamp(icon*.62,30,44),Height=Math.Clamp(icon*.62,30,44),Stretch=Stretch.Uniform,HorizontalAlignment=HorizontalAlignment.Left};RenderOptions.SetBitmapScalingMode(picture,BitmapScalingMode.HighQuality);owner.Icons.Load(entry,source=>picture.Source=source,Model.Id);card.Children.Add(picture);
            var label=UI.Text(entry.Name,13,null,true);label.TextWrapping=TextWrapping.Wrap;label.TextTrimming=TextTrimming.CharacterEllipsis;label.MaxHeight=36;label.Margin=new Thickness(0,10,0,0);card.Children.Add(label);var category=UI.Text(entry.Category,11,UI.Dim);category.TextTrimming=TextTrimming.CharacterEllipsis;category.Margin=new Thickness(0,3,0,0);card.Children.Add(category);content=card;
        }else{
            var stack=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            var picture=new Image{Width=icon,Height=icon,Stretch=Stretch.Uniform,Opacity=1};RenderOptions.SetBitmapScalingMode(picture,BitmapScalingMode.HighQuality);owner.Icons.Load(entry,source=>picture.Source=source,Model.Id);stack.Children.Add(picture);
            var label=UI.Text(entry.Name,12);label.TextAlignment=TextAlignment.Center;label.TextWrapping=TextWrapping.Wrap;label.TextTrimming=TextTrimming.CharacterEllipsis;label.MaxHeight=35;label.Margin=new Thickness(0,10,0,0);label.LineHeight=16;stack.Children.Add(label);content=stack;
        }
        var layout=new Grid();layout.Children.Add(content);
        if(Store.State.Favorites.Contains(entry.Id)){var star=UI.Text("★",12,UI.Brush(Model.Accent));star.HorizontalAlignment=HorizontalAlignment.Right;star.VerticalAlignment=VerticalAlignment.Top;star.Margin=new Thickness(0,2,5,0);layout.Children.Add(star);}button.Content=layout;
        button.Click+=(_,_)=>{if(DateTime.UtcNow>=blockClick)owner.Launch(entry);};button.MouseRightButtonUp+=(_,e)=>{OpenEntryMenu(entry,button);e.Handled=true;};
        button.PreviewMouseLeftButtonDown+=(_,e)=>dragStart=e.GetPosition(button);button.PreviewMouseMove+=(_,e)=>{if(e.LeftButton!=MouseButtonState.Pressed)return;var point=e.GetPosition(button);if((point-dragStart).Length<9)return;blockClick=DateTime.UtcNow.AddMilliseconds(450);DragDrop.DoDragDrop(button,new DataObject("PanneauxEntry",entry.Id),DragDropEffects.Move);blockClick=DateTime.UtcNow.AddMilliseconds(450);};
        button.AllowDrop=true;button.Drop+=(_,e)=>HandleDrop(e,entry.Id);return button;
    }
    void OpenEntryMenu(Entry entry,Button anchor){blockClick=DateTime.UtcNow.AddMilliseconds(350);GlassMenu.Open(owner,this,anchor,entry.Name,new[]{new MenuAction("Ouvrir","\uE8A7",()=>owner.Launch(entry)),new MenuAction(Store.State.Favorites.Contains(entry.Id)?"Retirer des favoris":"Ajouter aux favoris","\uE734",()=>{if(!Store.State.Favorites.Remove(entry.Id))Store.State.Favorites.Add(entry.Id);Store.Save();owner.RefreshBoards();}),new MenuAction(Store.State.TaskbarPins.Contains(entry.Id)?"Détacher de la barre":"Épingler à la barre","\uE718",()=>owner.ToggleTaskbarPin(entry)),new MenuAction("Personnaliser cet accès…","\uE70F",()=>owner.Edit(entry),Separator:true),new MenuAction("Afficher l’emplacement","\uE8B7",()=>owner.Reveal(entry)),new MenuAction("Copier le chemin","\uE8C8",()=>System.Windows.Clipboard.SetText(entry.Target)),new MenuAction("Masquer cet accès","\uE890",()=>{Store.State.Hidden.Add(entry.Id);Store.Save();owner.RefreshBoards();},Separator:true)},true);}
    void HandleDrop(DragEventArgs e,string? targetId){blockClick=DateTime.UtcNow.AddMilliseconds(450);if(e.Data.GetDataPresent("PanneauxEntry")){string id=(string)e.Data.GetData("PanneauxEntry");Store.State.BoardOverrides[id]=Model.Id;var destination=Store.Sorted(Store.Entries.Where(x=>Store.BoardFor(x)==Model.Id&&!Store.State.Hidden.Contains(x.Id))).Select(x=>x.Id).ToList();destination.Remove(id);int index=targetId==null?-1:destination.IndexOf(targetId);if(index<0)destination.Add(id);else destination.Insert(index,id);Store.State.Order=Store.State.Order.Where(x=>!destination.Contains(x)).Concat(destination).ToList();Store.Save();owner.RefreshBoards();}else if(e.Data.GetDataPresent(DataFormats.FileDrop)){foreach(string path in (string[])e.Data.GetData(DataFormats.FileDrop))owner.AddPath(path,Model.Id);owner.RefreshBoards();}e.Handled=true;}
}
