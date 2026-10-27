// language: C# 12, file: NotesWindow.cs, target: Windows 11 / .NET 8 WPF, persistent scratchpad in the glass layer
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PanneauxGlass;
// One note, saved in preferences, never shown anywhere else. It survives a restart and a collapsed launcher.
internal sealed class NotesWindow:GlassWindow {
    readonly Coordinator owner;readonly TextBox editor=new();readonly TextBlock status;
    readonly DispatcherTimer save=new(){Interval=TimeSpan.FromMilliseconds(500)};
    internal NotesWindow(Coordinator coordinator):base("Panneaux Glass — note rapide"){
        owner=coordinator;Width=660;Height=Math.Min(620,SystemParameters.WorkArea.Height-80);WindowStartupLocation=WindowStartupLocation.CenterScreen;Radius=34;
        ContentHost.Margin=new Thickness(28,24,28,20);ContentHost.RowDefinitions.Add(new(){Height=GridLength.Auto});ContentHost.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});ContentHost.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new DockPanel{Margin=new Thickness(0,0,0,18)};var close=UI.IconButton("\uE711","Fermer la note",Close);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        var titles=new StackPanel();titles.Children.Add(UI.Text("Ta note rapide.",26,null,true));var sub=UI.Text("Tout ce qui ne mérite pas un fichier · enregistré automatiquement",12,UI.Dim);sub.Margin=new Thickness(0,7,0,0);titles.Children.Add(sub);header.Children.Add(titles);ContentHost.Children.Add(header);
        editor.Text=Store.State.Scratch;editor.AcceptsReturn=true;editor.AcceptsTab=true;editor.TextWrapping=TextWrapping.Wrap;editor.FontSize=14;editor.Padding=new Thickness(16,14,16,14);editor.VerticalContentAlignment=VerticalAlignment.Top;editor.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;editor.Margin=new Thickness(0,0,0,16);System.Windows.Automation.AutomationProperties.SetName(editor,"Contenu de la note rapide");Grid.SetRow(editor,1);ContentHost.Children.Add(editor);
        var footer=new DockPanel();var actions=new StackPanel{Orientation=Orientation.Horizontal};var clear=UI.Button("Effacer",Erase);clear.Margin=new Thickness(0,0,8,0);actions.Children.Add(clear);
        var copy=UI.Button("Copier",()=>SystemActions.Copy(owner,editor.Text,"Note"));copy.Margin=new Thickness(0,0,8,0);actions.Children.Add(copy);
        var done=UI.Button("Terminer",Close);done.MinWidth=110;actions.Children.Add(done);DockPanel.SetDock(actions,Dock.Right);footer.Children.Add(actions);
        status=UI.Text("",11,UI.Dim);status.VerticalAlignment=VerticalAlignment.Center;status.TextTrimming=TextTrimming.CharacterEllipsis;status.Margin=new Thickness(2,0,14,0);footer.Children.Add(status);
        Grid.SetRow(footer,2);ContentHost.Children.Add(footer);
        save.Tick+=(_,_)=>{save.Stop();Persist();};
        editor.TextChanged+=(_,_)=>{if(!IsLoaded)return;save.Stop();save.Start();status.Text="Modifié …";};
        editor.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter&&Keyboard.Modifiers==ModifierKeys.Control){SystemActions.Copy(owner,editor.Text,"Note");e.Handled=true;}};
        Loaded+=(_,_)=>{editor.Focus();editor.CaretIndex=editor.Text.Length;status.Text=editor.Text.Length==0?"Une idée, une commande, un lien : tout reste ici.":"Enregistré";};
        Closed+=(_,_)=>{save.Stop();Persist();};
    }
    void Persist(){try{if(Store.State.Scratch==editor.Text)return;Store.State.Scratch=editor.Text;Store.Save();status.Text="Enregistré à "+DateTime.Now.ToString("HH:mm");}catch(Exception ex){status.Text=ex.Message;}}
    void Erase(){if(editor.Text.Length==0)return;if(!owner.Confirm("Effacer le contenu de la note ?\n\nElle sera vide au prochain enregistrement."))return;editor.Text="";Persist();status.Text="Note effacée";}
}
