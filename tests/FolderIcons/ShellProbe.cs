// Measures whether the shell icon cache returns new colours after an .ico is rewritten (same path vs alternate path).
using System;using System.Linq;using System.IO;using System.Runtime.InteropServices;using System.Threading;using D=System.Drawing;
static class ShellProbe{
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct FileInfo{public IntPtr Icon;public int Index;public uint Attr;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string Display;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)]public string Type;}
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern IntPtr SHGetFileInfo(string path,uint attrs,ref FileInfo info,uint size,uint flags);
 [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr icon);
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern void SHChangeNotify(uint kind,uint flags,string? a,IntPtr b);
 internal static D.Color Sample(string folder){var psi=new System.Diagnostics.ProcessStartInfo(Path.ChangeExtension(typeof(PanneauxGlass.Entry).Assembly.Location,null)==null?"":System.IO.Path.Combine(AppContext.BaseDirectory,"FolderIcons.exe"),"sample \""+folder+"\""){RedirectStandardOutput=true,UseShellExecute=false,CreateNoWindow=true};using var p=System.Diagnostics.Process.Start(psi)!;string text=p.StandardOutput.ReadToEnd().Trim();p.WaitForExit();var v=text.Split(',').Select(int.Parse).ToArray();return D.Color.FromArgb(v[0],v[1],v[2]);}
 internal static D.Color SampleInProc(string folder){var info=new FileInfo();SHGetFileInfo(folder,0,ref info,(uint)Marshal.SizeOf<FileInfo>(),0x100);if(info.Icon==IntPtr.Zero)return D.Color.Empty;try{using var icon=D.Icon.FromHandle(info.Icon);using var bmp=icon.ToBitmap();return bmp.GetPixel(bmp.Width/2,bmp.Height*3/4);}finally{DestroyIcon(info.Icon);}}
 static void Fixture(string folder,string icon){Directory.CreateDirectory(folder);string ini=Path.Combine(folder,"desktop.ini");if(File.Exists(ini))File.SetAttributes(ini,FileAttributes.Normal);File.WriteAllText(ini,"[.ShellClassInfo]\r\nIconResource="+icon+",0\r\n");File.SetAttributes(ini,FileAttributes.Hidden|FileAttributes.System);File.SetAttributes(folder,FileAttributes.System);}
 static void Draw(string path,string hex){var t=typeof(PanneauxGlass.Entry).Assembly.GetType("PanneauxGlass.ExplorerAppearance")!;string tmp=path+".tmp";t.GetMethod("DrawIcon",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.Invoke(null,new object[]{tmp,hex,"folder"});if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}
 static string Wait(string folder,Func<D.Color,bool> ok,int ms,out D.Color last){var end=DateTime.UtcNow.AddMilliseconds(ms);last=Sample(folder);var t0=DateTime.UtcNow;while(!ok(last)&&DateTime.UtcNow<end){Thread.Sleep(200);last=Sample(folder);}return ok(last)?((int)(DateTime.UtcNow-t0).TotalMilliseconds)+" ms":"never ("+ms+" ms)";}

 // Long-lived "Explorer proxy": warm in-process icon cache, pumps messages, reports when the colour changes.
 internal static string Watch(string folder,int ms){var end=DateTime.UtcNow.AddMilliseconds(ms);var first=SampleInProc(folder);var t0=DateTime.UtcNow;using var form=new System.Windows.Forms.Form();form.Show();while(DateTime.UtcNow<end){System.Windows.Forms.Application.DoEvents();Thread.Sleep(100);var c=SampleInProc(folder);if(Math.Abs(c.R-first.R)>60){return "CHANGED after "+(int)(DateTime.UtcNow-t0).TotalMilliseconds+" ms";}}return "UNCHANGED for "+ms+" ms";}
 static string Scenario(string dir,string name,Action<string,string,string> change,bool altPath){
  string a=Path.Combine(dir,name+"-a.ico"),b=Path.Combine(dir,name+"-b.ico"),f=Path.Combine(dir,name);Draw(a,"#fa3c5b");if(altPath)Draw(b,"#3c9cfa");Fixture(f,a);
  var psi=new System.Diagnostics.ProcessStartInfo(System.IO.Path.Combine(AppContext.BaseDirectory,"FolderIcons.exe"),"watch \""+f+"\" 9000"){RedirectStandardOutput=true,UseShellExecute=false,CreateNoWindow=true};using var p=System.Diagnostics.Process.Start(psi)!;Thread.Sleep(2500);change(f,a,b);string r=p.StandardOutput.ReadToEnd().Trim();p.WaitForExit();return name+": watcher(warm cache, message pump) "+r;}
 internal static string RunWatch(string dir){var o=new System.Text.StringBuilder();Directory.CreateDirectory(dir);
  o.AppendLine(Scenario(dir,"w1-samepath-nonotify",(f,a,b)=>Draw(a,"#3c9cfa"),false));
  o.AppendLine(Scenario(dir,"w2-samepath-assoc",(f,a,b)=>{Draw(a,"#3c9cfa");SHChangeNotify(0x08000000,0x1000|0x2000,null,IntPtr.Zero);},false));
  o.AppendLine(Scenario(dir,"w3-samepath-assoc+updatedir+ini",(f,a,b)=>{Draw(a,"#3c9cfa");SHChangeNotify(0x08000000,0x1000|0x2000,null,IntPtr.Zero);Fixture(f,a);SHChangeNotify(0x2000,5|0x3000,f,IntPtr.Zero);SHChangeNotify(0x1000,5|0x3000,f,IntPtr.Zero);},false));
  o.AppendLine(Scenario(dir,"w4-altpath-iniRewrite+updatedir",(f,a,b)=>{Fixture(f,b);SHChangeNotify(0x2000,5|0x3000,f,IntPtr.Zero);},true));
  o.AppendLine(Scenario(dir,"w5-altpath-iniRewrite+updatedir+assoc",(f,a,b)=>{Fixture(f,b);SHChangeNotify(0x08000000,0x1000|0x2000,null,IntPtr.Zero);SHChangeNotify(0x2000,5|0x3000,f,IntPtr.Zero);},true));
  o.AppendLine(Scenario(dir,"w6-samepath-UPDATEIMAGE(-1)+assoc",(f,a,b)=>{Draw(a,"#3c9cfa");SHChangeNotify(0x00008000,0x1000,null,(IntPtr)(-1));SHChangeNotify(0x08000000,0x1000|0x2000,null,IntPtr.Zero);},false));
  return o.ToString();}
 internal static string Run(string dir){
  var o=new System.Text.StringBuilder();Directory.CreateDirectory(dir);
  Func<D.Color,bool> red=c=>c.R>c.B+60,blue=c=>c.B>c.R+60;
  // (a) same path, content rewritten, no notification
  string a=Path.Combine(dir,"a.ico"),fa=Path.Combine(dir,"fa");Draw(a,"#fa3c5b");Fixture(fa,a);var c1=Sample(fa);Draw(a,"#3c9cfa");string r=Wait(fa,blue,4000,out var c2);o.AppendLine("(a1) same path, no notify: before="+c1+" after="+c2+" turned blue: "+r);
  // (a2) same path, ASSOCCHANGED
  string a2=Path.Combine(dir,"a2.ico"),fa2=Path.Combine(dir,"fa2");Draw(a2,"#fa3c5b");Fixture(fa2,a2);c1=Sample(fa2);Draw(a2,"#3c9cfa");SHChangeNotify(0x08000000,0x1000|0x2000,null,IntPtr.Zero);r=Wait(fa2,blue,4000,out c2);o.AppendLine("(a2) same path + ASSOCCHANGED(FLUSHNOWAIT): before="+c1+" after="+c2+" blue: "+r);
  // (a3) same path, ASSOCCHANGED + UPDATEDIR on folder
  string a3=Path.Combine(dir,"a3.ico"),fa3=Path.Combine(dir,"fa3");Draw(a3,"#fa3c5b");Fixture(fa3,a3);c1=Sample(fa3);Draw(a3,"#3c9cfa");SHChangeNotify(0x08000000,0x1000|0x2000,null,IntPtr.Zero);SHChangeNotify(0x1000,0x5|0x3000,fa3,IntPtr.Zero);SHChangeNotify(0x2000,0x5|0x3000,fa3,IntPtr.Zero);r=Wait(fa3,blue,4000,out c2);o.AppendLine("(a3) same path + ASSOCCHANGED + UPDATEDIR/ITEM: before="+c1+" after="+c2+" blue: "+r);
  // (a4) same path, UPDATEIMAGE-less: touch ini only
  string a4=Path.Combine(dir,"a4.ico"),fa4=Path.Combine(dir,"fa4");Draw(a4,"#fa3c5b");Fixture(fa4,a4);c1=Sample(fa4);Draw(a4,"#3c9cfa");Fixture(fa4,a4);SHChangeNotify(0x2000,0x5|0x3000,fa4,IntPtr.Zero);r=Wait(fa4,blue,4000,out c2);o.AppendLine("(a4) same path + ini rewritten (same text) + UPDATEDIR: before="+c1+" after="+c2+" blue: "+r);
  // (b) alternate path, ini rewritten
  string b1=Path.Combine(dir,"b-a.ico"),b2=Path.Combine(dir,"b-b.ico"),fb=Path.Combine(dir,"fb");Draw(b1,"#fa3c5b");Draw(b2,"#3c9cfa");Fixture(fb,b1);c1=Sample(fb);Fixture(fb,b2);SHChangeNotify(0x2000,0x5|0x3000,fb,IntPtr.Zero);r=Wait(fb,blue,4000,out c2);o.AppendLine("(b) alternate path + ini rewrite + UPDATEDIR: before="+c1+" after="+c2+" blue: "+r);
  return o.ToString();}
}
