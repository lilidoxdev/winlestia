// C#12 / ShortcutGateRules: blocage des raccourcis globaux pendant que les réglages sont ouverts, et prénom de salutation.
using PanneauxGlass;
namespace Winlestia.Tests;
public class ShortcutGateTests
{
    [Theory]
    [InlineData("Palette", true)]
    [InlineData("Capture", true)]
    [InlineData("Toggle", true)]
    [InlineData("Rescue", false)]
    [InlineData("Settings", false)]
    [InlineData("Wallpaper", false)]
    [InlineData("Pin", false)]
    [InlineData("None", false)]
    public void SettingsOpenBlocksOnlyThreeActions(string name, bool blocked)
    {
        var action = System.Enum.Parse<HotkeyAction>(name);
        Assert.Equal(blocked, ShortcutGateRules.IsBlockedBySettings(action, true));
        Assert.False(ShortcutGateRules.IsBlockedBySettings(action, false));
    }

    [Theory]
    [InlineData("Marie Dupont", "mdupont", "Marie")]
    [InlineData("", "mdupont", "mdupont")]
    [InlineData(null, " ", "toi")]
    public void FirstNameFallsBack(string? display, string? fallback, string expected)
        => Assert.Equal(expected, UserName.FirstOf(display, fallback));
}
