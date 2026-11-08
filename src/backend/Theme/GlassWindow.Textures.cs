// GlassWindow : textures de fond propres à chaque thème.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Input;

namespace PanneauxGlass;

internal partial class GlassWindow
{
    // Chaque thème porte une forme signature pour que deux thèmes ne se lisent jamais comme le même verre recoloré.
    static Brush? SignatureBrush(ThemeDefinition theme) => theme.Signature switch
    {
        "band" => new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops = new(){
            new(Colors.Transparent,.30),new(theme.Halo(52,1),.47),new(Color.FromArgb((byte)Math.Clamp(18*theme.HaloGain,0,255),
                                                                                     theme.HaloA.R,
                                                                                     theme.HaloA.G,
                                                                                     theme.HaloA.B),
                                                                      .56),new(Colors.Transparent,.70)}
        },
        "sun" => new RadialGradientBrush
        {
            Center = new Point(.88, .98),
            GradientOrigin = new Point(.88, .98),
            RadiusX = .75,
            RadiusY = .95,
            GradientStops = new() { new(theme.Halo(78, 1), 0), new(theme.Halo(28, 0), .55), new(Colors.Transparent, 1) }
        },
        "petals" => Petals(theme),
        "grid" => Grid(theme),
        "paper" => Paper(theme),
        "scan" => Scan(theme),
        "poster" => Poster(theme),
        "mist" => Mist(theme),
        "bento" => Bento(theme),
        "neon" => Neon(theme),
        "retro" => Retro(theme),
        "swiss" => Swiss(theme),
        "zen" => Zen(theme),
        _ => null
    };
    // Fibres de grain du thème papier : une feuille en mosaïque, jamais une image étirée.
    static Brush Paper(ThemeDefinition theme)
    {
        var group = new DrawingGroup();
        var fibre = new Pen(new SolidColorBrush(Color.FromArgb(22, 92, 72, 50)), .7);
        for (int i = 0; i < 26; i++)
        {
            double x = i * 37 % 120 + 4, y = i * 53 % 120 + 4;
            group.Children.Add(new GeometryDrawing(null, fibre, new LineGeometry(new Point(x, y), new Point(x + 18 + i % 5 * 6, y + 2))));
        }
        var speck = new SolidColorBrush(Color.FromArgb(12, 70, 55, 35));
        for (int i = 0; i < 44; i++)
        {
            double x = i * 29 % 120, y = i * 41 % 120;
            group.Children.Add(new GeometryDrawing(speck, null, new EllipseGeometry(new Point(x, y), .8, .8)));
        }
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewbox = new Rect(0, 0, 120, 120),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 120, 120),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill
        };
    }
    // Lignes de balayage au phosphore, un pixel de lumière tous les quatre.
    static Brush Scan(ThemeDefinition theme)
    {
        var group = new DrawingGroup();
        var line = new SolidColorBrush(Color.FromArgb(16, theme.HaloA.R, theme.HaloA.G, theme.HaloA.B));
        group.Children.Add(new GeometryDrawing(line, null, new RectangleGeometry(new Rect(0, 0, 4, 1))));
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewbox = new Rect(0, 0, 4, 4),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 4, 4),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill
        };
    }
    // Une affiche imprimée : un arc, une bande diagonale et un champ de demi-teintes.
    static Brush Poster(ThemeDefinition theme)
    {
        // Coordonnées absolues : le motif garde sa taille quelle que soit la largeur de la fenêtre au lieu de s'étirer.
        var group = new DrawingGroup();
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(18, theme.HaloB.R, theme.HaloB.G, theme.HaloB.B)), 2);
        group.Children.Add(new GeometryDrawing(null, pen, new EllipseGeometry(new Point(1050, 150), 150, 150)));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(10, theme.HaloA.R, theme.HaloA.G, theme.HaloA.B)),
                                               null,
                                               new RectangleGeometry(new Rect(0, 520, 780, 54), 2, 2)));
        var dot = new SolidColorBrush(Color.FromArgb(22, theme.HaloA.R, theme.HaloA.G, theme.HaloA.B));
        for (int row = 0; row < 5; row++)
            for (int column = 0; column < 10; column++)
                group.Children.Add(new GeometryDrawing(dot, null, new EllipseGeometry(new Point(80 + column * 38, 620 + row * 22), 2.4,
                    2.4)));
        return new DrawingBrush(group)
        {
            TileMode = TileMode.None,
            Viewbox = new Rect(0, 0, 1400, 900),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 1400, 900),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
    }
    // Lumière du jour douce : seulement deux halos flous et un grain léger.
    static Brush Mist(ThemeDefinition theme)
    {
        var group = new DrawingGroup();
        foreach (var (x, y, r) in new[] { (0.12, 0.08, 0.55), (0.9, 0.86, 0.6) })
        {
            var brush =
                new RadialGradientBrush(new GradientStopCollection { new(theme.Halo(46, 0), 0), new(theme.Halo(20, 1), .62),
                    new(Colors.Transparent, 1) })
                {
                    Center = new Point(x, y),
                    GradientOrigin = new Point(x, y),
                    RadiusX = r,
                    RadiusY = r
                };
            group.Children.Add(new GeometryDrawing(brush, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
        }
        return new DrawingBrush(group) { Stretch = Stretch.Fill };
    }
    static Brush Petals(ThemeDefinition theme)
    {
        var group = new DrawingGroup();
        foreach (var (x, y, r) in new[] { (0.16, 0.78, 0.26), (0.84, 0.18, 0.22), (0.66, 0.92, 0.16), (0.34, 0.12, 0.12) })
        {
            group.Children.Add(new GeometryDrawing(new RadialGradientBrush { GradientStops = new() { new(theme.Halo(40, 0), 0),
                new(Colors.Transparent, 1) } },
                                                   null,
                                                   new EllipseGeometry(new Point(x, y), r, r * .7)));
        }
        return new DrawingBrush(group);
    }
    static Brush Grid(ThemeDefinition theme)
    {
        var group = new DrawingGroup();
        var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(14 * theme.HaloGain, 0, 255), 255, 255, 255)), 1);
        group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 0), new Point(28, 0))));
        group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 0), new Point(0, 28))));
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 28, 28),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
    }
    // Bento : une grille de plateau discrète avec une cellule pleine décalée, pour que la surface se lise en compartiments.
    static Brush Bento(ThemeDefinition theme)
    {
        var group = new DrawingGroup();
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(9, theme.Text.R, theme.Text.G, theme.Text.B)), 1);
        group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 0), new Point(240, 0))));
        group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 0), new Point(0, 240))));
        var dot = new SolidColorBrush(Color.FromArgb(30, theme.HaloA.R, theme.HaloA.G, theme.HaloA.B));
        group.Children.Add(new GeometryDrawing(dot, null, new RectangleGeometry(new Rect(0, 0, 2, 2))));
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewbox = new Rect(0, 0, 240, 240),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 240, 240),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
    }
    // Néon: two tubes bleeding colour into a dark room, one from each corner.
    static Brush Neon(ThemeDefinition theme)
    {
        var group = new DrawingGroup();
        foreach (var (x, y, r, which) in new[] { (0.02, 0.98, 0.85, 0), (0.98, 0.02, 0.8, 1) })
        {
            group.Children.Add(new GeometryDrawing(new RadialGradientBrush
            {
                GradientStops = new() { new(theme.Halo(58, which), 0), new(theme.Halo(20, which), .5), new(Colors.Transparent, 1) }
            },
                                                   null,
                                                   new RectangleGeometry(new Rect(0, 0, 1, 1))));
        }
        return new DrawingBrush(group) { Stretch = Stretch.Fill };
    }
    // Rétro: the classic two-pixel desktop dither.
    static Brush Retro(ThemeDefinition theme)
    {
        var group = new DrawingGroup();
        var dot = new SolidColorBrush(Color.FromArgb(18, 0, 0, 0));
        group.Children.Add(new GeometryDrawing(dot, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
        group.Children.Add(new GeometryDrawing(dot, null, new RectangleGeometry(new Rect(2, 2, 1, 1))));
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewbox = new Rect(0, 0, 4, 4),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 4, 4),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill
        };
    }
    // Suisse : une seule grille de filets, presque invisible, plus un trait rouge en haut.
    static Brush Swiss(ThemeDefinition theme)
    {
        var group = new DrawingGroup();
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(9, 0, 0, 0)), 1);
        group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 0), new Point(0, 1000))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(255, theme.Accent.R, theme.Accent.G, theme.Accent.B)),
                                               null,
                                               new RectangleGeometry(new Rect(0, 0, 52, 3))));
        return new DrawingBrush(group)
        {
            TileMode = TileMode.None,
            Viewbox = new Rect(0, 0, 1000, 1000),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 1000, 1000),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.None
        };
    }
    // Zen : sable ratissé, sillons horizontaux à la dérive lente.
    static Brush Zen(ThemeDefinition theme)
    {
        var group = new DrawingGroup();
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(14, 92, 84, 64)), 1);
        for (int i = 0; i < 9; i++)
            group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(0, 4 + i * 9), new Point(1000, 4 + i * 9))));
        return new DrawingBrush(group)
        {
            TileMode = TileMode.Tile,
            Viewbox = new Rect(0, 0, 1000, 90),
            ViewboxUnits = BrushMappingMode.Absolute,
            Viewport = new Rect(0, 0, 1000, 90),
            ViewportUnits = BrushMappingMode.Absolute,
            Stretch = Stretch.Fill
        };
    }
    static Color colorWith(Color color, double gain) => Color.FromArgb((byte)Math.Clamp(color.A * gain, 0, 255), color.R, color.G, color.B);
}
