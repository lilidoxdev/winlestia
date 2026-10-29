// C#12 / RecycleActions.cs / Windows11. Only explicit local file drops use the Windows recycle operation.
using System;using System.Linq;using System.Collections.Specialized;using System.IO.Compression;using System.IO;using System.Runtime.InteropServices;using Microsoft.VisualBasic.FileIO;
namespace PanneauxGlass;
internal static class RecycleActions{
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern int SHEmptyRecycleBin(IntPtr owner,string? root,uint flags);
 internal static void Empty(){int result=SHEmptyRecycleBin(IntPtr.Zero,null,1);if(result<0)Marshal.ThrowExceptionForHR(result);}
 internal static string[] Existing(string[] paths)=>paths.Where(Path.IsPathFullyQualified).Select(Path.GetFullPath).Where(p=>File.Exists(p)||Directory.Exists(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
 internal static void ClipboardFiles(string[] paths){var values=new StringCollection();values.AddRange(Existing(paths));if(values.Count>0)System.Windows.Clipboard.SetFileDropList(values);}
 internal static string? ChooseZip(string[] paths){var valid=Existing(paths);if(valid.Length==0)return null;var dialog=new Microsoft.Win32.SaveFileDialog{Title="Créer une archive ZIP",Filter="Archive ZIP|*.zip",DefaultExt=".zip",FileName="Winlestia-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".zip",InitialDirectory=Path.GetDirectoryName(valid[0])};return dialog.ShowDialog()==true?dialog.FileName:null;}
 internal static void Zip(string[] paths,string destination){string temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{using(var archive=ZipFile.Open(temporary,ZipArchiveMode.Create)){var names=new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
   void Add(string path,string name){if(string.Equals(path,destination,StringComparison.OrdinalIgnoreCase)||string.Equals(path,temporary,StringComparison.OrdinalIgnoreCase))return;
    if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)return;
    if(Directory.Exists(path)){archive.CreateEntry(name+"/");foreach(var child in Directory.EnumerateFileSystemEntries(path))Add(child,name+"/"+Path.GetFileName(child));}
    else archive.CreateEntryFromFile(path,name,CompressionLevel.Fastest);
   }
   foreach(var path in Existing(paths)){string original=Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));if(string.IsNullOrEmpty(original))original="Volume";string name=original;int n=2;while(!names.Add(name))name=Path.GetFileNameWithoutExtension(original)+" ("+n+++ ")"+Path.GetExtension(original);Add(path,name);}
  }File.Move(temporary,destination,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
 internal static void Recycle(string[] paths){foreach(string value in paths){
  string path=Path.GetFullPath(value);if(!Path.IsPathFullyQualified(value)||!File.Exists(path)&&!Directory.Exists(path))continue;
  if(Directory.Exists(path))FileSystem.DeleteDirectory(path,UIOption.AllDialogs,RecycleOption.SendToRecycleBin,UICancelOption.DoNothing);
  else FileSystem.DeleteFile(path,UIOption.AllDialogs,RecycleOption.SendToRecycleBin,UICancelOption.DoNothing);
 }}
}
