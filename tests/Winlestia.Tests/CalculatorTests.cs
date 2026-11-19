// Calculator : evaluation arithmetique en ligne (recherche et palette). Parseur recursif, sans lettres ni evaluation libre.
using System.Globalization;
using System.Linq;
using PanneauxGlass;
namespace Winlestia.Tests;
public class CalculatorTests
{
    static double Eval(string input)
    {
        Assert.True(Calculator.TryEvaluate(input, out double value), "doit etre evalue : " + input);
        return value;
    }

    [Theory]
    [InlineData("1+2", 3)]
    [InlineData("2+3*4", 14)]
    [InlineData("(2+3)*4", 20)]
    [InlineData("2*(3+4)", 14)]
    [InlineData("((2))+0", 2)]
    [InlineData("10-4-3", 3)]
    [InlineData("100/10/5", 2)]
    [InlineData("10%3", 1)]
    [InlineData("-5", -5)]
    [InlineData("2--3", 5)]
    [InlineData("--3+0", 3)]
    [InlineData("2++3", 5)]
    [InlineData("2*-3", -6)]
    [InlineData("1.5*2", 3)]
    [InlineData(".5+.5", 1)]
    [InlineData("0x10+1", 1)]      // le x est un signe de multiplication : 0*10+1, pas de l'hexadecimal
    public void ValidExpressions(string input, double expected) => Assert.Equal(expected, Eval(input), 9);

    [Theory]
    [InlineData("2x3", 6)]
    [InlineData("2X3", 6)]
    [InlineData("2×3", 6)]
    [InlineData("6÷3", 2)]
    [InlineData("5−2", 3)]
    [InlineData("1,5+1", 2.5)]
    [InlineData("2 400 / 60", 40)]
    [InlineData("  3 + 4  ", 7)]
    public void TypographicVariantsAndSpacesAreNormalised(string input, double expected) =>
        Assert.Equal(expected, Eval(input), 9);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("42")]
    [InlineData("3.14")]
    [InlineData("hello")]
    [InlineData("2+a")]
    [InlineData("abc+1")]
    [InlineData("1e5+1")]
    [InlineData("Math.PI*2")]
    public void NotAnExpressionIsRejected(string? input) => Assert.False(Calculator.TryEvaluate(input, out _));

    [Theory]
    [InlineData("1/0")]
    [InlineData("5%0")]
    [InlineData("1/(2-2)")]
    [InlineData("(2+3")]
    [InlineData("2+3)")]
    [InlineData("()")]
    [InlineData("2+")]
    [InlineData("*3")]
    [InlineData("1.2.3+1")]
    [InlineData("2 3")]
    [InlineData("2+3 4+")]
    public void MalformedOrUndefinedIsRejectedWithoutThrowing(string input)
    {
        Assert.False(Calculator.TryEvaluate(input, out double value));
        Assert.Equal(0, value);
    }

    [Fact]
    public void InputLongerThan120CharactersIsRejected()
    {
        string ok = string.Join("+", Enumerable.Repeat("1", 60));   // 119 caracteres
        string tooLong = ok + "+1";                                  // 121 caracteres
        Assert.Equal(60, Eval(ok), 9);
        Assert.False(Calculator.TryEvaluate(tooLong, out _));
    }

    [Fact]
    public void OverflowToInfinityIsRejected()
    {
        string huge = new string('9', 100);
        Assert.False(Calculator.TryEvaluate(huge + "*" + huge + "*" + huge + "*" + huge, out _));
    }

    [Fact]
    public void ModuloKeepsTheSignOfTheDividend()
    {
        Assert.Equal(-1, Eval("-7%3"), 9);
        Assert.Equal(1, Eval("7%-3"), 9);
    }

    [Fact]
    public void FormatDropsDecimalsOnWholeNumbersAndRoundsToFourPlaces()
    {
        var saved = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal("3", Calculator.Format(3));
            Assert.Equal("3", Calculator.Format(3.0000000001));
            Assert.Equal("0.3333", Calculator.Format(1.0 / 3));
            Assert.Equal("-2.5", Calculator.Format(-2.5));
            Assert.Equal("0", Calculator.Format(0.1 + 0.2 - 0.3));
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }
}
