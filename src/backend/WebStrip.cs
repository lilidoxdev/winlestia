// language: C# 12, file: WebStrip.cs, target: Windows 11 / .NET 8 WPF, independent compact web icons growing to the left
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PanneauxGlass;
internal sealed class WebStrip:IDisposable {
    readonly Coordinator coordinator;readonly List<GlassWindow> windows=new();string signature="~";double right,top,available;bool placed,visible;
    const double Tile=56,Gap=10;
    internal IEnumerable<GlassWindow> Windows=>windows;
    internal WebStrip(Coordinator owner){coordinator=owner;}
    internal void Arrange(double rightEdge,double y,double width,bool animate){right=rightEdge;top=y+4;available=width;placed=true;visible=true;Refresh();Place(animate);}
    internal void Refresh(){string next=string.Join("|",Store.State.Websites.Select(x=>x.Id+x.Name+x.Url+x.IconPath+x.ShowGlass+File.GetLastWriteTimeUtc(x.IconPath).Ticks))+"/"+Math.Floor(available/(Tile+Gap));if(signature==next)return;signature=next;foreach(var window in windows.ToArray())window.Close();windows.Clear();
        int slots=Math.Max(1,(int)((available+Gap)/(Tile+Gap))),capacity=Math.Max(0,slots-1);bool overflow=Store.State.Websites.Count>capacity;if(overflow)capacity=Math.Max(0,capacity-1);
        foreach(var site in Store.State.Websites.Take(capacity)){var window=MakeWindow("site · "+site.Name,site.ShowGlass);var button=UI.Button("",()=>Launch(site));button.ToolTip=site.Name;System.Windows.Automation.AutomationProperties.SetName(button,site.Name);System.Windows.Automation.AutomationProperties.SetHelpText(button,site.Url);var image=new Image{Width=34,Height=34,Stretch=Stretch.Uniform};try{image.Source=WebIcons.Image(site.IconPath);}catch{image.Source=FallbackImage();}button.Content=image;PrepareButton(window,button);
            button.MouseRightButtonUp+=(_,e)=>{GlassMenu.Open(coordinator,window,button,site.Name,new[]{new MenuAction("Ouvrir dans le navigateur","\uE774",()=>Launch(site)),new MenuAction("Modifier ce site…","\uE70F",()=>coordinator.AddWebsite(site)),new MenuAction("Image et recadrage…","\uE7A8",()=>coordinator.AddWebsite(site,true)),new MenuAction("Déplacer vers la gauche","\uE72B",()=>Move(site,1),Separator:true),new MenuAction("Déplacer vers la droite","\uE72A",()=>Move(site,-1)),new MenuAction("Retirer ce raccourci","\uE738",()=>{Store.State.Websites.Remove(site);Store.Save();Refresh();},Separator:true)},true);e.Handled=true;};
        }
        if(overflow){var window=MakeWindow("autres sites",true);var button=UI.IconButton("\uE712","Autres sites",()=>{});PrepareButton(window,button);button.Click+=(_,_)=>GlassMenu.Open(coordinator,window,button,"Autres sites",Store.State.Websites.Skip(capacity).Select(site=>new MenuAction(site.Name,"\uE774",()=>Launch(site))));}
        var plusWindow=MakeWindow("ajouter un site",true);PrepareButton(plusWindow,UI.IconButton("\uE710","Ajouter un site web",()=>coordinator.AddWebsite()));if(placed&&visible)Place(false);
    }
    GlassWindow MakeWindow(string title,bool material){var window=new GlassWindow("Panneaux Glass — "+title){Radius=18,MaterialVisible=material,ShowInTaskbar=coordinator.PreviewMode};window.KeyDown+=(_,e)=>{if(e.Key==Key.Escape)coordinator.Collapse();};windows.Add(window);return window;}
    static void PrepareButton(GlassWindow window,Button button){button.Width=Tile;button.Height=Tile;button.Padding=new Thickness(0);button.Margin=new Thickness(0);button.Background=Brushes.Transparent;button.BorderThickness=new Thickness(0);window.ContentHost.Children.Add(button);}
    void Place(bool animate){for(int i=0;i<windows.Count;i++)windows[i].MoveInto(right-Tile-i*(Tile+Gap),top,Tile,Tile,i,animate);}
    internal void HideImmediately(){visible=false;foreach(var window in windows)window.HideImmediately();}
    internal void SlideOut(Action completed){visible=false;if(windows.Count==0){completed();return;}int remaining=windows.Count;foreach(var window in windows)window.SlideOut(0,()=>{if(--remaining==0)completed();});}
    void Launch(WebShortcut site){try{Process.Start(new ProcessStartInfo(WebIcons.Parse(site.Url).AbsoluteUri){UseShellExecute=true});coordinator.CollapseImmediately();}catch(Exception ex){coordinator.Message("Ce site n’a pas pu s’ouvrir.\n"+ex.Message);}}
    void Move(WebShortcut site,int by){int index=Store.State.Websites.IndexOf(site),target=Math.Clamp(index+by,0,Store.State.Websites.Count-1);if(index==target)return;Store.State.Websites.RemoveAt(index);Store.State.Websites.Insert(target,site);Store.Save();Refresh();}
    internal static ImageSource FallbackImage(){var group=new DrawingGroup();using(var drawing=group.Open()){var pen=new Pen(UI.Brush("#B5E7FF").CloneCurrentValue(),5);drawing.DrawEllipse(null,pen,new Point(48,48),36,36);drawing.DrawEllipse(null,pen,new Point(48,48),17,36);drawing.DrawLine(pen,new Point(13,48),new Point(83,48));}group.Freeze();return new DrawingImage(group);}
    public void Dispose(){foreach(var window in windows.ToArray())window.Close();windows.Clear();}
}