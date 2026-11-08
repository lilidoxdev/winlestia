// C# 12, fichier Themes.cs, cible Windows 11 / .NET 8 WPF, thèmes d'apparence globaux
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace PanneauxGlass;
// Un thème possède plus qu'une couleur : sa matière (verre, papier, encre, affiche, doux, bento, néon,
// rétro, suisse, zen), le cadre de ses boutons, la typographie des titres, la forme d'une carte
// et la disposition de la grille d'accueil. La géométrie, les zones de clic et le comportement ne changent jamais :
// seul l'aspect de la surface change.
internal enum SurfaceKind
{
    Glass, Paper, Ink, Poster, Soft, Bento, Neon, Retro, Swiss, Zen
}

internal sealed class ThemeDefinition
{
    public string Id = "", Name = "", Description = "";
    public Color Surface, HaloA, HaloB, RimA, RimB, Reflection, Accent, Text, Dim;
    public double RadiusScale = 1, ReflectionGain = 1, HaloGain = 1;
    public string Signature = "halos";
    public SurfaceKind Kind = SurfaceKind.Glass;
    public string Heading = "display";   // display · serif · mono · pixel · sans
    public string Tiles = "grid";        // grid · cards · list · bento · index · stack
    public string Frame = "glass";       // glass · flat · outline · bevel · neon
    public double BorderWeight = 1;
    public bool Light;
    // Unified = les couleurs viennent de la palette Material-3 du shell (voir Tokens.cs), pas d'un thème fait main.
    public bool Unified;
    M3Palette? palette;
    public M3Palette P
    {
        get => palette ??= M3Palette.FromTheme(this); init => palette = value;
    }
    public Color Role(M3Role role) => role switch
    {
        M3Role.Surface => P.Surface,
        M3Role.SurfaceLow => P.SurfaceLow,
        M3Role.SurfaceContainer => P.SurfaceContainer,
        M3Role.SurfaceHigh => P.SurfaceHigh,
        M3Role.SurfaceHighest => P.SurfaceHighest,
        M3Role.OnSurface => P.OnSurface,
        M3Role.OnSurfaceVariant => P.OnSurfaceVariant,
        M3Role.Primary => P.Primary,
        M3Role.OnPrimary => P.OnPrimary,
        M3Role.PrimaryContainer => P.PrimaryContainer,
        M3Role.OnPrimaryContainer => P.OnPrimaryContainer,
        M3Role.SecondaryContainer => P.SecondaryContainer,
        M3Role.OnSecondaryContainer => P.OnSecondaryContainer,
        M3Role.Outline => P.Outline,
        M3Role.OutlineVariant => P.OutlineVariant,
        M3Role.Error => P.Error,
        _ => P.OnError
    };
    public bool Opaque => Kind != SurfaceKind.Glass;
    public string Family => Kind switch
    {
        SurfaceKind.Paper => "Papier",
        SurfaceKind.Ink => "Encre",
        SurfaceKind.Poster => "Affiche",
        SurfaceKind.Soft => "Brume",
        SurfaceKind.Bento => "Bento",
        SurfaceKind.Neon => "Néon",
        SurfaceKind.Retro => "Rétro",
        SurfaceKind.Swiss => "Suisse",
        SurfaceKind.Zen => "Zen",
        _ => "Verre"
    };
    // Un thème clair qui peint ses propres plaques opaques (bento, rétro, suisse, zen) ne doit pas transformer
    // chaque blanc translucide du chrome en lavis d'encre : ces thèmes utilisent des surfaces pleines.
    public bool Plated => Opaque && Light;
    public Color Halo(byte alpha, int which) => which == 0
        ? Color.FromArgb((byte)Math.Clamp(alpha * HaloGain, 0, 255), HaloA.R, HaloA.G, HaloA.B)
        : Color.FromArgb((byte)Math.Clamp(alpha * HaloGain, 0, 255), HaloB.R, HaloB.G, HaloB.B);
}

