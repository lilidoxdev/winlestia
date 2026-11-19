// Regression : le XAML des styles de l'application Files (Styles.Load) doit rester analysable.
// Des concatenations C# collees dans la chaine brute rendaient le XAML invalide (XmlException) sans qu'aucun test ne le voie.
// Aucune fenetre n'est creee (XamlReader.Parse seul, sur un thread STA), donc ce test tourne aussi en CI.
using System.Reflection;
using System.Windows;
using Xunit;
namespace Winlestia.Tests;
public class FilesStylesXamlTests
{
    [Fact]
    public void Le_xaml_des_styles_de_Files_est_valide()
    {
        Sta.Run(() =>
        {
            // Styles est internal dans Winlestia.Files : appel par reflexion.
            var type = Assembly.Load("Winlestia.Files").GetType("WinlestiaFiles.Styles", true)!;
            var load = type.GetMethod("Load", BindingFlags.NonPublic | BindingFlags.Static)!;
            var dictionary = Assert.IsType<ResourceDictionary>(load.Invoke(null, null));
            Assert.NotEmpty(dictionary.Keys);
        });
    }
}
