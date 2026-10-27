// language: C# 12, file: WorkspaceVerification.cs, target: Windows 11 / .NET 8
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace PanneauxGlass;
internal static class WorkspaceVerification {
    internal static int Run(){var checks=new List<object>();int failed=0;void Check(bool pass,string name){checks.Add(new{Name=name,Pass=pass});if(!pass)failed++;}var originalState=Store.State;var originalEntries=Store.Entries;
        try{
            var bounds=new Rect(50,100,1800,1000);var panel=new Rect(250,300,800,500);
            foreach(string edge in new[]{"n","s","e","w","nw","ne","sw","se"}){var result=PanelLayout.Drag(panel,bounds,45,35,edge);bool horizontal=!edge.Contains('w')&&!edge.Contains('e')||result.Width!=panel.Width;bool vertical=!edge.Contains('n')&&!edge.Contains('s')||result.Height!=panel.Height;Check(horizontal&&vertical&&bounds.Contains(result),"Redimensionnement du bord "+edge);}
            var minimum=PanelLayout.Drag(panel,bounds,5000,5000,"nw");Check(minimum.Width==480&&minimum.Height==310&&minimum.Right==panel.Right&&minimum.Bottom==panel.Bottom,"Taille minimale et bord opposé conservé");
            var moved=PanelLayout.Drag(panel,bounds,5000,-5000,"move");Check(moved.Right==bounds.Right&&moved.Top==bounds.Top&&moved.Size==panel.Size,"Déplacement borné à l’écran");
            var placement=PanelPlacement.Capture(panel,bounds);Check(placement.Resolve(bounds)==panel,"Disposition restaurée à l’identique");var small=new Rect(0,100,900,600);Check(small.Contains(placement.Resolve(small)),"Disposition adaptée à une autre résolution");
            var preferences=JsonSerializer.Deserialize<Preferences>("{\"Favorites\":[\"a\"],\"Boards\":null}",Store.Json)!;preferences.Normalize();Check(preferences.HomeScope=="Tous"&&preferences.Boards.All(b=>b.Placement==null&&!b.FavoritesOnly)&&preferences.Favorites.SequenceEqual(new[]{"a"}),"Migration sans perte des favoris existants");
            preferences.Boards[1].Placement=placement;preferences.Boards[1].FavoritesOnly=true;var saved=JsonSerializer.Deserialize<Preferences>(JsonSerializer.Serialize(preferences,Store.Json),Store.Json)!;Check(saved.Boards[1].FavoritesOnly&&saved.Boards[1].Placement!.Resolve(bounds)==panel,"Persistance de la taille, position et étoile locale");
            Store.State=new Preferences{Favorites=new(){"a","b","c","d"},Recent=new(){"b","c","b"},Hidden=new(){"d"}};Store.Entries=new(){new(){Id="a",Name="Alpha",Section="Applications"},new(){Id="b",Name="Beta",Section="Jeux"},new(){Id="c",Name="Canada",Section="Dossiers"},new(){Id="d",Name="Masqué",Section="Applications"}};
            Check(Store.HomeFavorites().Select(e=>e.Id).SequenceEqual(new[]{"b","c","a"}),"Favoris triés par dernière ouverture, accès masqués exclus");Check(Store.HomeFavorites("games").Single().Id=="b","Favoris limités à la catégorie Jeux");Check(Store.Visible("Tous","cánad").Single().Id=="c","Recherche sans accents");
            Store.RecordLaunch("a");Store.RecordLaunch("a");Check(Store.State.Recent[0]=="a"&&Store.State.Recent.Count(id=>id=="a")==1&&Store.State.LaunchCounts["a"]==2,"Ouvertures : récence sans doublon et compteur");Check(Store.HomeFavorites().First().Id=="a","Favori ouvert replacé en tête");
        }catch(Exception ex){Check(false,ex.ToString());}finally{Store.State=originalState;Store.Entries=originalEntries;}
        File.WriteAllText(Path.Combine(Store.Root,"verification-workspace.json"),JsonSerializer.Serialize(new{At=DateTimeOffset.Now,Passed=checks.Count-failed,Failed=failed,Checks=checks},Store.Json));return failed==0?0:1;
    }
}
