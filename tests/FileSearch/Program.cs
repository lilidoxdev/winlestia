// C#12 / FileSearch: normalisation and escaping of the index query (pure functions, no index needed).
using System;using System.Collections.Generic;using PanneauxGlass;
class Test{
static int Main(){var r=new Dictionary<string,bool>();string t,sql;
string profile=@"C:\Users\Moi";
r["shortRefused"]=!FileQuery.TryNormalize("a",out _)&&!FileQuery.TryNormalize("  a ",out _)&&!FileQuery.TryNormalize(null,out _)&&!FileQuery.TryNormalize("",out _);
r["twoCharsOk"]=FileQuery.TryNormalize(" ab ",out t)&&t=="ab";
r["blanksCollapsed"]=FileQuery.TryNormalize("a \t  b\r\nc",out t)&&t=="a b c";
r["controlBecomesBlank"]=FileQuery.TryNormalize("ab\0cd",out t)&&t=="ab cd";
r["tooLongRefused"]=!FileQuery.TryNormalize(new string('x',FileQuery.MaxChars+1),out _)&&FileQuery.TryNormalize(new string('x',FileQuery.MaxChars),out _);
r["specialsBecomeBlanks"]=FileQuery.NeutraliseContains("a\"b*c%d_e[f]g")=="a b c d e f g";
r["plainUntouched"]=FileQuery.NeutraliseContains("rapport 2024")=="rapport 2024";
r["quoteDoubled"]=FileQuery.TryBuild("it's",profile,out sql)&&sql.EndsWith("CONTAINS(System.ItemNameDisplay,'\"it''s*\"')");
r["injectionStaysInLiteral"]=FileQuery.TryBuild("x' OR 1=1 --",profile,out sql)&&sql.EndsWith("'\"x'' OR 1=1 --*\"')");
r["wildcardsNeverReachSql"]=FileQuery.TryBuild("50%_[a]*\"",profile,out sql)&&sql.EndsWith("'\"50 a*\"')");
r["onlySpecialsRefused"]=!FileQuery.TryBuild("%_%",profile,out _)&&!FileQuery.TryBuild("[]*",profile,out _);
r["scopeIsProfile"]=FileQuery.TryBuild("abc",profile,out sql)&&sql.Contains(@"SCOPE='file:C:\Users\Moi'");
r["scopeQuoteDoubled"]=FileQuery.TryBuild("abc",@"C:\Users\O'Brien\",out sql)&&sql.Contains(@"SCOPE='file:C:\Users\O''Brien'");
r["top20"]=FileQuery.TryBuild("abc",profile,out sql)&&sql.StartsWith("SELECT TOP 20 System.ItemPathDisplay, System.ItemNameDisplay FROM SystemIndex WHERE ");
r["relativeProfileRefused"]=!FileQuery.TryBuild("abc",@"Users\Moi",out _)&&!FileQuery.TryBuild("abc","",out _)&&!FileQuery.TryBuild("abc",null,out _);
r["shortTermNoSql"]=!FileQuery.TryBuild("a",profile,out sql)&&sql=="";
r["exeUpper"]=FileQuery.IsExecutable(@"C:\a\X.EXE");r["doubleExt"]=FileQuery.IsExecutable(@"C:\a\setup.pdf.exe");r["pdfSafe"]=!FileQuery.IsExecutable(@"C:\a\a.pdf");r["noExtSafe"]=!FileQuery.IsExecutable(@"C:\a\readme");r["lnk"]=FileQuery.IsExecutable(@"C:\a\x.lnk");r["ps1Upper"]=FileQuery.IsExecutable(@"C:\a\x.PS1");
int ok=0;foreach(var v in r.Values)if(v)ok++;foreach(var k in r)if(!k.Value)Console.WriteLine("FAIL "+k.Key);Console.WriteLine("TOTAL "+ok+"/"+r.Count);return ok==r.Count?0:1;}
}
