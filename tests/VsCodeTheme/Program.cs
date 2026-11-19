// C#12 / VsCodeTheme: JSONC surgical edit (empty, {}, comments, key in comment/string, nested key, trailing comma, BOM, CRLF, invalid), byte-exact restore, colour mapping, file layer in a temp folder only.
using System;using System.Collections.Generic;using System.IO;using System.Linq;using System.Text;using PanneauxGlass;
class Test{
static readonly Dictionary<string,string> Pal=new(){{"m3surface","#101518"},{"m3onSurface","#e5ebef"},{"m3primary","#afd4f0"},{"m3onPrimary","#0a2a3c"},{"m3outline","#8a9399"},{"m3outlineVariant","#2c3338"},{"m3onSurfaceVariant","#b5bec4"},{"m3error","#ffb4ab"}};
static readonly SortedDictionary<string,string> C=VsCodeThemePolicy.Map(Pal)!;
static string? Apply(string t){return VsCodeThemePolicy.TryApplyColors(t,C,out var r,out _)?r:null;}
static string? Remove(string t){return VsCodeThemePolicy.TryRemove(t,out var r,out _)?r:null;}
static string? Raw(string t){return VsCodeThemePolicy.TryGetValue(t,out var raw,out _)?(raw??"<absent>"):"<error>";}
// activate then deactivate (absent key -> remove) must give the original back
static bool RoundTrip(string t){var a=Apply(t);if(a==null||a==t)return false;if(Raw(a)=="<absent>"||Raw(a)=="<error>")return false;return Remove(a)==t;}
static bool Same(byte[] a,byte[] b)=>a.AsSpan().SequenceEqual(b);
static byte[] Bytes(string s)=>new UTF8Encoding(false).GetBytes(s);
static int Main(){var r=new Dictionary<string,bool>();
const string K="\"workbench.colorCustomizations\"";
r["roundBraces"]=RoundTrip("{}");
r["newlineBracesRemoveEmpty"]=Apply("{\n}") is string nb&&Remove(nb) is "{}" or "{\n}";
r["roundTrailingNewline"]=RoundTrip("{\n  \"a\": 1\n}\n");
r["roundCrlf"]=RoundTrip("{\r\n  \"a\": 1,\r\n  \"b\": [1, 2]\r\n}\r\n");
r["crlfKept"]=Apply("{\r\n  \"a\": 1\r\n}\r\n")!.Replace("\r\n","").IndexOf('\n')<0;
r["roundTrailingComma"]=RoundTrip("{\n  \"a\": 1,\n  \"b\": 2,\n}\n");
r["roundBom"]=RoundTrip("\uFEFF{\n  \"a\": 1\n}\n");
r["bomKept"]=Apply("\uFEFF{}")!.StartsWith("\uFEFF{");
r["roundComments"]=RoundTrip("// top { \"x\"\n{\n  /* { \" */ \"a\": \"}\", // end {\n  \"b\": 1 // last\n}\n// tail\n");
r["roundTabs"]=RoundTrip("{\n\t\"a\": 1\n}");
r["roundOneLine"]=RoundTrip("{\"a\":1}");
r["emptyFile"]=Apply("") is string e&&Raw(e)!="<absent>"&&Raw(e)!="<error>"&&e.StartsWith("{");
r["emptyBomFile"]=Apply("\uFEFF") is string eb&&eb.StartsWith("\uFEFF{")&&Raw(eb)!="<absent>";
r["whitespaceOnly"]=Apply("  \r\n") is string w&&w.Contains("workbench");
// key lookup
r["keyInComment"]=Raw("{\n  // "+K+": {\"a\":1}\n  \"a\": 1\n}")=="<absent>";
r["keyInBlockComment"]=Raw("{ /* "+K+": 1 */ \"a\": 1 }")=="<absent>";
r["keyInString"]=Raw("{\"note\": \"x "+K.Replace("\"","\\\"")+" y\", \"a\": 1}")=="<absent>";
r["keyAsValue"]=Raw("{\"a\": "+K+"}")=="<absent>";
r["nestedKeyIgnored"]=Raw("{\"other\": {"+K+": {\"a\":\"#fff\"}}}")=="<absent>";
r["nestedKeyUntouched"]=Apply("{\"other\": {"+K+": {\"a\":\"#fff\"}}}") is string n&&n.StartsWith("{\"other\": {"+K+": {\"a\":\"#fff\"}},");
r["inArrayIgnored"]=Raw("{\"l\": ["+K+", {"+K+": 1}]}")=="<absent>";
r["escapedKeyMatches"]=Raw("{\"workbench\\u002ecolorCustomizations\": {\"x\": 1}}")=="{\"x\": 1}";
// replace existing
string existing="// c\n{\n  \"a\": 1,\n  "+K+": {\n    \"old\": \"#000000\" // keep me\n  },\n  \"z\": [1,2,],\n}\n";
r["rawValueExact"]=Raw(existing)=="{\n    \"old\": \"#000000\" // keep me\n  }";
var replaced=Apply(existing)!;
r["replaceKeepsRest"]=replaced.StartsWith("// c\n{\n  \"a\": 1,\n  "+K+": {")&&replaced.EndsWith("},\n  \"z\": [1,2,],\n}\n")&&!replaced.Contains("\"old\"");
r["replaceNoDuplicate"]=replaced.Split("workbench.colorCustomizations").Length==2;
r["restoreRawExact"]=VsCodeThemePolicy.TrySetRaw(replaced,Raw(existing)!,out var back,out _)&&back==existing;
r["idempotent"]=Apply(replaced)==replaced;
r["removeExisting"]=Remove(existing)=="// c\n{\n  \"a\": 1,\n  \"z\": [1,2,],\n}\n";
r["removeLast"]=Remove("{\n  \"a\": 1,\n  "+K+": {}\n}\n")=="{\n  \"a\": 1\n}\n";
r["removeAbsentUnchanged"]=Remove("{ \"a\": 1 }")=="{ \"a\": 1 }";
// invalid input refused
foreach(var (name,bad) in new[]{("badOpen","[1,2]"),("badTrunc","{\"a\": 1"),("badComma","{\"a\" 1}"),("badStr","{\"a\": \"x}"),("badComment","{ /* x \"a\": 1 }"),("badAfter","{} {}"),("badValue","{\"a\": nope}"),("badKey","{a: 1}"),("badNumber","{\"a\": 1.2.3}"),("badEscape","{\"a\": \"\\q\"}"),("scalar","42")}){
 r[name]=!VsCodeThemePolicy.TryApplyColors(bad,C,out var rr,out var err)&&rr==bad&&err.Length>0;}
r["badRemoveRefused"]=!VsCodeThemePolicy.TryRemove("{\"a\":",out _,out _);
// colour mapping
r["mapNull"]=VsCodeThemePolicy.Map(new Dictionary<string,string>{{"m3surface","#101518"}})==null&&VsCodeThemePolicy.Map(null)==null&&VsCodeThemePolicy.Map(new Dictionary<string,string>{{"m3surface","red"},{"m3onSurface","#fff"},{"m3primary","#00f"}})==null;
r["mapKeys"]=new[]{"editor.background","editor.foreground","sideBar.background","activityBar.background","statusBar.background","titleBar.activeBackground","tab.activeBackground","panel.background","terminal.background","list.activeSelectionBackground","focusBorder","button.background","input.background"}.All(C.ContainsKey);
r["mapOwned"]=C.Keys.All(k=>Array.IndexOf(VsCodeThemePolicy.OwnedKeys,k)>=0);
r["mapAccentIsPrimary"]=C["focusBorder"]=="#afd4f0"&&C["button.background"]=="#afd4f0"&&C["editor.background"]=="#101518"&&C["editor.foreground"]=="#e5ebef";
r["mapValuesHex"]=C.Values.All(v=>(v.Length==7||v.Length==9)&&v[0]=='#'&&v.Skip(1).All(Uri.IsHexDigit));
r["mapContrastText"]=VsCodeThemePolicy.Contrast(C["editor.foreground"],C["editor.background"])>=4.5&&VsCodeThemePolicy.Contrast(C["sideBar.foreground"],C["sideBar.background"])>=4.5&&VsCodeThemePolicy.Contrast(C["statusBar.foreground"],C["statusBar.background"])>=4.5&&VsCodeThemePolicy.Contrast(C["button.foreground"],C["button.background"])>=4.5;
var weak=VsCodeThemePolicy.Map(new Dictionary<string,string>{{"m3surface","#202020"},{"m3onSurface","#f0f0f0"},{"m3primary","#303030"},{"m3onSurfaceVariant","#252525"},{"m3outline","#262626"}})!;
r["mapContrastForced"]=VsCodeThemePolicy.Contrast(weak["sideBar.foreground"],weak["sideBar.background"])>=4.5&&VsCodeThemePolicy.Contrast(weak["button.foreground"],weak["button.background"])>=4.5;
var light=VsCodeThemePolicy.Map(new Dictionary<string,string>{{"m3surface","#fff8f6"},{"m3onSurface","#261916"},{"m3primary","#895143"}})!;
r["mapLightDerived"]=light["editor.background"]=="#fff8f6"&&VsCodeThemePolicy.Contrast(light["statusBar.foreground"],light["statusBar.background"])>=4.5;
r["mapMissingErrorSkipped"]=!light.ContainsKey("errorForeground")&&C.ContainsKey("errorForeground");
r["mapOutputParses"]=Apply("{}") is string o&&Raw(o)!="<error>";
r["quoteEscapes"]=VsCodeThemePolicy.Quote("a\"b\\c\n")=="\"a\\\"b\\\\c\\u000a\"";
r["looksOwned"]=VsCodeThemePolicy.LooksOwned(Apply("{}")!)&&!VsCodeThemePolicy.LooksOwned("{"+K+": {\"mine\": 1}}")&&!VsCodeThemePolicy.LooksOwned("{}")&&!VsCodeThemePolicy.LooksOwned("{"+K+": {}}");

// ---------- file layer (temp folders only, never the real settings.json) ----------
string root=Path.Combine(Path.GetTempPath(),"vscode-theme-test-"+Guid.NewGuid().ToString("N"));
try{
 string Settings(string app,string folder="Code")=>Path.Combine(app,folder,"User","settings.json");
 (VsCodeTheme,string,string) Env(string name,byte[]? content,string folder="Code"){string app=Path.Combine(root,name,"app"),bk=Path.Combine(root,name,"backup");Directory.CreateDirectory(Path.GetDirectoryName(Settings(app,folder))!);if(content!=null)File.WriteAllBytes(Settings(app,folder),content);return(new VsCodeTheme(app,bk),Settings(app,folder),bk);}
 var absent=new VsCodeTheme(Path.Combine(root,"none","app"),Path.Combine(root,"none","bk"));
 r["absentNotAvailable"]=!absent.Available()&&absent.Apply(Pal)=="absent"&&!Directory.Exists(Path.Combine(root,"none"));
 foreach(var (name,content) in new (string,byte[])[]{("empty",Array.Empty<byte>()),("braces",Bytes("{}")),("bomCrlf",new byte[]{0xEF,0xBB,0xBF}.Concat(Bytes("{\r\n  // c {\"\r\n  \"a\": 1,\r\n}\r\n")).ToArray()),("existingKey",Bytes(existing))}){
  var (svc,file,bk)=Env("rt-"+name,content);
  string a=svc.Apply(Pal);byte[] active=File.ReadAllBytes(file);string again=svc.Apply(Pal);
  bool ok=a=="ok"&&again=="ok"&&!Same(active,content)&&Same(File.ReadAllBytes(file),active);
  svc.Restore();
  r["fileRoundTrip-"+name]=ok&&Same(File.ReadAllBytes(file),content)&&!svc.HasState();}
 {var (svc,file,_)=Env("created",null);r["fileCreatedThenRemoved"]=svc.Apply(Pal)=="ok"&&File.Exists(file)&&(svc.Restore()==""&&!File.Exists(file));}
 {var (svc,file,_)=Env("nochange",Bytes("{\"a\":1}"));svc.Apply(Pal);var t1=File.GetLastWriteTimeUtc(file);System.Threading.Thread.Sleep(30);svc.Apply(Pal);r["noRewriteWhenSame"]=File.GetLastWriteTimeUtc(file)==t1;}
 {byte[] bad=Bytes("{\"a\": ");var (svc,file,bk)=Env("invalid",bad);r["invalidRefused"]=svc.Apply(Pal)=="refused"&&Same(File.ReadAllBytes(file),bad)&&!svc.HasState()&&!File.Exists(file+".winlestia.tmp");}
 {byte[] bad=new byte[]{0x7B,0xFF,0xFE,0x7D};var (svc,file,_)=Env("badutf8",bad);r["invalidUtf8Refused"]=svc.Apply(Pal)=="refused"&&Same(File.ReadAllBytes(file),bad);}
 {var (svc,file,_)=Env("badpalette",Bytes("{}"));r["badPaletteRefused"]=svc.Apply(new Dictionary<string,string>())=="refused"&&Same(File.ReadAllBytes(file),Bytes("{}"));}
 {byte[] orig=Bytes("{\n  \"a\": 1,\n  "+K+": {\"old\": \"#111111\"}\n}\n");var (svc,file,_)=Env("edited",orig);svc.Apply(Pal);
  string cur=File.ReadAllText(file).Replace("\"a\": 1","\"a\": 2,\n  \"added\": true");File.WriteAllText(file,cur);svc.Restore();
  string final=File.ReadAllText(file);r["restoreKeepsOtherEdits"]=final.Contains("\"a\": 2")&&final.Contains("\"added\": true")&&final.Contains("{\"old\": \"#111111\"}")&&!final.Contains("editor.background");}
 {byte[] orig=Bytes("{\n  \"a\": 1\n}\n");var (svc,file,_)=Env("edited2",orig);svc.Apply(Pal);File.WriteAllText(file,File.ReadAllText(file).Replace("\"a\": 1","\"a\": 5"));svc.Restore();
  r["restoreAbsentKeyKeepsEdits"]=File.ReadAllText(file)=="{\n  \"a\": 5\n}\n";}
 {byte[] orig=Bytes("{\"a\":1}");var (svc,file,bk)=Env("keepbackup",orig);svc.Apply(Pal);var copy1=File.ReadAllBytes(Path.Combine(bk,"stable.settings.json.bak"));
  var other=new Dictionary<string,string>(Pal){["m3primary"]="#ff8800"};svc.Apply(other);var copy2=File.ReadAllBytes(Path.Combine(bk,"stable.settings.json.bak"));
  r["backupNotOverwritten"]=Same(copy1,orig)&&Same(copy1,copy2)&&File.ReadAllText(Path.Combine(bk,"stable.state.json")).Contains("\"RawValue\":null")&&File.ReadAllText(file).Contains("#ff8800");
  svc.Restore();r["paletteChangeThenRestore"]=Same(File.ReadAllBytes(file),orig);}
 {byte[] orig=Bytes("{\n  \"a\": 1\n}\n");var (svc,file,bk)=Env("nobackup",orig);svc.Apply(Pal);Directory.Delete(bk,true);svc.Restore();r["noBackupRemovesOurKey"]=Same(File.ReadAllBytes(file),orig);}
 {byte[] user=Bytes("{\n  "+K+": {\"mine\": \"#123456\"}\n}\n");var (svc,file,_)=Env("nobackupmine",user);svc.Restore();r["noBackupKeepsForeignKey"]=Same(File.ReadAllBytes(file),user);}
 {var (svc,file,bk)=Env("restoreinvalid",Bytes("{}"));svc.Apply(Pal);var broken=Bytes("{\"a\": ");File.WriteAllBytes(file,broken);r["restoreInvalidRefused"]=svc.Restore()=="refused"&&Same(File.ReadAllBytes(file),broken)&&svc.HasState();}
 {string app=Path.Combine(root,"both","app"),bk=Path.Combine(root,"both","bk");foreach(var f in new[]{"Code","Code - Insiders"}){Directory.CreateDirectory(Path.Combine(app,f,"User"));File.WriteAllBytes(Settings(app,f),Bytes("{\"x\":1}"));}
  var svc=new VsCodeTheme(app,bk);svc.Apply(Pal);bool both=File.ReadAllText(Settings(app)).Contains("editor.background")&&File.ReadAllText(Settings(app,"Code - Insiders")).Contains("editor.background");svc.Restore();
  r["insidersToo"]=both&&File.ReadAllText(Settings(app))=="{\"x\":1}"&&File.ReadAllText(Settings(app,"Code - Insiders"))=="{\"x\":1}";}
 {byte[] orig=Bytes("{\"a\":1}\n");var (svc,file,bk)=Env("uninstall",orig);svc.Apply(Pal);var fresh=new VsCodeTheme(Path.Combine(root,"uninstall","app"),bk);string res=fresh.Restore();r["uninstallRestore"]=res==""&&Same(File.ReadAllBytes(file),orig)&&!fresh.HasState();
  r["uninstallNoState"]=new VsCodeTheme(Path.Combine(root,"uninstall","app"),bk).Restore()==""&&new VsCodeTheme(Path.Combine(root,"nothing"),Path.Combine(root,"nothing2")).Restore()=="";}
 {var (svc,file,_)=Env("debounce",Bytes("{}"));for(int i=0;i<5;i++)svc.Schedule(new Dictionary<string,string>(Pal){["m3primary"]="#00000"+i},150);
  r["debounceNotImmediate"]=!File.ReadAllText(file).Contains("focusBorder");System.Threading.Thread.Sleep(900);r["debounceLast"]=File.ReadAllText(file).Contains("\"focusBorder\": \"#000004\"");
  var (svc2,file2,_)=Env("debounce2",Bytes("{}"));svc2.Schedule(Pal,200);svc2.Restore();System.Threading.Thread.Sleep(600);r["restoreCancelsPending"]=File.ReadAllText(file2)=="{}";}
 }catch(Exception ex){Console.WriteLine("EXCEPTION "+ex);r["exception"]=false;}
finally{try{Directory.Delete(root,true);}catch{}}
foreach(var kv in r.OrderBy(k=>k.Key))if(!kv.Value)Console.WriteLine("FAIL "+kv.Key);
Console.WriteLine($"{r.Count(k=>k.Value)}/{r.Count} passed");return r.All(k=>k.Value)?0:1;}
}
