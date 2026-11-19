// C# 12, fichier Calculator.cs, cible Windows 11 / .NET 8, arithmétique en ligne pour la recherche et la palette
using System;
using System.Globalization;
using System.Linq;

namespace PanneauxGlass;
// Descente récursive sur + - * / % et parenthèses. Aucune lettre, aucune évaluation d'autre chose.
internal static class Calculator
{
    internal static bool TryEvaluate(string? input, out double value)
    {
        // Un espace n'est jamais un opérateur, il sert donc de séparateur de milliers : "2 400 / 60" vaut 40.
        value = 0;
        string source = new string((input ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()).Replace(',', '.').Replace('×',
            '*').Replace('÷', '/').Replace('−', '-').Replace('x', '*').Replace('X', '*');
        if (source.Length is 0 or > 120)
            return false;
        if (!source.Any(c => c is '+' or '-' or '*' or '/' or '%' or '('))
            return false;
        var parser = new Parser(source);
        double result = parser.Expression();
        if (!parser.Valid || !parser.Done || !double.IsFinite(result))
            return false;
        value = result;
        return true;
    }
    internal static string Format(double value) => Math.Abs(value - Math.Round(value)) < 1e-9 ? Math.Round(value).ToString("0",
        CultureInfo.CurrentCulture) : value.ToString("0.####", CultureInfo.CurrentCulture);
    sealed class Parser
    {
        readonly string text; int index; internal bool Valid = true;
        internal Parser(string source)
        {
            text = source;
        }
        internal bool Done => index >= text.Length;
        void Skip()
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
                index++;
        }
        char Peek()
        {
            Skip();
            return index < text.Length ? text[index] : '\0';
        }
        internal double Expression()
        {
            double value = Term();
            while (Valid)
            {
                char c = Peek();
                if (c == '+')
                {
                    index++;
                    value += Term();
                }
                else if (c == '-')
                {
                    index++;
                    value -= Term();
                }
                else
                    break;
            }
            return value;
        }
        double Term()
        {
            double value = Factor();
            while (Valid)
            {
                char c = Peek();
                if (c == '*')
                {
                    index++;
                    value *= Factor();
                }
                else if (c == '/')
                {
                    index++;
                    double divisor = Factor();
                    if (divisor == 0)
                    {
                        Valid = false;
                        return 0;
                    }
                    value /= divisor;
                }
                else if (c == '%')
                {
                    index++;
                    double divisor = Factor();
                    if (divisor == 0)
                    {
                        Valid = false;
                        return 0;
                    }
                    value %= divisor;
                }
                else
                    break;
            }
            return value;
        }
        double Factor()
        {
            char c = Peek();
            if (c == '+')
            {
                index++;
                return Factor();
            }
            if (c == '-')
            {
                index++;
                return -Factor();
            }
            if (c == '(')
            {
                index++;
                double inner = Expression();
                if (Peek() == ')')
                    index++;
                else
                    Valid = false;
                return inner;
            }
            return Number();
        }
        double Number()
        {
            Skip();
            int start = index;
            while (index < text.Length && (char.IsDigit(text[index]) || text[index] == '.'))
                index++;
            if (start == index)
            {
                Valid = false;
                return 0;
            }
            string slice = text.Substring(start, index - start);
            if (slice.Count(c => c == '.') > 1)
            {
                Valid = false;
                return 0;
            }
            if (!double.TryParse(slice, slice.Contains('.') ? NumberStyles.AllowDecimalPoint : NumberStyles.None,
                CultureInfo.InvariantCulture, out double value))
            {
                Valid = false;
                return 0;
            }
            return value;
        }
    }
}
