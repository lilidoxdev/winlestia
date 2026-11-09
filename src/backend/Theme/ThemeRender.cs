// C# 12, fichier ThemeRender.cs, cible Windows 11 / .NET 8 WPF, rendus hors écran des thèmes et de la palette
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace PanneauxGlass;
// Vérification sans bureau : le coordinateur peint ses vraies fenêtres, mais chaque panneau est déplacé
// très loin hors de l'écran virtuel, donc rien n'apparaît et le pointeur n'est jamais pris. Chaque thème est
// exporté dans son propre PNG, puis l'application se ferme d'elle-même.
internal static class ThemeRender
{
    // La palette se résout hors du thread UI : attendre son rendu avant l'export.
    const int PaletteSettleMs = 2200;
    // Laisse le thème s'appliquer et se dessiner avant l'export de l'image.
    const int RenderSettleMs = 900;
    internal static void Themes(Coordinator coordinator, string? only)
    {
        var ids = Theme.Catalogue.Select(theme => theme.Id).Where(id => string.IsNullOrWhiteSpace(only) || id == only).ToList();
        if (ids.Count == 0)
            ids.Add(Theme.Current.Id);
        int index = -1;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(RenderSettleMs) };
        timer.Tick += (_, _) =>
        {
            if (index >= 0 && index < ids.Count)
            {
                coordinator.ExportPreview("render-" + ids[index] + ".png");
                Store.Log("Rendu hors écran · " + ids[index]);
            }
            index++;
            if (index >= ids.Count)
            {
                timer.Stop();
                Application.Current.Shutdown();
                return;
            }
            coordinator.SetTheme(ids[index]);
        };
        timer.Start();
    }
    internal static void Palette(Coordinator coordinator, string query)
    {
        coordinator.PreviewPaletteQuery(query);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PaletteSettleMs) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            coordinator.ExportPreview("render-palette.png");
            Store.Log("Rendu hors écran · palette");
            Application.Current.Shutdown();
        };
        timer.Start();
    }
}
