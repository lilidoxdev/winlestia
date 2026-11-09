// C#12 / UserName.FirstOf: prénom de salutation, formes « Prénom Nom » et « Nom, Prénom ».
using PanneauxGlass;
namespace Winlestia.Tests;
public class UserNameTests
{
    [Theory]
    [InlineData("Marie Dupont", "x", "Marie")]
    [InlineData("Dupont, Marie", "x", "Marie")]
    [InlineData("Dupont,Marie Claire", "x", "Marie")]
    [InlineData("  Marie   Dupont  ", "x", "Marie")]
    [InlineData("Marie", "x", "Marie")]
    [InlineData("", "mdupont", "mdupont")]
    [InlineData("   ", " mdupont ", "mdupont")]
    [InlineData("Dupont,", "mdupont", "mdupont")]
    [InlineData(null, null, "toi")]
    public void FirstOfHandlesBothOrders(string? display, string? fallback, string expected)
        => Assert.Equal(expected, UserName.FirstOf(display, fallback));
}
