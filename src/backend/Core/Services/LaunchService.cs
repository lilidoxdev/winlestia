// language: C# 12, file: LaunchService.cs, target: Windows 11 / .NET 8 WPF, lancement des accès, raccourcis Windows et recherche web
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PanneauxGlass;

internal interface ILaunchService
{
    void Launch(Entry entry);
    void Reveal(Entry entry);
    void LaunchTarget(string target);
    void LaunchShortcut(WindowsShortcut shortcut);
    void WebSearch(string value, bool address);
    void AddPath(string path, string board);
    void Rescue();
}

internal sealed class LaunchService : ILaunchService
{
    readonly Action<string> message;
    readonly Action collapse, collapseNow, open;
    // Les retours vers l'espace (message, repli, ouverture) restent des délégués : le service ne connaît aucune fenêtre.
    internal LaunchService(Action<string> message, Action collapse, Action collapseNow, Action open)
    {
        this.message = message;
        this.collapse = collapse;
        this.collapseNow = collapseNow;
        this.open = open;
    }
    public void Launch(Entry entry)
    {
        try
        {
            if (Store.State.FilesDefaultHandler && LaunchRules.IsPlainExplorer(entry.Target) && File.Exists(FilesDefaultHandler.ExePath))
            {
                FilesDefaultHandler.Open();
                Done(entry);
                return;
            }
            if (LaunchRules.IsShellTarget(entry.Target))
                Process.Start(new ProcessStartInfo("explorer.exe", entry.Target) { UseShellExecute = true });
            else
            {
                var start = new ProcessStartInfo(entry.Target) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Normal };
                if (File.Exists(entry.Target))
                    start.WorkingDirectory = Path.GetDirectoryName(entry.Target);
                Process.Start(start);
            }
            Done(entry);
        }
        catch (Exception ex) { message("L’accès « " + entry.Name + " » n’a pas pu s’ouvrir.\n" + ex.Message); }
    }
    void Done(Entry entry)
    {
        Store.RecordLaunch(entry.Id);
        Store.Save();
        collapse();
    }
    public void Reveal(Entry entry)
    {
        try
        {
            if (Directory.Exists(entry.Target))
                Process.Start("explorer.exe", "\"" + entry.Target + "\"");
            else if (File.Exists(entry.Target))
                Process.Start("explorer.exe", LaunchRules.SelectArguments(entry.Target));
            else
                message("Cet accès utilise un protocole de lancement Windows.");
        }
        catch (Exception ex) { message(ex.Message); }
    }
    public void LaunchTarget(string target)
    {
        collapseNow();
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) { message(ex.Message); }
    }
    public async void LaunchShortcut(WindowsShortcut shortcut)
    {
        collapseNow();
        try
        {
            if (LaunchRules.IsScreenClip(shortcut.Target))
                await Task.Delay(LaunchRules.ScreenClipDelayMs);
            Process.Start(new ProcessStartInfo(shortcut.Target) { Arguments = shortcut.Arguments, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            open();
            message("Le raccourci « " + shortcut.Name + " » n’a pas pu s’ouvrir.\n" + ex.Message);
        }
    }
    public void WebSearch(string value, bool address)
    {
        try
        {
            Process.Start(new ProcessStartInfo(LaunchRules.WebTarget(value, address)) { UseShellExecute = true });
            collapseNow();
        }
        catch (Exception ex) { message("La recherche n’a pas pu s’ouvrir.\n" + ex.Message); }
    }
    public void AddPath(string path, string board)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
            return;
        var existing = Store.Entries.FirstOrDefault(e => e.Target.Equals(path, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            Store.State.BoardOverrides[existing.Id] = board;
            Store.State.Hidden.Remove(existing.Id);
        }
        else
        {
            var entry = new Entry
            {
                Id = "custom:" + Guid.NewGuid().ToString("N"),
                Name = Directory.Exists(path) ? new DirectoryInfo(path).Name : Path.GetFileNameWithoutExtension(path),
                Target = path,
                IconPath = path,
                Section = LaunchRules.SectionFor(board),
                Category = "Mes accès"
            };
            Store.State.Custom.Add(entry);
            Store.State.BoardOverrides[entry.Id] = board;
            Store.Entries.Add(entry);
        }
        Store.Save();
    }
    public void Rescue()
    {
        string executable = Path.Combine(Store.Root, "SECOURS_BUREAU.exe");
        if (!File.Exists(executable))
        {
            message("Le secours indépendant est absent.\nDocuments\\SECOURS_BUREAU.exe peut également être utilisé.");
            return;
        }
        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
    }
}
