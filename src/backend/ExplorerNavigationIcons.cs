// C#12 / ExplorerNavigationIcons.cs / removes legacy per-user namespace icon overrides (HKCU is not honoured by the navigation pane); original values restored.
using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Text.Json;using Microsoft.Win32;
namespace PanneauxGlass;
internal static class ExplorerNavigationIcons{
 internal sealed class Entry{public string Key{get;set;}="";public bool HadValue{get;set;}public string? Original{get;set;}public int Kind{get;set;}public string Applied{get;set;}="";}
 static string Manifest(string root)=>Path.Combine(root,"navigation-icons.json");
 static List<Entry> Read(string root)=>File.Exists(Manifest(root))?JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(Manifest(root)))??new():new();
 static void Save(string root,List<Entry> data){Directory.CreateDirectory(root);File.WriteAllText(Manifest(root)+".tmp",JsonSerializer.Serialize(data));File.Move(Manifest(root)+".tmp",Manifest(root),true);}
 internal static int Restore(string root){var data=Read(root);var remaining=new List<Entry>();foreach(var entry in data){try{using(var target=Registry.CurrentUser.OpenSubKey(entry.Key,true)){if(target==null)continue;if(target.GetValue("") as string!=entry.Applied){remaining.Add(entry);continue;}if(entry.HadValue)target.SetValue("",entry.Original??"",(RegistryValueKind)entry.Kind);else target.DeleteValue("",false);}using var check=Registry.CurrentUser.OpenSubKey(entry.Key);if(check!=null&&check.ValueCount==0&&check.SubKeyCount==0)Registry.CurrentUser.DeleteSubKey(entry.Key,false);}catch{remaining.Add(entry);}}Save(root,remaining);return remaining.Count;}
}
