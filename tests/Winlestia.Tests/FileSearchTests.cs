// C#12 / FileSearch: normalisation and escaping of the index query (pure functions, no index needed).
using System;
using System.Collections.Generic;
using PanneauxGlass;
namespace Winlestia.Tests;
public class FileSearchTests
{
    [Fact]
    public void AllChecks()
    {
        string t, sql;
        string profile = @"C:\Users\Moi";
        Assert.True(!FileQuery.TryNormalize("a", out _) && !FileQuery.TryNormalize("  a ", out _) && !FileQuery.TryNormalize(null, out _)
            && !FileQuery.TryNormalize("", out _), "shortRefused");
        Assert.True(FileQuery.TryNormalize(" ab ", out t) && t == "ab", "twoCharsOk");
        Assert.True(FileQuery.TryNormalize("a \t  b\r\nc", out t) && t == "a b c", "blanksCollapsed");
        Assert.True(FileQuery.TryNormalize("ab\0cd", out t) && t == "ab cd", "controlBecomesBlank");
        Assert.True(!FileQuery.TryNormalize(new string('x', FileQuery.MaxChars + 1), out _)
            && FileQuery.TryNormalize(new string('x', FileQuery.MaxChars), out _), "tooLongRefused");
        Assert.True(FileQuery.NeutraliseContains("a\"b*c%d_e[f]g") == "a b c d e f g", "specialsBecomeBlanks");
        Assert.True(FileQuery.NeutraliseContains("rapport 2024") == "rapport 2024", "plainUntouched");
        Assert.True(FileQuery.TryBuild("it's", profile, out sql) && sql.EndsWith("CONTAINS(System.ItemNameDisplay,'\"it''s*\"')"),
            "quoteDoubled");
        Assert.True(FileQuery.TryBuild("x' OR 1=1 --", profile, out sql) && sql.EndsWith("'\"x'' OR 1=1 --*\"')"),
            "injectionStaysInLiteral");
        Assert.True(FileQuery.TryBuild("50%_[a]*\"", profile, out sql) && sql.EndsWith("'\"50 a*\"')"), "wildcardsNeverReachSql");
        Assert.True(!FileQuery.TryBuild("%_%", profile, out _) && !FileQuery.TryBuild("[]*", profile, out _), "onlySpecialsRefused");
        Assert.True(FileQuery.TryBuild("abc", profile, out sql) && sql.Contains(@"SCOPE='file:C:\Users\Moi'"), "scopeIsProfile");
        Assert.True(FileQuery.TryBuild("abc", @"C:\Users\O'Brien\", out sql) && sql.Contains(@"SCOPE='file:C:\Users\O''Brien'"),
            "scopeQuoteDoubled");
        Assert.True(FileQuery.TryBuild("abc", profile, out sql)
            && sql.StartsWith("SELECT TOP 20 System.ItemPathDisplay, System.ItemNameDisplay FROM SystemIndex WHERE "), "top20");
        Assert.True(!FileQuery.TryBuild("abc", @"Users\Moi", out _) && !FileQuery.TryBuild("abc", "", out _)
            && !FileQuery.TryBuild("abc", null, out _), "relativeProfileRefused");
        Assert.True(!FileQuery.TryBuild("a", profile, out sql) && sql == "", "shortTermNoSql");
        Assert.True(FileQuery.IsExecutable(@"C:\a\X.EXE"), "exeUpper");
        Assert.True(FileQuery.IsExecutable(@"C:\a\setup.pdf.exe"), "doubleExt");
        Assert.True(!FileQuery.IsExecutable(@"C:\a\a.pdf"), "pdfSafe");
        Assert.True(!FileQuery.IsExecutable(@"C:\a\readme"), "noExtSafe");
        Assert.True(FileQuery.IsExecutable(@"C:\a\x.lnk"), "lnk");
        Assert.True(FileQuery.IsExecutable(@"C:\a\x.PS1"), "ps1Upper");
    }
}
