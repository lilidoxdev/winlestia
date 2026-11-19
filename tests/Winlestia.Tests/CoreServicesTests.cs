using System;
using System.IO;
using System.Linq;
using System.Windows;
using Forms = System.Windows.Forms;
using PanneauxGlass;

// Règles pures extraites du Coordinator : aucune fenêtre, aucun registre, aucun processus.
public class CoreServicesTests
{
    static Entry E(string id, string name, string section = "Applications", string target = "") =>
        new() { Id = id, Name = name, Section = section, Target = target };

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("shell:AppsFolder\\x")]
    [InlineData("https://exemple.test/a.exe")]
    [InlineData(@"\\serveur\partage\a.exe")]
    [InlineData(@"relatif\a.exe")]
    [InlineData(@"C:\dossier\sans-extension")]
    public void TargetGone_ignore_les_cibles_non_verifiables(string target) => Assert.False(CatalogueRules.TargetGone(target));

    [Fact]
    public void TargetGone_vrai_pour_un_fichier_absent_faux_pour_un_fichier_present()
    {
        string file = Path.Combine(Path.GetTempPath(), "core-" + Guid.NewGuid().ToString("N") + ".exe");
        Assert.True(CatalogueRules.TargetGone(file));
        File.WriteAllText(file, "x");
        try { Assert.False(CatalogueRules.TargetGone(file)); }
        finally { File.Delete(file); }
    }

    [Fact]
    public void NewEntries_ecarte_un_id_deja_connu()
    {
        var added = CatalogueRules.NewEntries(new[] { E("1", "Alpha") }, new[] { E("1", "Autre"), E("2", "Beta") });
        Assert.Equal(new[] { "2" }, added.Select(x => x.Id));
    }

    [Fact]
    public void NewEntries_ecarte_meme_nom_meme_section_sans_casse()
    {
        var added = CatalogueRules.NewEntries(new[] { E("1", "Alpha") }, new[] { E("9", "ALPHA") });
        Assert.Empty(added);
    }

    [Fact]
    public void NewEntries_garde_un_meme_nom_dans_une_autre_section()
    {
        var added = CatalogueRules.NewEntries(new[] { E("1", "Alpha") }, new[] { E("9", "Alpha", "Jeux") });
        Assert.Single(added);
    }

    [Fact]
    public void NewEntries_dedoublonne_dans_le_meme_lot()
    {
        var added = CatalogueRules.NewEntries(Array.Empty<Entry>(), new[] { E("1", "Alpha"), E("1", "Alpha") });
        Assert.Single(added);
    }

    [Theory]
    [InlineData(@"C:\Menu\App.lnk", @"C:\Menu\App.lnk", true)]
    [InlineData(@"c:\menu\app.lnk", @"C:\Menu\App.lnk", true)]
    [InlineData(@"C:\Menu\Dossier\App.lnk", @"C:\Menu\Dossier", true)]
    [InlineData(@"C:\Menu\DossierBis\App.lnk", @"C:\Menu\Dossier", false)]
    [InlineData(@"C:\Autre\App.lnk", @"C:\Menu", false)]
    public void IsUnderDeleted_suit_le_chemin_supprime(string target, string deleted, bool expected)
        => Assert.Equal(expected, CatalogueRules.IsUnderDeleted(target, new[] { deleted }));

    [Theory]
    [InlineData(-1, 10, false)]
    [InlineData(10, -1, false)]
    [InlineData(10, 10, false)]
    [InlineData(10, 11, true)]
    [InlineData(11, 10, true)]
    public void PackagesChanged_exige_deux_mesures_valides(int previous, int current, bool expected)
        => Assert.Equal(expected, CatalogueRules.PackagesChanged(previous, current));

    [Theory]
    [InlineData(true, false, 0, true)]
    [InlineData(false, true, 2, true)]
    [InlineData(false, true, 0, false)]
    [InlineData(false, false, 5, false)]
    public void NeedsInventory_combine_paquets_et_fichiers_disparus(bool packages, bool changed, int missing, bool expected)
        => Assert.Equal(expected, CatalogueRules.NeedsInventory(packages, changed, missing));

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void RetryDelay_abandonne_apres_trois_echecs(int failed, bool retries)
        => Assert.Equal(retries, DesktopIconsRules.RetryDelay(failed).HasValue);

    [Fact]
    public void RetryDelay_attend_une_seconde_puis_deux()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), DesktopIconsRules.RetryDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(2), DesktopIconsRules.RetryDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(2), DesktopIconsRules.RetryDelay(3));
    }

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    public void CanGuard_refuse_apercu_arret_et_suspension(bool preview, bool disposed, bool suspended, bool expected)
        => Assert.Equal(expected, DesktopIconsRules.CanGuard(preview, disposed, suspended));

    [Fact]
    public void Reasserts_reste_croissant_pour_survivre_a_un_moteur_de_fond_ecran()
        => Assert.Equal(new[] { 3, 10 }, DesktopIconsRules.Reasserts);

    [Theory]
    [InlineData("right", 1918, 500, true)]
    [InlineData("right", 1915, 500, false)]
    [InlineData("right", 1918, 10, false)]
    [InlineData("right", 1918, 1070, false)]
    [InlineData("left", 3, 500, true)]
    [InlineData("left", 6, 500, false)]
    public void AtEdge_respecte_la_bande_et_les_coins(string side, int x, int y, bool expected)
        => Assert.Equal(expected, TickRules.AtEdge(side, x, y, 0, 1920, 0, 1080));

    [Fact]
    public void Dwell_le_bord_est_inclusif_les_reperes_de_sortie_sont_stricts()
    {
        var now = DateTime.UtcNow;
        Assert.True(TickRules.EdgeDwellReached(now.AddMilliseconds(-150), now));
        Assert.False(TickRules.EdgeDwellReached(now.AddMilliseconds(-149), now));
        Assert.False(TickRules.RailAwayExceeded(now.AddMilliseconds(-650), now));
        Assert.True(TickRules.RailAwayExceeded(now.AddMilliseconds(-651), now));
        Assert.False(TickRules.BoardAwayExceeded(now.AddMilliseconds(-1800), now));
        Assert.True(TickRules.BoardAwayExceeded(now.AddMilliseconds(-1801), now));
    }

    [Fact]
    public void AutoHideSuspended_faux_seulement_quand_tout_autorise_le_repli()
    {
        Assert.False(TickRules.AutoHideSuspended(true, false, false, false, false, true));
        Assert.True(TickRules.AutoHideSuspended(false, false, false, false, false, true));
        Assert.True(TickRules.AutoHideSuspended(true, true, false, false, false, true));
        Assert.True(TickRules.AutoHideSuspended(true, false, true, false, false, true));
        Assert.True(TickRules.AutoHideSuspended(true, false, false, true, false, true));
        Assert.True(TickRules.AutoHideSuspended(true, false, false, false, true, true));
        Assert.True(TickRules.AutoHideSuspended(true, false, false, false, false, false));
    }

    [Fact]
    public void RailPlacement_centre_verticalement_et_colle_au_bord_droit()
    {
        var p = TickRules.RailPlacement("right", 0, 1920, 0, 1000, 1.0, 62, .8);
        Assert.Equal(1920 - 62 - 12, p.X);
        Assert.Equal(800, p.Height);
        Assert.Equal(100, p.Y);
    }

    [Fact]
    public void RailPlacement_bord_gauche_et_echelle_dpi()
    {
        var p = TickRules.RailPlacement("left", 100, 2020, 40, 1000, 2.0, 62, .5);
        Assert.Equal(100 / 2.0 + 12, p.X);
        Assert.Equal(250, p.Height);
        Assert.Equal(40 / 2.0 + (500 - 250) / 2.0, p.Y);
    }

    [Fact]
    public void Inside_inclut_le_bord_haut_gauche_et_exclut_le_bas_droit()
    {
        Assert.True(TickRules.Inside(10, 10, 100, 50, 1.0, 10, 10));
        Assert.True(TickRules.Inside(10, 10, 100, 50, 1.0, 109, 59));
        Assert.False(TickRules.Inside(10, 10, 100, 50, 1.0, 110, 30));
        Assert.False(TickRules.Inside(10, 10, 100, 50, 1.0, 30, 60));
    }

    [Fact]
    public void Inside_applique_le_dpi_de_la_fenetre()
        => Assert.True(TickRules.Inside(10, 10, 100, 50, 1.5, 160, 89));

    [Fact]
    public void CaptureBounds_vide_donne_null()
        => Assert.Null(PreviewExportRules.CaptureBounds(Array.Empty<Rect>()));

    [Fact]
    public void CaptureBounds_englobe_les_fenetres_avec_la_marge()
    {
        var b = PreviewExportRules.CaptureBounds(new[] { new Rect(100, 50, 200, 100), new Rect(400, 300, 100, 100) })!.Value;
        Assert.Equal(80, b.Left);
        Assert.Equal(30, b.Top);
        Assert.Equal(440, b.Width);
        Assert.Equal(390, b.Height);
    }

    [Fact]
    public void PixelSize_arrondit_au_pixel_superieur()
        => Assert.Equal((11, 6), PreviewExportRules.PixelSize(new Rect(0, 0, 10.2, 5.01)));

    [Fact]
    public void OpensMenu_uniquement_pour_le_clic_droit()
    {
        Assert.True(TrayService.OpensMenu(Forms.MouseButtons.Right));
        Assert.False(TrayService.OpensMenu(Forms.MouseButtons.Left));
        Assert.False(TrayService.OpensMenu(Forms.MouseButtons.Middle));
    }
}
