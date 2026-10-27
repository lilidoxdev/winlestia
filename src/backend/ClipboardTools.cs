// language: C# 12, file: ClipboardTools.cs, target: Windows 11 / .NET 8 WPF, clipboard history
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Streams;
using WinClipboard=Windows.ApplicationModel.DataTransfer.Clipboard;

namespace PanneauxGlass;
// A quiet observer: it only records what Windows already put on the clipboard, and never writes
// to it unless dj asks for an item back.
internal sealed class ClipboardService:IDisposable {
    readonly List<ClipboardItem> items=new();readonly string folder;readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(750)};
    bool busy,disposed;string last="";uint lastSequence;
    [DllImport("user32.dll")]static extern uint GetClipboardSequenceNumber();
    internal event Action? Changed;
    internal IReadOnlyList<ClipboardItem> Items=>items;
    internal ClipboardService(){
        folder=Path.Combine(Store.Root,"clipboard");try{Directory.CreateDirectory(folder);}catch(Exception ex){Store.Log("Presse-papiers: "+ex.Message);}
        Load();timer.Tick+=(_,_)=>_ = PollAsync();ApplySettings();Prune();
    }
    internal void ApplySettings(){timer.Stop();if(Store.State.ClipboardHistory)timer.Start();}
    internal void Clear(){items.Clear();Save();foreach(var file in Directory.EnumerateFiles(folder,"*.png").ToArray())try{File.Delete(file);}catch{}Changed?.Invoke();}
    internal void Remove(ClipboardItem item){items.Remove(item);if(!string.IsNullOrEmpty(item.ImagePath))try{File.Delete(item.ImagePath);}catch{}Save();Changed?.Invoke();}
    internal void TogglePin(ClipboardItem item){item.Pinned=!item.Pinned;Save();Changed?.Invoke();}
    internal void CopyBack(ClipboardItem item){
        try{
            if(item.Kind=="image"&&File.Exists(item.ImagePath)){var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.UriSource=new Uri(item.ImagePath);image.EndInit();image.Freeze();System.Windows.Clipboard.SetImage(image);}
            else System.Windows.Clipboard.SetText(item.Text);
            last=Signature(item);lastSequence=GetClipboardSequenceNumber();
        }catch(Exception ex){Store.Log("Presse-papiers copie: "+ex.Message);}
    }
    async Task PollAsync(){
        if(busy||disposed||!Store.State.ClipboardHistory)return;
        uint sequence=GetClipboardSequenceNumber();if(sequence==0||sequence==lastSequence)return;busy=true;
        try{
            var view=WinClipboard.GetContent();
            if(view.Contains(StandardDataFormats.Text)){
                string text=await view.GetTextAsync();
                if(disposed||!Store.State.ClipboardHistory)return;
                if(!string.IsNullOrWhiteSpace(text)&&text.Length<200000&&Signature(text)!=last){last=Signature(text);Add(new ClipboardItem{Kind="text",Text=text,At=DateTime.Now.ToString("HH:mm")});}
            }else if(Store.State.ClipboardImages&&view.Contains(StandardDataFormats.Bitmap)){
                var reference=await view.GetBitmapAsync();using var stream=await reference.OpenReadAsync();
                var bytes=await ReadAll(stream);if(disposed||!Store.State.ClipboardHistory)return;string path=Path.Combine(folder,Guid.NewGuid().ToString("N")+".png");await File.WriteAllBytesAsync(path,bytes);
                last="image:"+new FileInfo(path).Length;
                Add(new ClipboardItem{Kind="image",ImagePath=path,At=DateTime.Now.ToString("HH:mm")});
            }
            lastSequence=sequence;
        }catch(Exception ex){Store.Log("Presse-papiers lecture: "+ex.Message);}
        finally{busy=false;}
    }
    static async Task<byte[]> ReadAll(IRandomAccessStream stream){
        uint size=(uint)stream.Size;var reader=new DataReader(stream.GetInputStreamAt(0));await reader.LoadAsync(size);
        var bytes=new byte[size];reader.ReadBytes(bytes);reader.Dispose();return bytes;
    }
    static string Signature(string text)=>text.Length+"|"+text.GetHashCode();
    static string Signature(ClipboardItem item)=>item.Kind=="image"?"image:"+item.ImagePath:Signature(item.Text);
    void Add(ClipboardItem item){
        if(item.Kind=="image"&&!Store.State.ClipboardImages)return;
        items.Insert(0,item);Prune();Save();Changed?.Invoke();
    }
    void Prune(){
        int limit=Math.Clamp(Store.State.ClipboardLimit,8,80);
        while(items.Count>limit){var victim=items.LastOrDefault(x=>!x.Pinned);if(victim==null)break;items.Remove(victim);if(!string.IsNullOrEmpty(victim.ImagePath))try{File.Delete(victim.ImagePath);}catch{}}
    }
    void Load(){
        try{string path=Path.Combine(folder,"history.json");if(!File.Exists(path))return;var list=JsonSerializer.Deserialize<List<ClipboardItem>>(File.ReadAllText(path),Store.Json);if(list==null)return;foreach(var item in list.Take(120)){if(item.Kind=="image"&&!File.Exists(item.ImagePath))continue;items.Add(item);}}
        catch(Exception ex){Store.Log("Presse-papiers chargement: "+ex.Message);}
    }
    void Save(){
        try{File.WriteAllText(Path.Combine(folder,"history.json"),JsonSerializer.Serialize(items,Store.Json));}catch(Exception ex){Store.Log("Presse-papiers sauvegarde: "+ex.Message);}
    }
    public void Dispose(){disposed=true;timer.Stop();Save();}
}
internal sealed class ClipboardWindow:GlassWindow {
    readonly Coordinator owner;readonly ClipboardService service;readonly StackPanel list=new();readonly TextBox search;readonly TextBlock count,status;
    internal ClipboardWindow(Coordinator coordinator,ClipboardService clipboard):base("Panneaux Glass — presse-papiers"){
        owner=coordinator;service=clipboard;Width=580;Height=Math.Min(720,SystemParameters.WorkArea.Height-80);WindowStartupLocation=WindowStartupLocation.CenterOwner;Radius=34;ShowActivated=true;
        ContentHost.Margin=new Thickness(26,22,26,18);ContentHost.RowDefinitions.Add(new(){Height=GridLength.Auto});ContentHost.RowDefinitions.Add(new(){Height=GridLength.Auto});ContentHost.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});ContentHost.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new DockPanel();var close=UI.IconButton("\uE711","Fermer le presse-papiers",Close);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        var titles=new StackPanel();titles.Children.Add(UI.Text("Ton presse-papiers.",25,null,true));var sub=UI.Text("Clique un élément pour le remettre dans le presse-papiers",11,UI.Dim);sub.Margin=new Thickness(0,6,0,0);titles.Children.Add(sub);header.Children.Add(titles);ContentHost.Children.Add(header);
        var palette=UI.Button(owner.PaletteLabel,()=>owner.TogglePalette());palette.FontSize=11;palette.Padding=new Thickness(10,6,10,6);palette.Margin=new Thickness(0,14,0,10);palette.HorizontalAlignment=HorizontalAlignment.Left;palette.ToolTip="Ouvrir la commande rapide";ContentHost.Children.Add(palette);
        var searchRow=new Grid{Margin=new Thickness(0,0,0,12)};searchRow.ColumnDefinitions.Add(new(){Width=new GridLength(28)});searchRow.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});searchRow.Children.Add(UI.Glyph("\uE721",15));search=new TextBox{FontSize=13,Padding=new Thickness(10,8,10,8)};System.Windows.Automation.AutomationProperties.SetName(search,"Rechercher dans le presse-papiers");Grid.SetColumn(search,1);searchRow.Children.Add(search);Grid.SetRow(searchRow,1);ContentHost.Children.Add(searchRow);
        var scroll=new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,0,0,10)};Grid.SetRow(scroll,2);ContentHost.Children.Add(scroll);
        var footer=new DockPanel();var actions=new StackPanel{Orientation=Orientation.Horizontal};var clear=UI.Button("Vider",()=>{if(owner.Confirm("Vider l’historique du presse-papiers ?\n\nLes éléments épinglés restent.")){service.Clear();}});clear.FontSize=12;clear.Padding=new Thickness(12,7,12,7);actions.Children.Add(clear);DockPanel.SetDock(actions,Dock.Right);footer.Children.Add(actions);
        count=UI.Text("",11,UI.Dim);count.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(count);Grid.SetRow(footer,3);ContentHost.Children.Add(footer);
        status=UI.Text("Windows garde ces éléments tant que Panneaux tourne. Rien n’est envoyé sur Internet.",10,UI.Dim);status.Margin=new Thickness(0,8,0,0);status.TextWrapping=TextWrapping.Wrap;ContentHost.Children.Add(status);
        search.TextChanged+=(_,_)=>Refresh();service.Changed+=Refresh;Closed+=(_,_)=>{service.Changed-=Refresh;};
        Loaded+=(_,_)=>{ApplyGlass();Refresh();search.Focus();};
        KeyDown+=(_,e)=>{if(e.Key==Key.Escape)Close();if(e.Key==Key.F&&Keyboard.Modifiers==ModifierKeys.Control)search.Focus();};
    }
    void Refresh(){
        list.Children.Clear();
        string query=Store.Fold(search?.Text??"");
        var visible=service.Items.Where(item=>query.Length==0||Store.Fold(item.Kind=="image"?"image capture":item.Text).Contains(query)).ToList();
        count.Text=visible.Count+" élément"+(visible.Count==1?"":"s");
        if(visible.Count==0){var empty=UI.Text(query.Length==0?"Rien encore : copie du texte ou une image, elle apparaîtra ici.":"Aucun élément ne correspond.",12,UI.Dim);empty.Margin=new Thickness(6,24,6,10);empty.TextWrapping=TextWrapping.Wrap;list.Children.Add(empty);return;}
        int index=0;
        foreach(var item in visible){list.Children.Add(Row(item,index++));}
    }
    FrameworkElement Row(ClipboardItem item,int index){
        var row=new Border{CornerRadius=new CornerRadius(16),Padding=new Thickness(10),Margin=new Thickness(0,0,0,7),Background=UI.Brush("#0CFFFFFF"),BorderBrush=UI.Brush("#1CFFFFFF"),BorderThickness=new Thickness(1),Cursor=Cursors.Hand};
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(52)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        if(item.Kind=="image"&&File.Exists(item.ImagePath)){
            var image=new Image{Width=42,Height=42,Stretch=Stretch.UniformToFill};
            try{var source=new BitmapImage();source.BeginInit();source.CacheOption=BitmapCacheOption.OnLoad;source.DecodePixelWidth=84;source.UriSource=new Uri(item.ImagePath);source.EndInit();source.Freeze();image.Source=source;}catch{}
            image.Clip=new RectangleGeometry(new Rect(0,0,42,42),9,9);
            grid.Children.Add(new Border{Width=42,Height=42,CornerRadius=new CornerRadius(9),Background=UI.Brush("#18FFFFFF"),Child=image,VerticalAlignment=VerticalAlignment.Center});
        }else{
            var glyph=UI.Glyph("\uE8C8",17);glyph.HorizontalAlignment=HorizontalAlignment.Center;grid.Children.Add(new Border{Width=42,Height=42,CornerRadius=new CornerRadius(9),Background=UI.Brush("#18FFFFFF"),Child=glyph,VerticalAlignment=VerticalAlignment.Center});
        }
        var text=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(12,0,8,0)};
        var preview=UI.Text(item.Kind=="image"?"Image copiée":item.Text.Replace("\r"," ").Replace("\n","  "),12);
        preview.TextTrimming=TextTrimming.CharacterEllipsis;preview.MaxHeight=34;preview.TextWrapping=TextWrapping.Wrap;text.Children.Add(preview);
        var meta=UI.Text(item.At+(item.Pinned?" · épinglé":""),10,UI.Dim);meta.Margin=new Thickness(0,4,0,0);text.Children.Add(meta);
        Grid.SetColumn(text,1);grid.Children.Add(text);
        var tools=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
        var pin=UI.IconButton("\uE840",item.Pinned?"Détacher":"Épingler",()=>service.TogglePin(item));pin.Width=28;pin.Height=28;pin.Background=item.Pinned?UI.Brush("#3AD8EEFF"):Brushes.Transparent;pin.BorderBrush=Brushes.Transparent;pin.Margin=new Thickness(0,0,2,0);tools.Children.Add(pin);
        var remove=UI.IconButton("\uE74D","Supprimer",()=>service.Remove(item));remove.Width=28;remove.Height=28;remove.Background=Brushes.Transparent;remove.BorderBrush=Brushes.Transparent;tools.Children.Add(remove);
        Grid.SetColumn(tools,2);grid.Children.Add(tools);
        row.Child=grid;
        row.MouseLeftButtonUp+=(_,e)=>{if(e.OriginalSource is Button)return;service.CopyBack(item);owner.ShowToast("Élément copié");Close();};
        Motion.Stagger(row,index);
        return row;
    }
}
