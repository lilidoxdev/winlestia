// Regression : deux plantages au demarrage ont ete livres parce qu'aucun test ne construisait Coordinator
// (XAML des styles invalide, puis HomeWindow lisant Coordinator.Keys avant son affectation).
// Mode --preview : pas de barre des taches remplacee, pas de masquage des icones du bureau, pas de cle Run, pas de pont Qt.
// Preview ouvre de vraies fenetres WPF : categorie Desktop, hors CI.
using System;
using System.IO;
using System.Linq;
using System.Windows;
using PanneauxGlass;
using Xunit;
namespace Winlestia.Tests;
public class CoordinatorStartupTests
{
    [Fact]
    [Trait("Category", "Desktop")]
    public void Le_coordinateur_se_construit_en_preview_sans_exception()
    {
        string dir = Path.Combine(Path.GetTempPath(), "WinlestiaTest-" + Guid.NewGuid().ToString("N"));
        string? previous = Environment.GetEnvironmentVariable("WINLESTIA_DATA_DIR");
        Environment.SetEnvironmentVariable("WINLESTIA_DATA_DIR", dir);
        try
        {
            Sta.Run(() =>
            {
                _ = Application.Current ?? new Application();
                Coordinator? coordinator = null;
                try
                {
                    coordinator = new Coordinator(true, true, false, RunOptions.Default);
                }
                finally
                {
                    coordinator?.Dispose();
                    foreach (var window in Application.Current!.Windows.Cast<Window>().ToArray())
                        window.Close();
                }
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINLESTIA_DATA_DIR", previous);
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
