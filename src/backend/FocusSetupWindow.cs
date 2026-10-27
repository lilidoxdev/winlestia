// language: C# 12, file: FocusSetupWindow.cs, target: Windows 11 / .NET 8 WPF, focus duration chooser
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PanneauxGlass;
// The timer never decides for dj: presets are a starting point, the field and its unit are the truth.
internal sealed class FocusSetupWindow:GlassWindow {
    readonly Coordinator owner;readonly TextBox field;readonly TextBlock preview,status;readonly ComboBox unit=new();readonly List<(Button Button,int Value)> presets=new();
    int amount=25;
    internal FocusSetupWindow(Coordinator coordinator):base("Panneaux Glass — minuteur"){
        owner=coordinator;Width=560;Height=Math.Min(438,SystemParameters.WorkArea.Height-80);WindowStartupLocation=WindowStartupLocation.CenterOwner;Radius=32;ShowActivated=true;
        ContentHost.Margin=new Thickness(28,24,28,22);
        var root=new StackPanel();ContentHost.Children.Add(root);
        var header=new DockPanel{Margin=new Thickness(0,0,0,6)};var close=UI.IconButton("\uE711","Fermer le minuteur",Close);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        var titles=new StackPanel();titles.Children.Add(UI.Text("Un focus, à ta façon.",26,null,true));var sub=UI.Text("Choisis une durée en minutes, heures ou secondes. Le compte à rebours reste au-dessus de tes fenêtres.",12,UI.Dim);sub.Margin=new Thickness(0,7,0,0);sub.TextWrapping=TextWrapping.Wrap;titles.Children.Add(sub);header.Children.Add(titles);root.Children.Add(header);
        field=new TextBox{Text="25",FontSize=15,Padding=new Thickness(11,8,11,8),TextAlignment=TextAlignment.Center};System.Windows.Automation.AutomationProperties.SetName(field,"Durée du focus");
        var body=new StackPanel{Margin=new Thickness(0,18,0,0)};root.Children.Add(body);
        var chips=new WrapPanel();
        foreach(int value in new[]{5,10,15,25,45,60,90}){
            int chosen=value;var chip=UI.Button(value+" min",()=>{unit.SelectedIndex=0;amount=chosen;field.Text=chosen.ToString(CultureInfo.InvariantCulture);RefreshChips();});chip.Padding=new Thickness(15,9,15,9);chip.Margin=new Thickness(0,0,8,8);chip.FontSize=13;
            System.Windows.Automation.AutomationProperties.SetName(chip,value+" minutes");presets.Add((chip,value));chips.Children.Add(chip);
        }
        body.Children.Add(chips);
        var custom=new Grid{Margin=new Thickness(0,14,0,0)};custom.ColumnDefinitions.Add(new(){Width=GridLength.Auto});custom.ColumnDefinitions.Add(new(){Width=new GridLength(96)});custom.ColumnDefinitions.Add(new(){Width=new GridLength(150)});
        var label=UI.Text("Durée libre",13);label.VerticalAlignment=VerticalAlignment.Center;label.Margin=new Thickness(0,0,14,0);custom.Children.Add(label);
        Grid.SetColumn(field,1);custom.Children.Add(field);
        unit.Items.Add("minutes");unit.Items.Add("heures");unit.Items.Add("secondes");unit.SelectedIndex=0;unit.MinWidth=142;unit.Margin=new Thickness(10,0,0,0);unit.VerticalAlignment=VerticalAlignment.Center;System.Windows.Automation.AutomationProperties.SetName(unit,"Unité de la durée du focus");Grid.SetColumn(unit,2);custom.Children.Add(unit);
        body.Children.Add(custom);
        preview=UI.Text("",12,UI.Dim);preview.Margin=new Thickness(0,16,0,0);body.Children.Add(preview);
        status=UI.Text("",11,UI.Dim);status.Margin=new Thickness(0,6,0,0);status.TextWrapping=TextWrapping.Wrap;body.Children.Add(status);
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,18,0,0)};
        var cancel=UI.Button("Annuler",Close);actions.Children.Add(cancel);
        var start=UI.Button("Démarrer",Begin);start.MinWidth=118;start.Margin=new Thickness(10,0,0,0);start.Background=UI.Brush("#33BFE9F7");System.Windows.Automation.AutomationProperties.SetName(start,"Démarrer le focus");actions.Children.Add(start);
        body.Children.Add(actions);
        field.TextChanged+=(_,_)=>{if(int.TryParse(field.Text.Trim(),out int value)&&value is >=1 and <=86400)amount=value;RefreshChips();};
        unit.SelectionChanged+=(_,_)=>RefreshChips();
        field.PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){Begin();e.Handled=true;}};
        Loaded+=(_,_)=>{ApplyGlass();field.Focus();field.SelectAll();RefreshChips();};
        KeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape)Close();};
    }
    int Seconds()=>unit.SelectedIndex switch{1=>Math.Clamp(amount,1,24)*3600,2=>Math.Clamp(amount,1,86400),_=>Math.Clamp(amount,1,1440)*60};
    void RefreshChips(){
        foreach(var (button,value) in presets){bool active=unit.SelectedIndex==0&&value==amount;button.Background=active?UI.Brush("#48C4E9F8"):UI.Brush("#0FFFFFFF");button.BorderBrush=active?UI.Brush("#8AE2F3FF"):UI.Brush("#20FFFFFF");}
        int seconds=Seconds();var end=DateTime.Now.AddSeconds(seconds);
        preview.Text="Se termine à "+end.ToString("HH:mm:ss",CultureInfo.InvariantCulture)+" · "+FocusTimer.DurationText(seconds);
        preview.Foreground=UI.Dim;
        status.Text=unit.SelectedIndex==1&&amount>24?"Vingt-quatre heures au maximum.":unit.SelectedIndex==0&&amount>1440?"Vingt-quatre heures au maximum.":"";
    }
    void Begin(){
        int seconds=Seconds();Close();
        Dispatcher.BeginInvoke((Action)(()=>owner.StartFocus(seconds)),DispatcherPriority.Background);
    }
}
