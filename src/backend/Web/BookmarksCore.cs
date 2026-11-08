// C# 12 / .NET 8 / Windows 11. GPL-3.0-or-later. Favoris des navigateurs : détection, lecture, favicons, suppression Chromium.
// Aucune dépendance au reste de l'application (testable seul). Ne journalise jamais d'URL ni de titre : seulement des comptes.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;

namespace PanneauxGlass;

internal sealed class BrowserInfo
{
    public string Id = "", Name = "", Exe = "", UserData = "", ProfileDir = "", InstallDir = "";
    public bool Chromium, Installed, IsDefault;
    public string[] Processes = Array.Empty<string>();
    public string BookmarksFile => Chromium ? Path.Combine(ProfileDir, "Bookmarks") : Path.Combine(ProfileDir, "places.sqlite");
}

internal sealed class BNode
{
    public string Id = "", Title = "", Url = "", Icon = "";
    public bool Folder;
    public List<BNode> Children = new();
}

internal static partial class BookmarksCore
{

    const int MaxNodes = 30000, MaxDepth = 24;

    static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        Programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            ProgramsX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

    internal static string WorkRoot => Path.Combine(Local, "Winlestia", "Bookmarks");

    // ---- Utilitaires ------------------------------------------------------------------------------------------
    static byte[] ReadShared(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        input.CopyTo(memory);
        return memory.ToArray();
    }

    static bool CopyShared(string source, string target)
    {
        try
        {
            if (!File.Exists(source))
                return false;
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
            input.CopyTo(output);
            return true;
        }
        catch { return false; }
    }

    static string Hash(string text) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    static SqliteConnection Open(string file)
    {
        var connection = new SqliteConnection("Data Source=" + file + ";Mode=ReadWriteCreate;Pooling=False");
        connection.Open();
        return connection;
    }

    // Copie places.sqlite (+ -wal) dans un dossier de travail ; l'original n'est jamais ouvert en SQLite.
    static string? CopyDatabase(string source, string workDir)
    {
        Directory.CreateDirectory(workDir);
        string name = Path.GetFileName(source), target = Path.Combine(workDir, name);
        if (!CopyShared(source, target))
            return null;
        CopyShared(source + "-wal", target + "-wal");
        return target;
    }

    internal static int CountUrls(IEnumerable<BNode> nodes)
    {
        int n = 0;
        foreach (var x in nodes)
            n += x.Folder ? CountUrls(x.Children) : 1;
        return n;
    }

}
