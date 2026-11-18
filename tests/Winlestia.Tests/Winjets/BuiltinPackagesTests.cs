// C#12 / Tous les paquets Winjets inclus (src/winjets/builtin) passent le vrai validateur, sans liste codée en dur.
using System;
using System.IO;
using System.Linq;
using PanneauxGlass.Winjets;
using Xunit;
namespace Winlestia.Tests.Winjets;

public class BuiltinPackagesTests
{
    static string BuiltinDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Winlestia.sln")))
            dir = dir.Parent;
        string root = dir?.FullName ?? throw new InvalidOperationException("Racine du dépôt introuvable.");
        return Path.Combine(root, "src", "winjets", "builtin");
    }

    [Fact]
    public void EveryBuiltinFolderIsValid()
    {
        string[] dirs = Directory.GetDirectories(BuiltinDir());
        Assert.NotEmpty(dirs);
        foreach (string dir in dirs)
        {
            string id = Path.GetFileName(dir);
            using var v = WinjetValidator.ValidateDirectory(dir, WinjetValidator.CurrentVersion());
            Assert.True(v.Ok, id + " : " + string.Join(" | ", v.Errors));
        }
    }

    [Theory]
    [InlineData("winlestia.lecteur")]
    [InlineData("winlestia.systeme")]
    [InlineData("winlestia.marches")]
    [InlineData("winlestia.horloge")]
    [InlineData("winlestia.calendrier")]
    [InlineData("winlestia.batterie")]
    [InlineData("winlestia.stockage")]
    [InlineData("winlestia.fichiers")]
    [InlineData("winlestia.notes")]
    [InlineData("winlestia.agenda")]
    [InlineData("winlestia.meteo")]
    public void ExpectedBuiltinIsShipped(string id)
        => Assert.Contains(id, Directory.GetDirectories(BuiltinDir()).Select(Path.GetFileName));
}
