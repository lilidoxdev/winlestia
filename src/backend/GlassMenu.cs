// language: C# 12, file: GlassMenu.cs, target: Windows 11 / .NET 8 WPF, native acrylic menus with keyboard navigation
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PanneauxGlass;
internal sealed record MenuAction(string Label,string Glyph,Action Run,bool Selected=false,bool Separator=false);
internal sealed class GlassMenu:GlassWindow {
    static GlassMenu? active;
    internal static void DismissActive()=>active?.Dismiss();
    bool dismissing;
    void Dismiss(){if(dismissing)return;dismissing=true;Close();}
    internal static void Open(Coordinator coordinator,Window parent,FrameworkElement anchor,string title,IEnumerable<MenuAction> actions,bool atPointer=false){
        active?.Dismiss();var menu=new GlassMenu(coordinator,parent,title,actions.ToList());active=menu;
        Point point=atPointer?parent.PointToScreen(Mouse.GetPosition(parent)):anchor.PointToScreen(new Point(0,anchor.ActualHeight+7));
        var screen=System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)point.X,(int)point.Y));double scale=VisualTreeHelper.GetDpi(parent).DpiScaleX;var area=screen.WorkingArea;menu.Width=Math.Min(menu.Width,area.Width/scale-16);menu.Height=Math.Min(menu.Height,area.Height/scale-16);
        menu.Left=Math.Clamp(point.X/scale,area.Left/scale+8,area.Right/scale-menu.Width-8);menu.Top=Math.Clamp(point.Y/scale,area.Top/scale+8,area.Bottom/scale-menu.Height-8);
        coordinator.PopupOpen=true;menu.Closed+=(_,_)=>{coordinator.PopupOpen=false;if(active==menu)active=null;};menu.Show();menu.Activate();
    }
    GlassMenu(Coordinator coordinator,Window parent,string title,List<MenuAction> actions):base("Panneaux Glass — "+title){
        Owner=parent;ShowInTaskbar=coordinator.PreviewMode;ShowActivated=true;Radius=23;Width=318;
        Height=Math.Min(58+actions.Count*41+actions.Count(x=>x.Separator)*9,SystemParameters.WorkArea.Height-40);ContentHost.Margin=new Thickness(9);
        ContentHost.RowDefinitions.Add(new(){Height=GridLength.Auto});ContentHost.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var caption=UI.Text(title.ToUpperInvariant(),10,UI.Label,true);caption.Margin=new Thickness(14,9,10,13);ContentHost.Children.Add(caption);
        var list=new StackPanel();var scroll=new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Grid.SetRow(scroll,1);ContentHost.Children.Add(scroll);
        var buttons=new List<Button>();foreach(var action in actions){if(action.Separator)list.Children.Add(new Border{Height=1,Background=UI.T(M3Role.OnSurface,0x22),Margin=new Thickness(13,4,13,5)});
            var button=UI.Button("",()=>{Dismiss();Dispatcher.BeginInvoke(action.Run);});button.Height=41;button.Padding=new Thickness(12,0,12,0);button.Background=action.Selected?UI.Selected:Brushes.Transparent;button.BorderThickness=new Thickness(0);button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
            var row=new Grid();row.ColumnDefinitions.Add(new(){Width=new GridLength(28)});row.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new(){Width=new GridLength(22)});
            var glyph=UI.Glyph(action.Glyph,17);glyph.HorizontalAlignment=HorizontalAlignment.Left;glyph.Foreground=UI.Label;row.Children.Add(glyph);
            var label=UI.Text(action.Label,13);label.VerticalAlignment=VerticalAlignment.Center;label.TextTrimming=TextTrimming.CharacterEllipsis;Grid.SetColumn(label,1);row.Children.Add(label);if(action.Selected){var check=UI.Glyph("\uE73E",13);Grid.SetColumn(check,2);row.Children.Add(check);}button.Content=row;System.Windows.Automation.AutomationProperties.SetName(button,action.Label);list.Children.Add(button);buttons.Add(button);
        }
        Closing+=(_,_)=>dismissing=true;Deactivated+=(_,_)=>Dismiss();Loaded+=(_,_)=>{Motion.Reveal(ContentHost);int selected=Math.Max(0,actions.FindIndex(x=>x.Selected));if(buttons.Count>0)buttons[selected].Focus();};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){Dismiss();e.Handled=true;}if(e.Key is Key.Down or Key.Up){int i=buttons.FindIndex(x=>x.IsKeyboardFocused);if(buttons.Count>0)buttons[(i+(e.Key==Key.Down?1:buttons.Count-1)+buttons.Count)%buttons.Count].Focus();e.Handled=true;}};
    }
    internal static string CategoryGlyph(string label){string text=Store.Fold(label);return text.Contains("jeu")?"\uE7FC":text.Contains("intelligence")||text.Contains("assistant")?"\uE99A":text.Contains("develop")||text.Contains("script")||text.Contains("reverse")?"\uE943":text.Contains("media")||text.Contains("video")?"\uE714":text.Contains("canada")?"\uE707":text.Contains("admin")?"\uE8A5":text.Contains("bot")?"\uE713":"\uE8B7";}
}
