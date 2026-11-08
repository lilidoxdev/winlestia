// C# 12 / .NET 8 / Windows 11. Favoris des navigateurs : détection des navigateurs installés
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

internal static partial class BookmarksCore
{
    // ---- Détection ---------------------------------------------------------------------------------------------
    static string FromAppPaths(string exe)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exe);
                    if (key?.GetValue(null) is string path)
                    {
                        path = path.Trim('"');
                        if (File.Exists(path))
                            return path;
                    }
                }
                catch (Exception)
                {
                    // Clé App Paths absente : ce navigateur n'est pas installé.
                }
            }
        return "";
    }

    static string First(params string[] paths) => paths.FirstOrDefault(File.Exists) ?? "";

    public static List<BrowserInfo> Detect()
    {
        var list = new List<BrowserInfo>();
        void Add(string id, string name, string exe, bool chromium, string userData, params string[] procs)
        {
            var b = new BrowserInfo { Id = id, Name = name, Exe = exe, Chromium = chromium, UserData = userData, Processes = procs };
            try
            {
                if (exe.Length > 0)
                    b.InstallDir = Path.GetDirectoryName(exe) ?? "";
                if (chromium && Directory.Exists(userData))
                {
                    string profile = "Default";
                    if (id is not ("opera" or "operagx"))
                    {
                        try
                        {
                            string state = Path.Combine(userData, "Local State");
                            if (File.Exists(state))
                            {
                                using var doc = JsonDocument.Parse(ReadShared(state));
                                if (doc.RootElement.TryGetProperty("profile", out var p) && p.TryGetProperty("last_used", out var last)
                                    && last.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(last.GetString()))
                                    profile = last.GetString()!;
                            }
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                        {
                            Store.Log("BookmarksCore.Add : " + ex.GetType().Name + ": " + ex.Message);
                        }
                        if (profile.IndexOfAny(new[] { '\\', '/' }) >= 0 || profile.Contains(".."))
                            profile = "Default";
                        b.ProfileDir = Path.Combine(userData, profile);
                    }
                    else
                        b.ProfileDir = userData;
                    b.Installed = exe.Length > 0 && File.Exists(b.BookmarksFile);
                }
                else if (!chromium)
                {
                    b.ProfileDir = FirefoxProfile();
                    b.Installed = exe.Length > 0 && b.ProfileDir.Length > 0 && File.Exists(b.BookmarksFile);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                Store.Log("BookmarksCore.Add : " + ex.GetType().Name + ": " + ex.Message);
            }
            list.Add(b);
        }
        Add("edge", "Microsoft Edge", First(FromAppPaths("msedge.exe"), Path.Combine(ProgramsX86,
            @"Microsoft\Edge\Application\msedge.exe"),
            Path.Combine(Programs, @"Microsoft\Edge\Application\msedge.exe")), true, Path.Combine(Local, @"Microsoft\Edge\User Data"),
            "msedge");
        Add("chrome", "Google Chrome", First(FromAppPaths("chrome.exe"), Path.Combine(Programs, @"Google\Chrome\Application\chrome.exe"),
            Path.Combine(ProgramsX86, @"Google\Chrome\Application\chrome.exe"), Path.Combine(Local,
            @"Google\Chrome\Application\chrome.exe")), true, Path.Combine(Local, @"Google\Chrome\User Data"), "chrome");
        Add("brave", "Brave", First(FromAppPaths("brave.exe"), Path.Combine(Programs,
            @"BraveSoftware\Brave-Browser\Application\brave.exe"),
            Path.Combine(Local, @"BraveSoftware\Brave-Browser\Application\brave.exe")), true, Path.Combine(Local,
            @"BraveSoftware\Brave-Browser\User Data"), "brave");
        Add("opera", "Opera", First(Path.Combine(Local, @"Programs\Opera\opera.exe"), Path.Combine(Programs, @"Opera\opera.exe"),
            Path.Combine(Local, @"Programs\Opera\launcher.exe")), true, Path.Combine(Roaming, @"Opera Software\Opera Stable"), "opera");
        Add("operagx", "Opera GX", First(Path.Combine(Local, @"Programs\Opera GX\opera.exe"), Path.Combine(Programs,
            @"Opera GX\opera.exe"),
            Path.Combine(Local, @"Programs\Opera GX\launcher.exe")), true, Path.Combine(Roaming, @"Opera Software\Opera GX Stable"),
            "opera");
        Add("vivaldi", "Vivaldi", First(FromAppPaths("vivaldi.exe"), Path.Combine(Local, @"Vivaldi\Application\vivaldi.exe"),
            Path.Combine(Programs, @"Vivaldi\Application\vivaldi.exe")), true, Path.Combine(Local, @"Vivaldi\User Data"), "vivaldi");
        Add("firefox", "Mozilla Firefox", First(FromAppPaths("firefox.exe"), Path.Combine(Programs, @"Mozilla Firefox\firefox.exe"),
            Path.Combine(ProgramsX86, @"Mozilla Firefox\firefox.exe")), false, "", "firefox");
        string def = DefaultBrowserId();
        foreach (var b in list)
            b.IsDefault = b.Id == def;
        return list;
    }

    internal static string BrowserIdFromProgId(string prog)
    {
        // OperaGX avant Opera : « OperaGX » contient aussi « Opera ».
        foreach (var (needle, id) in new[] { ("MSEdge", "edge"), ("Chrome", "chrome"), ("Brave", "brave"), ("Firefox", "firefox"),
            ("Vivaldi", "vivaldi"), ("OperaGX", "operagx"), ("Opera", "opera") })
            if (prog.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return id;
        return "";
    }

    static string DefaultBrowserId()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            string prog = (key?.GetValue("ProgId") as string) ?? "";
            return BrowserIdFromProgId(prog);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            Store.Log("BookmarksCore.DefaultBrowserId : " + ex.GetType().Name + ": " + ex.Message);
        }
        return "";
    }

    static string FirefoxProfile() => FirefoxProfileIn(Path.Combine(Roaming, @"Mozilla\Firefox"));

    // Racine passée en paramètre : profiles.ini se teste sur un dossier temporaire sans toucher au vrai profil Firefox.
    internal static string FirefoxProfileIn(string root)
    {
        try
        {
            string ini = Path.Combine(root, "profiles.ini");
            if (!File.Exists(ini))
                return "";
            var sections = new List<(string name, Dictionary<string, string> values)>();
            foreach (string raw in File.ReadAllLines(ini))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == ';')
                    continue;
                if (line[0] == '[' && line[^1] == ']')
                {
                    sections.Add((line[1..^1], new(StringComparer.OrdinalIgnoreCase)));
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq > 0 && sections.Count > 0)
                    sections[^1].values[line[..eq].Trim()] = line[(eq + 1)..].Trim();
            }
            string Resolve(string path, bool relative) => relative ? Path.GetFullPath(Path.Combine(root, path.Replace('/', '\\')))
                : path.Replace('/', '\\');
            foreach (var s in sections.Where(s => s.name.StartsWith("Install", StringComparison.OrdinalIgnoreCase)))
                if (s.values.TryGetValue("Default", out var d) && d.Length > 0)
                {
                    string dir = Resolve(d, !Path.IsPathRooted(d));
                    if (Directory.Exists(dir))
                        return dir;
                }
            foreach (var s in sections.Where(s => s.name.StartsWith("Profile", StringComparison.OrdinalIgnoreCase)
                && s.values.GetValueOrDefault("Default") == "1"))
                if (s.values.TryGetValue("Path", out var p))
                {
                    string dir = Resolve(p, s.values.GetValueOrDefault("IsRelative", "1") != "0");
                    if (Directory.Exists(dir))
                        return dir;
                }
            foreach (var s in sections.Where(s => s.name.StartsWith("Profile", StringComparison.OrdinalIgnoreCase)))
                if (s.values.TryGetValue("Path", out var p))
                {
                    string dir = Resolve(p, s.values.GetValueOrDefault("IsRelative", "1") != "0");
                    if (File.Exists(Path.Combine(dir, "places.sqlite")))
                        return dir;
                }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Store.Log("BookmarksCore.FirefoxProfile : " + ex.GetType().Name + ": " + ex.Message);
        }
        return "";
    }

    internal static bool IsRunning(BrowserInfo b)
    {
        foreach (string name in b.Processes)
        {
            Process[] found;
            try
            {
                found = Process.GetProcessesByName(name);
            }
            catch { return true; }
            foreach (var p in found)
            {
                try
                {
                    string? file = p.MainModule?.FileName;
                    if (file == null || b.InstallDir.Length == 0 || file.StartsWith(b.InstallDir, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch { return true; } // illisible : par prudence on considère que le navigateur tourne
                finally { p.Dispose(); }
            }
        }
        try
        {
            string lockfile = Path.Combine(b.UserData, "lockfile");
            if (File.Exists(lockfile))
            {
                using var fs = new FileStream(lockfile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }
        }
        catch { return true; } // verrouillé (ou illisible) : le navigateur tourne
        return false;
    }
}
