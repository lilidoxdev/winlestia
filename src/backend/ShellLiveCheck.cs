// language: C# 12, file: ShellLiveCheck.cs, target: Windows 11 / .NET 8 WPF, native lifecycle integration check
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PanneauxGlass;
internal static class ShellLiveCheck {
    internal static void Capture(Window window,string name){window.UpdateLayout();if(window.Content is not FrameworkElement visual||visual.ActualWidth<1)return;var bitmap=new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth),(int)Math.Ceiling(visual.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(Store.Root,name));encoder.Save(stream);}
    internal static async void Run(Coordinator owner){var checks=new List<object>();int failed=0;void Check(bool valid,string name){checks.Add(new{Name=name,Pass=valid});if(!valid)failed++;}string originalTheme=Store.State.Theme;var favorites=Store.State.Favorites.ToArray();try{
        owner.Pinned=true;owner.ShowShellPage("dashboard");await Task.Delay(700);Check(owner.ShellPage=="dashboard"&&owner.Anchor.IsVisible,"Accueil natif visible");Capture(owner.Anchor,"shell-home-native.png");foreach(var bar in Application.Current.Windows.OfType<ShellDockWindow>())Capture(bar,"shell-taskbar-native.png");
        owner.ShowShellPage("music");await Task.Delay(800);Check(owner.ShellPage=="music"&&owner.Anchor.IsVisible,"Navigation musique native");Capture(owner.Anchor,"shell-music-native.png");
        owner.ShowShellPage("performance");for(int i=0;i<40&&owner.ShellSampling.Samples==0;i++)await Task.Delay(500);Check(owner.ShellSampling.Running&&owner.ShellSampling.Samples>0,"Mesures matérielles réelles publiées : "+owner.ShellSampling);await Task.Delay(1600);Capture(owner.Anchor,"shell-performance-native.png");
        owner.SetTheme("shell-dark");await Task.Delay(500);Check(Theme.Current.Id=="shell-dark","Changement de thème après création des graphiques");Capture(owner.Anchor,"shell-performance-dark-native.png");owner.SetTheme("shell");owner.ShowShellPage("spaces");await Task.Delay(1500);Check(!owner.ShellSampling.Running,"Collecteur arrêté en quittant Performances");Check(owner.ShellPage=="spaces","Navigation espaces et accès existants");
        for(int i=0;i<12;i++){owner.ShowShellPage(i%2==0?"dashboard":"music");await Task.Delay(70);}owner.ShowShellPage("dashboard");await Task.Delay(500);Check(owner.ShellPage=="dashboard","Transitions rapides interrompues : état final cohérent");Capture(owner.Anchor,"shell-home-native.png");
        owner.ShowWorkspace();await Task.Delay(600);Check(owner.Boards.Count(b=>b.IsVisible)==4,"Les quatre panneaux de fichiers restent accessibles");owner.CollapseImmediately();await Task.Delay(1300);Check(!owner.ShellSampling.Running&&!owner.UiActivity.open,"Collecte suspendue après fermeture : "+owner.ShellSampling+" / ouvert="+owner.UiActivity.open);Check(favorites.SequenceEqual(Store.State.Favorites),"Favoris conservés pendant toute la navigation");
    }catch(Exception ex){Check(false,ex.ToString());}finally{owner.SetTheme(originalTheme);File.WriteAllText(Path.Combine(Store.Root,"verification-shell-live.json"),JsonSerializer.Serialize(new{At=DateTimeOffset.Now,Passed=checks.Count-failed,Failed=failed,Checks=checks},Store.Json));Application.Current.Shutdown(failed==0?0:1);}}
}
