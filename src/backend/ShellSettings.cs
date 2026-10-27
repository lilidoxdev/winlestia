// language: C# 12, file: ShellSettings.cs, target: Windows 11 / .NET 8, non-destructive shell migration
using System;
using System.IO;
using System.Linq;

namespace PanneauxGlass;
internal static class ShellSettings {
    internal static void Migrate(bool preview){if(preview||Store.State.ShellRevision>=1)return;Store.State.PreviousTheme=Store.State.Theme;Store.State.Theme="shell";Store.State.RailSide="left";Store.State.ReplaceTaskbar=true;Store.State.ShellRevision=1;
        if(Store.State.TaskbarPins.Count==0){Store.State.TaskbarPins.AddRange(Store.State.Favorites.Where(id=>Store.Entries.Any(e=>e.Id==id&&Store.BoardFor(e) is "apps" or "games")).Take(12));string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),@"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");if(Directory.Exists(folder))foreach(string path in Directory.EnumerateFiles(folder,"*.lnk").Take(12))if(!Store.State.TaskbarPins.Contains(path))Store.State.TaskbarPins.Add(path);}Store.Save();
    }
}