internal static class Theme
{
    static readonly ThemeDefinition[] All ={
        new(){Id="shell",Name="Atelier clair",Description="Crème, argile et cartes douces. Le nouveau bureau Panneaux.",Surface=C(
            252,248,243),HaloA=C(236,220,211),HaloB=C(233,222,214),RimA=C(219,206,197),RimB=C(219,206,197),Reflection=C(
            255,252,249),Accent=C(146,92,76),Text=C(64,51,50),Dim=C(130,107,100),Kind=SurfaceKind
            .Soft,Heading="sans",Tiles="grid",Frame="flat",Light=true,ReflectionGain=0,HaloGain=0,Signature="none",RadiusScale=.85},
        new(){Id="shell-dark",Name="Atelier sombre",Description="Encre chaude, argile et cartes douces.",Surface=C(40,37,45),HaloA=C(
            60,48,60),HaloB=C(60,48,60),RimA=C(76,65,75),RimB=C(76,65,75),Reflection=C(232,191,175),Accent=C(232,191,175),Text=C(
            244,232,225),Dim=C(189,169,160),Kind=SurfaceKind
            .Soft,Heading="sans",Tiles="grid",Frame="flat",ReflectionGain=0,HaloGain=0,Signature="none",RadiusScale=.85},
        new(){
            Id="nuit",Name="Nuit boréale",Description="Le verre d’origine : bleu profond, halos cyan.",
            Surface=C(13,26,43),HaloA=C(82,177,226),HaloB=C(104,220,220),RimA=C(255,255,255),RimB=C(183,216,249),
            Reflection=C(196,244,255),Accent=C(159,228,247),Text=C(245,247,252),Dim=C(185,201,219)
        },
        new(){
            Id="amethyste",Name="Améthyste",Description="Prune et violet, une bande de lumière en travers du verre.",
            Surface=C(27,16,48),HaloA=C(150,100,255),HaloB=C(220,120,255),RimA=C(227,212,255),RimB=C(201,169,255),
            Reflection=C(230,216,255),Accent=C(214,185,255),Text=C(247,242,255),Dim=C(198,184,220),
            ReflectionGain=1.1,HaloGain=1.1,Signature="band"
        },
        new(){
            Id="emeraude",Name="Émeraude",Description="Vert profond, brume turquoise et pétales de verre.",
            Surface=C(7,28,25),HaloA=C(60,200,160),HaloB=C(70,190,220),RimA=C(207,247,231),RimB=C(155,231,208),
            Reflection=C(215,255,244),Accent=C(158,235,211),Text=C(240,251,246),Dim=C(180,207,198),Signature="petals"
        },
        new(){
            Id="cuivre",Name="Cuivre",Description="Ambre chaud, reflets de fin de journée.",
            Surface=C(36,18,9),HaloA=C(235,140,70),HaloB=C(240,190,110),RimA=C(255,226,194),RimB=C(247,192,138),
            Reflection=C(255,233,210),Accent=C(246,196,140),Text=C(255,246,236),Dim=C(217,194,174),
            ReflectionGain=1.05,HaloGain=1.1,Signature="sun"
        },
        new(){
            Id="graphite",Name="Graphite",Description="Neutre, mat, angles plus nets et trame technique.",
            Surface=C(22,24,28),HaloA=C(120,130,145),HaloB=C(90,100,115),RimA=C(230,234,240),RimB=C(185,192,204),
            Reflection=C(232,237,245),Accent=C(207,215,227),Text=C(242,244,248),Dim=C(185,192,204),
            RadiusScale=.52,ReflectionGain=.45,HaloGain=.55,Signature="grid"
        },
        new(){
            Id="sakura",Name="Sakura",Description="Prune rosé, halos fleur de cerisier.",
            Surface=C(33,16,26),HaloA=C(245,130,175),HaloB=C(210,120,220),RimA=C(255,217,230),RimB=C(240,174,203),
            Reflection=C(255,228,238),Accent=C(247,180,208),Text=C(255,244,248),Dim=C(214,185,198),
            ReflectionGain=1.05,HaloGain=1.15,Signature="petals"
        },
        // Calque : du papier, pas du verre. Crème opaque, titres à empattements, grain, cartes au lieu d'une grille d'icônes.
        new(){
            Id="calque",Name="Papier calque",Description="Papier crème, encre brune et titres en serif. Les accès deviennent des fiches.",
            Surface=C(233,226,211),HaloA=C(201,162,39),HaloB=C(127,169,184),RimA=C(255,255,255),RimB=C(183,169,143),
            Reflection=C(255,255,255),Accent=C(162,74,42),Text=C(36,30,23),Dim=C(107,97,82),
            RadiusScale=.38,ReflectionGain=.18,HaloGain=.55,Signature="paper",Kind=SurfaceKind.Paper,
            Heading="serif",Tiles="cards",BorderWeight=1,Light=true
        },
        // Encre : un terminal, pas une fenêtre. Net, plat, à chasse fixe, chaque accès est une ligne.
        new(){
            Id="encre",Name="Encre",Description="Noir de terminal, phosphore vert, typographie à chasse fixe. Tout est en listes.",
            Surface=C(7,10,8),HaloA=C(34,197,94),HaloB=C(14,165,160),RimA=C(183,255,216),RimB=C(46,94,73),
            Reflection=C(156,255,201),Accent=C(74,222,128),Text=C(216,243,228),Dim=C(126,154,139),
            RadiusScale=.12,ReflectionGain=.12,HaloGain=.35,Signature="scan",Kind=SurfaceKind.Ink,
            Heading="mono",Tiles="list",BorderWeight=1
        },
        // Affiche : une affiche imprimée. Gros blocs, traits épais, grandes cartes.
        new(){
            Id="affiche",Name="Affiche",Description="Bleu nuit, corail et moutarde. Blocs imprimés, bords francs, grandes fiches.",
            Surface=C(21,26,46),HaloA=C(255,90,60),HaloB=C(255,200,87),RimA=C(255,255,255),RimB=C(255,90,60),
            Reflection=C(255,209,161),Accent=C(255,200,87),Text=C(255,246,233),Dim=C(199,190,176),
            RadiusScale=.22,ReflectionGain=.15,HaloGain=.45,Signature="poster",Kind=SurfaceKind.Poster,
            Heading="display",Tiles="cards",BorderWeight=3
        },
        // Brume : soft daylight. Pale stone, dark ink, generous curves, no glass blur.
        new(){
            Id="brume",Name="Brume",Description="Pierre claire, ombres douces, courbes généreuses. Une matière mate, lumineuse.",
            Surface=C(223,229,236),HaloA=C(255,255,255),HaloB=C(169,184,201),RimA=C(255,255,255),RimB=C(180,192,206),
            Reflection=C(255,255,255),Accent=C(61,126,166),Text=C(38,49,63),Dim=C(97,110,126),
            RadiusScale=1.35,ReflectionGain=.3,HaloGain=.5,Signature="mist",Kind=SurfaceKind.Soft,
            Heading="sans",Tiles="grid",BorderWeight=1,Light=true
        },
        // Bento : un plateau de petites boîtes pleines. Aucun verre : de larges écarts, des cellules remplies,
        // un index de compartiments colorés au lieu d'une grille d'icônes flottantes.
        new(){
            Id="bento",Name="Bento",Description="Plateau crème et cases pleines, couleurs franches. Chaque accès est une petite boîte.",
            Surface=C(242,237,228),HaloA=C(226,86,47),HaloB=C(76,143,109),RimA=C(255,255,255),RimB=C(120,110,98),
            Reflection=C(255,255,255),Accent=C(214,74,41),Text=C(28,25,22),Dim=C(107,98,87),
            RadiusScale=.55,ReflectionGain=.1,HaloGain=.35,Signature="bento",Kind=SurfaceKind.Bento,
            Heading="sans",Tiles="bento",Frame="flat",BorderWeight=1,Light=true
        },
        // Néon : a dark room lit by two tubes. Outline cards, glow instead of reflection.
        new(){
            Id="neon",Name="Néon",Description="Noir de nuit, traits lumineux magenta et cyan. Les cadres brillent au lieu de refléter.",
            Surface=C(7,7,12),HaloA=C(255,47,185),HaloB=C(57,230,255),RimA=C(255,214,246),RimB=C(57,230,255),
            Reflection=C(120,245,255),Accent=C(182,255,60),Text=C(242,242,247),Dim=C(142,138,166),
            RadiusScale=.8,ReflectionGain=0,HaloGain=1,Signature="neon",Kind=SurfaceKind.Neon,
            Heading="sans",Tiles="grid",Frame="neon",BorderWeight=1
        },
        // Rétro : 1995 on purpose. Grey slab, bevelled buttons, square corners, list rows.
        new(){
            Id="retro",Name="Rétro 95",Description="Gris de bureau, bords biseautés, coins carrés et listes à l’ancienne. Un hommage " +
                "assumé.",
            Surface=C(192,192,192),HaloA=C(0,0,128),HaloB=C(0,128,128),RimA=C(255,255,255),RimB=C(64,64,64),
            Reflection=C(255,255,255),Accent=C(0,0,128),Text=C(16,16,16),Dim=C(74,74,74),
            RadiusScale=.05,ReflectionGain=0,HaloGain=.2,Signature="retro",Kind=SurfaceKind.Retro,
            Heading="pixel",Tiles="list",Frame="bevel",BorderWeight=2,Light=true
        },
        // Suisse : un index imprimé. Feuille blanche, filets, repère rouge, lignes numérotées.
        new(){
            Id="suisse",Name="Suisse",Description="Feuille blanche, filets fins, rouge signal et lignes numérotées. Rien de décoratif.",
            Surface=C(250,250,247),HaloA=C(225,37,27),HaloB=C(14,14,14),RimA=C(14,14,14),RimB=C(200,200,196),
            Reflection=C(255,255,255),Accent=C(225,37,27),Text=C(14,14,14),Dim=C(110,110,110),
            RadiusScale=.1,ReflectionGain=0,HaloGain=.05,Signature="swiss",Kind=SurfaceKind.Swiss,
            Heading="sans",Tiles="index",Frame="outline",BorderWeight=1,Light=true
        },
        // Zen : sable ratissé, lin profond, courbes organiques, larges lignes qui laissent respirer.
        new(){
            Id="zen",Name="Zen",Description="Sable clair, encre verte et courbes amples. Des rangées larges, beaucoup d’air.",
            Surface=C(233,226,212),HaloA=C(143,160,122),HaloB=C(216,201,168),RimA=C(255,255,255),RimB=C(176,166,144),
            Reflection=C(255,255,255),Accent=C(180,85,45),Text=C(44,51,43),Dim=C(110,115,101),
            RadiusScale=1.6,ReflectionGain=.12,HaloGain=.4,Signature="zen",Kind=SurfaceKind.Zen,
            Heading="serif",Tiles="stack",Frame="flat",BorderWeight=1,Light=true
        }
    };
    internal static ThemeDefinition Current { get; private set; } = All[0];
    internal static int Version
    {
        get; private set;
    }
    internal static IReadOnlyList<ThemeDefinition> Catalogue => All;
    internal static ThemeDefinition? Find(string? id) => All.FirstOrDefault(t => string.Equals(t.Id, id,
        StringComparison.OrdinalIgnoreCase));
    internal static Color Mix(Color a, Color b, double t) => Color.FromRgb((byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G
        + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
    // Identifiants qui désignent « l'apparence du shell » : le thème par défaut, la bascule clair/sombre du shell et la palette
    // personnalisée.
    // Tout autre identifiant (amethyste, calque, retro...) est un thème hérité et ne s'applique que si l'utilisateur le choisit.
    static bool IsShellFamily(string id) => id is "shell" or "shell-dark" or "nuit" or "custom";
    // Same decision tree as QtShellBridge settings.themeMode: auto > custom > light (shell) > dark.
    static ThemeDefinition Unify(ThemeDefinition legacy, bool custom)
    {
        M3Palette palette;
        if (Store.State.AutoTheme)
            palette = M3.AutoPalette ?? M3Palette.DarkDefault();
        else if (custom)
            palette = M3Palette.Custom(Store.State.CustomPalette);
        else
            palette = legacy.Id == "shell" ? M3Palette.LightDefault() : M3Palette.DarkDefault();
        // Les shells sombres sont du verre translucide sur le fond de la fenêtre ; les modes clair et effets réduits restent pleins.
        var kind = !palette.Dark || M3.ReducedEffects ? SurfaceKind.Soft : SurfaceKind.Glass;
        return new ThemeDefinition
        {
            Id = legacy.Id,
            Name = legacy.Name,
            Description = legacy.Description,
            Unified = true,
            P = palette,
            Surface = palette.SurfaceContainer,
            HaloA = palette.Primary,
            HaloB = palette.Primary,
            RimA = palette.OutlineVariant,
            RimB = palette.OutlineVariant,
            Reflection = palette.OnSurface,
            Accent = palette.Primary,
            Text = palette.OnSurface,
            Dim = palette.OnSurfaceVariant,
            Kind = kind,
            Heading = "sans",
            Tiles = "grid",
            Frame = "flat",
            Light = !palette.Dark,
            ReflectionGain = 0,
            HaloGain = 0,
            Signature = "none",
            RadiusScale = 1
        };
    }
    internal static void Use(string? id)
    {
        bool custom = id == "custom";
        string nativeId = id ?? "nuit";
        if (custom)
        {
            nativeId = "shell";
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(Store.State.CustomPalette.GetValueOrDefault("background", "#fff8f6"));
                nativeId = color.R * .2126 + color.G * .7152 + color.B * .0722 < 128 ? "nuit" : "shell";
            }
            catch (Exception ex)
            {
                Store.Log("Themes.Use : " + ex.GetType().Name + ": " + ex.Message);
            }
        }
        var next = Find(nativeId) ?? Find("nuit")!;
        if (IsShellFamily(next.Id))
            next = Unify(next, custom);
        Current = next;
        Version++;
        UI.ApplyTheme();
        FilesPreload.NotifyPalette();
        if (!custom && !string.Equals(Store.State.Theme, next.Id, StringComparison.Ordinal))
        {
            Store.State.Theme = next.Id;
            Store.Save();
        }
    }
    static Color C(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
}
