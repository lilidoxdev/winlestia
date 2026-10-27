// language: C# 12, file: FileTools.cs, target: Windows 11 / .NET 8 WPF, disk and system hygiene
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace PanneauxGlass;
internal static class SystemTools {
    const string NotificationsKey=@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";
    const string ToastsValue="NOC_GLOBAL_SETTING_TOASTS_ENABLED";
    internal static bool NotificationsOn{
        get{try{using var key=Registry.CurrentUser.OpenSubKey(NotificationsKey);object? value=key?.GetValue(ToastsValue);return value==null||Convert.ToInt32(value)!=0;}catch{return true;}}
    }
    internal static void SetNotifications(bool on){
        using var key=Registry.CurrentUser.CreateSubKey(NotificationsKey);
        key.SetValue(ToastsValue,on?1:0,RegistryValueKind.DWord);
    }
    internal static void RestartExplorer(){
        foreach(var process in Process.GetProcessesByName("explorer")){try{process.Kill();}catch{}}
        var timer=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(900)};
        timer.Tick+=(_,_)=>{timer.Stop();try{Process.Start(new ProcessStartInfo("explorer.exe"){UseShellExecute=true});}catch{}};timer.Start();
    }
    internal static string Size(long bytes)=>bytes>=1L<<30?Math.Round(bytes/(double)(1L<<30),1)+" Go":bytes>=1L<<20?Math.Round(bytes/(double)(1L<<20))+" Mo":Math.Round(bytes/1024d)+" Ko";
}
internal sealed class LargeFilesWindow:GlassWindow {
    readonly StackPanel list=new();readonly TextBlock status;readonly Button scan;CancellationTokenSource? cancel;
    internal LargeFilesWindow(Coordinator coordinator):base("Panneaux Glass — gros fichiers"){
        Width=680;Height=Math.Min(700,SystemParameters.WorkArea.Height-80);WindowStartupLocation=WindowStartupLocation.CenterOwner;Radius=32;ShowActivated=true;
        ContentHost.Margin=new Thickness(26,22,26,18);ContentHost.RowDefinitions.Add(new(){Height=GridLength.Auto});ContentHost.RowDefinitions.Add(new(){Height=GridLength.Auto});ContentHost.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});ContentHost.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new DockPanel();var close=UI.IconButton("\uE711","Fermer",Close);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        var titles=new StackPanel();titles.Children.Add(UI.Text("Ce qui remplit le disque.",25,null,true));var sub=UI.Text("Les fichiers de plus de 200 Mo, classés du plus lourd au plus léger",11,UI.Dim);sub.Margin=new Thickness(0,6,0,0);titles.Children.Add(sub);header.Children.Add(titles);ContentHost.Children.Add(header);
        var actions=new DockPanel{Margin=new Thickness(0,14,0,12)};scan=UI.Button("Choisir un dossier…",Choose);scan.Padding=new Thickness(14,8,14,8);DockPanel.SetDock(scan,Dock.Right);actions.Children.Add(scan);
        status=UI.Text("Aucun dossier choisi",11,UI.Dim);status.VerticalAlignment=VerticalAlignment.Center;status.TextTrimming=TextTrimming.CharacterEllipsis;actions.Children.Add(status);Grid.SetRow(actions,1);ContentHost.Children.Add(actions);
        var scroll=new ScrollViewer{Content=list,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,0,0,8)};Grid.SetRow(scroll,2);ContentHost.Children.Add(scroll);
        var note=UI.Text("Rien n’est supprimé depuis Panneaux : chaque ligne ouvre l’Explorateur sur le fichier.",10,UI.Dim);note.TextWrapping=TextWrapping.Wrap;Grid.SetRow(note,3);ContentHost.Children.Add(note);
        Loaded+=(_,_)=>ApplyGlass();Closed+=(_,_)=>cancel?.Cancel();KeyDown+=(_,e)=>{if(e.Key==Key.Escape)Close();};
    }
    void Choose(){
        using var picker=new System.Windows.Forms.FolderBrowserDialog{Description="Choisir le dossier à analyser",UseDescriptionForTitle=true,ShowNewFolderButton=false};
        if(picker.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return;
        Scan(picker.SelectedPath);
    }
    async void Scan(string folder){
        cancel?.Cancel();cancel=new CancellationTokenSource();var token=cancel.Token;
        list.Children.Clear();scan.IsEnabled=false;status.Text="Analyse de "+folder+"…";var watch=Stopwatch.StartNew();
        try{
            var found=await Task.Run(()=>Find(folder,token),token);
            if(token.IsCancellationRequested)return;
            long total=found.Sum(file=>file.Size);
            status.Text=found.Count+" fichier"+(found.Count==1?"":"s")+" · "+SystemTools.Size(total)+" · "+watch.Elapsed.TotalSeconds.ToString("0.0")+" s";
            if(found.Count==0){var empty=UI.Text("Aucun fichier de plus de 200 Mo dans ce dossier.",12,UI.Dim);empty.Margin=new Thickness(4,18,0,0);list.Children.Add(empty);return;}
            int index=0;foreach(var file in found.Take(120))list.Children.Add(Row(file,index++));
        }catch(OperationCanceledException){status.Text="Analyse annulée";}
        catch(Exception ex){status.Text="Analyse impossible : "+ex.Message;}
        finally{scan.IsEnabled=true;}
    }
    static List<(string Path,long Size)> Find(string folder,CancellationToken token){
        var options=new EnumerationOptions{RecurseSubdirectories=true,IgnoreInaccessible=true,AttributesToSkip=FileAttributes.ReparsePoint};
        var result=new List<(string,long)>();
        foreach(string path in Directory.EnumerateFiles(folder,"*",options)){
            if(token.IsCancellationRequested)throw new OperationCanceledException();
            try{var info=new FileInfo(path);if(info.Length>=200L*1024*1024)result.Add((path,info.Length));}catch{}
        }
        return result.OrderByDescending(item=>item.Item2).Take(200).ToList();
    }
    FrameworkElement Row((string Path,long Size) file,int index){
        var card=new Border{CornerRadius=new CornerRadius(14),Padding=new Thickness(11),Margin=new Thickness(0,0,0,6),Background=UI.Brush("#0AFFFFFF"),BorderBrush=UI.Brush("#18FFFFFF"),BorderThickness=new Thickness(1)};
        var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(70)});grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var text=new StackPanel();var name=UI.Text(Path.GetFileName(file.Path),12,null,true);name.TextTrimming=TextTrimming.CharacterEllipsis;text.Children.Add(name);
        var folder=UI.Text(Path.GetDirectoryName(file.Path)??"",10,UI.Dim);folder.TextTrimming=TextTrimming.CharacterEllipsis;folder.Margin=new Thickness(0,3,0,0);text.Children.Add(folder);grid.Children.Add(text);
        var size=UI.Text(SystemTools.Size(file.Size),11,UI.Dim);size.TextAlignment=TextAlignment.Right;size.VerticalAlignment=VerticalAlignment.Center;Grid.SetColumn(size,1);grid.Children.Add(size);
        var reveal=UI.IconButton("\uE8B7","Afficher dans l’Explorateur",()=>{try{Process.Start("explorer.exe","/select,\""+file.Path+"\"");}catch{}});reveal.Width=28;reveal.Height=28;reveal.Margin=new Thickness(8,0,0,0);reveal.Background=Brushes.Transparent;reveal.BorderBrush=Brushes.Transparent;Grid.SetColumn(reveal,2);grid.Children.Add(reveal);
        card.Child=grid;Motion.Stagger(card,index);return card;
    }
}
