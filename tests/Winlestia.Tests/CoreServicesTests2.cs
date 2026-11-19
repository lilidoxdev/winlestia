using System;
using System.Windows;
using PanneauxGlass;

// Règles pures extraites du Coordinator (lot 7B) : lancement, onglets rapides, disposition, état partagé.
public class CoreServicesTests2
{
    [Theory]
    [InlineData("File Explorer.lnk", true)]
    [InlineData(@"C:\x\Explorateur de fichiers.lnk", true)]
    [InlineData("EXPLORER.EXE", true)]
    [InlineData("notepad.exe", false)]
    [InlineData(null, false)]
    public void IsPlainExplorer_reconnait_les_variantes(string? target, bool expected)
        => Assert.Equal(expected, LaunchRules.IsPlainExplorer(target));

    [Theory]
    [InlineData("apps", "Applications")]
    [InlineData("games", "Jeux")]
    [InlineData("projects", "Projets")]
    [InlineData("documents", "Dossiers")]
    public void SectionFor_classe_par_panneau(string board, string section) => Assert.Equal(section, LaunchRules.SectionFor(board));

    [Fact]
    public void WebTarget_complete_les_adresses_et_encode_les_recherches()
    {
        Assert.Equal("https://exemple.test", LaunchRules.WebTarget("exemple.test", true));
        Assert.Equal("ftp://h", LaunchRules.WebTarget("ftp://h", true));
        Assert.Equal("https://duckduckgo.com/?q=a%20b", LaunchRules.WebTarget("a b", false));
    }

    [Fact]
    public void Cibles_shell_et_screenclip_sont_detectees_sans_casse()
    {
        Assert.True(LaunchRules.IsShellTarget("SHELL:Downloads"));
        Assert.False(LaunchRules.IsShellTarget("C:\a"));
        Assert.True(LaunchRules.IsScreenClip("MS-ScreenClip:"));
    }

    [Theory]
    [InlineData("glass:home", "Home")]
    [InlineData("GLASS: Palette ", "Palette")]
    [InlineData("glass:inconnu", "Unknown")]
    [InlineData("https://a", "Unknown")]
    [InlineData(null, "Unknown")]
    public void QuickTab_Parse(string? target, string expected) => Assert.Equal(expected, QuickTabRules.Parse(target).ToString());

    [Fact]
    public void QuickTab_IsInternal_exige_le_prefixe()
    {
        Assert.True(QuickTabRules.IsInternal("Glass:pc"));
        Assert.False(QuickTabRules.IsInternal("ms-settings:"));
    }

    [Fact]
    public void Layout_FitsBoth_depend_de_la_largeur_en_unites_wpf()
    {
        Assert.True(LayoutRules.FitsBoth(3400, 2));
        Assert.False(LayoutRules.FitsBoth(1699, 1));
    }

    [Fact]
    public void Layout_Detailed_plafonne_la_marge_et_donne_quatre_cibles()
    {
        var layout = LayoutRules.Detailed(new Rect(0, 0, 1000, 800), 200, 10, .5, .5);
        Assert.Equal(45, layout.Panel.X, 3);
        Assert.Equal(4, layout.Targets.Length);
        Assert.True(layout.Panel.Height >= 310);
        Assert.Equal(780, layout.ToolbarWidth, 3);
    }

    [Fact]
    public void ShellState_demarre_ferme_avec_des_dates_minimales()
    {
        var state = new ShellState();
        Assert.False(state.Open);
        Assert.Equal(DateTime.MinValue, state.AwaySince);
        Assert.Equal(DateTime.MinValue, state.EdgeSince);
    }
}
