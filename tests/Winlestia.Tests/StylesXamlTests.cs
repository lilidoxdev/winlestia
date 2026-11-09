// C#12 / Regression : le XAML des styles (UI.StylesXaml) doit rester analysable pour chaque variante de theme.
// Un refactor avait laisse des concatenations C# dans des chaines brutes : XmlException au demarrage, aucun test ne le voyait.
// Aucune fenetre n'est creee (XamlReader.Parse seul, sur un thread STA), donc ces tests tournent aussi en CI.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;
using PanneauxGlass;
using Xunit;
namespace Winlestia.Tests;
public class StylesXamlTests
{
    static readonly BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Static;

    // Catalogue complet (verre, papier, encre, affiche, ... clair et sombre) + variantes unifiees (Material-3) du shell.
    static IEnumerable<ThemeDefinition> Variants()
    {
        var unify = typeof(Theme).GetMethod("Unify", Hidden)!;
        foreach (var t in Theme.Catalogue)
            yield return t;
        foreach (var id in new[] { "shell", "nuit" })
        {
            foreach (var custom in new[] { false, true })
            {
                yield return (ThemeDefinition)unify.Invoke(null, new object[] { Theme.Find(id)!, custom })!;
            }
        }
    }

    static void WithTheme(ThemeDefinition theme, Action body)
    {
        var setter = typeof(Theme).GetProperty("Current", Hidden)!.GetSetMethod(true)!;
        var previous = Theme.Current;
        setter.Invoke(null, new object[] { theme });
        try
        {
            body();
        }
        finally
        {
            setter.Invoke(null, new object[] { previous });
        }
    }

    [Fact]
    public void Chaque_variante_de_theme_produit_un_xaml_valide()
    {
        Sta.Run(() =>
        {
            var failures = new List<string>();
            foreach (var theme in Variants())
            {
                WithTheme(theme, () =>
                {
                    try
                    {
                        Assert.IsType<ResourceDictionary>(XamlReader.Parse(UI.StylesXaml(theme)));
                    }
                    catch (Exception ex)
                    {
                        failures.Add(theme.Id + (theme.Unified ? " (unifie)" : "") + " : " + ex.Message);
                    }
                });
            }
            Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        });
    }

    [Fact]
    public void Les_variantes_couvrent_unifie_opaque_verre_clair_et_sombre()
    {
        var all = Variants().ToList();
        Assert.Contains(all, t => t.Unified);
        Assert.Contains(all, t => t.Opaque && !t.Unified);
        Assert.Contains(all, t => !t.Opaque);
        Assert.Contains(all, t => t.Light);
        Assert.Contains(all, t => !t.Light);
    }
}
