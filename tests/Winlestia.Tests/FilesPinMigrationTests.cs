// C#12 / FilesPinMigration: pin removal on an in-memory Preferences (never the user's state file).
using System;
using System.Collections.Generic;
using System.Linq;
using PanneauxGlass;
namespace Winlestia.Tests;
internal class State // in-memory stand-in for the three Preferences members the migration touches
{
    public bool FilesPinRemoved; public List<string> TaskbarPins = new(); public List<Entry> Custom = new(); public bool Run(string exe, List<string> ids) => FilesPinMigration.Apply(ref FilesPinRemoved, TaskbarPins, Custom, exe, ids);
}
public class FilesPinMigrationTests
{
    [Fact]
    public void AllChecks()
    {
        const string exe = @"C:\App\Winlestia.Files.exe";
        string id = "dock:" + exe;
        State S(params string[] pins)
        {
            var s = new State();
            s.TaskbarPins.AddRange(pins);
            return s;
        }
        Entry E(string i, string name, string target) => new Entry { Id = i, Name = name, Target = target };
        {
            var s = S("a", id, "b");
            s.Custom.Add(E(id, "Winlestia Fichiers", exe));
            var ids = new List<string>();
            bool c = s.Run(exe, ids);
            Assert.True(c && s.TaskbarPins.SequenceEqual(new[] { "a", "b" }), "removesDockPinOnly");
            Assert.True(s.Custom.Count == 0 && ids.SequenceEqual(new[] { id }), "removesOwnCustomEntry");
            Assert.True(s.FilesPinRemoved, "setsFlag");
            var again = s.Run(exe, new List<string>());
            Assert.True(!again && s.TaskbarPins.Count == 2, "idempotent");
        }
        {
            var s = S(exe.ToUpperInvariant(), "x");
            s.Run(exe, new List<string>());
            Assert.True(s.TaskbarPins.SequenceEqual(new[] { "x" }), "removesBareExeCaseInsensitive");
        }
        {
            var s = S("a", "b");
            s.Custom.Add(E("c1", "Autre", @"C:\x.exe"));
            var ids = new List<string>();
            s.Run(exe, ids);
            Assert.True(s.TaskbarPins.SequenceEqual(new[] { "a", "b" }) && s.Custom.Count == 1 && ids.Count == 0 && s.FilesPinRemoved, "otherPinsAndEntriesUntouched");
        }
        {
            var s = S(id);
            s.Custom.Add(E(id, "Renommé par l'utilisateur", exe));
            var ids = new List<string>();
            s.Run(exe, ids);
            Assert.True(s.Custom.Count == 1 && ids.Count == 0 && s.TaskbarPins.Count == 0, "renamedEntryKept");
        }
        {
            var s = S(id);
            s.FilesPinRemoved = true;
            bool c = s.Run(exe, new List<string>());
            Assert.True(!c && s.TaskbarPins.Count == 1, "flagAlreadySetIsNoop");
        }
    }
}
