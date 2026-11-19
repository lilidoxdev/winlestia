using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using PanneauxGlass;
using Xunit;

namespace Winlestia.Tests;

// Le QML lit ces vues par nom de clé : un renommage de propriété casserait l'interface sans
// erreur de compilation. Ces tests figent les clés JSON émises vers le frontend.
public class BridgeViewJsonTests
{
    // Mêmes options que QtShellBridge.Json : aucune politique de nommage.
    static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    static string[] Keys<T>(T value)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value, Json));
        return doc.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
    }

    [Fact]
    public void PaletteWindowKeys() =>
        Assert.Equal(new[] { "hwnd", "title", "exe" }, Keys(new PaletteWindow(1, "t", "e.exe")));

    [Fact]
    public void WallpaperDisplayViewKeys() =>
        Assert.Equal(
            new[] { "id", "name", "width", "height", "primary", "path" },
            Keys(new WallpaperDisplayView(0, "n", 1920, 1080, true, "p")));

    [Fact]
    public void WallpaperPickerViewKeys() =>
        Assert.Equal(
            new[] { "displays", "animated" },
            Keys(new WallpaperPickerView(new WallpaperDisplayView[0], new string[0])));
}
