// language: C# 12, file: WinjetCondition.cs, target: Windows 11 / .NET 8, évaluateur minimal des conditions (visibleWhen, enabledWhen)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
namespace PanneauxGlass.Winjets;

// Grammaire fermée : littéraux ('x' "x" 1.5 true false null), chemins pointés, == != < <= > >=, &&, ||, !, parenthèses.
// Rien d'autre n'est évalué : pas d'appel, pas d'arithmétique, pas d'accès indexé.
internal sealed class WinjetCondition
{
    readonly Func<Func<string, object?>, object?> root;
    WinjetCondition(Func<Func<string, object?>, object?> root) => this.root = root;

    internal static bool TryParse(string? text, out WinjetCondition? condition, out string error)
    {
        condition = null;
        error = "";
        if (string.IsNullOrWhiteSpace(text) || text.Length > 400)
        {
            error = "expression vide ou trop longue";
            return false;
        }
        try
        {
            var parser = new Parser(text);
            condition = new WinjetCondition(parser.ParseAll());
            return true;
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    // Chemin inconnu = null (donc faux). Une expression invalide vaut faux : une source mal déclarée reste désactivée.
    internal static bool Evaluate(string? text, Func<string, object?> resolve)
        => TryParse(text, out var c, out _) && c!.Test(resolve);

    internal bool Test(Func<string, object?> resolve) => Truthy(root(resolve));

    static bool Truthy(object? v) => v switch
    {
        null => false,
        bool b => b,
        double d => d != 0,
        string s => s.Length > 0,
        _ => true
    };

    // Normalise les nombres entiers/flottants en double pour la comparaison.
    internal static object? Normalize(object? v) => v switch
    {
        int i => (double)i,
        long l => (double)l,
        float f => (double)f,
        decimal m => (double)m,
        _ => v
    };

    static bool Equal(object? a, object? b)
    {
        a = Normalize(a);
        b = Normalize(b);
        if (a == null || b == null)
            return a == null && b == null;
        return a.GetType() == b.GetType() && a.Equals(b);
    }

    static int Compare(object? a, object? b, out bool ok)
    {
        a = Normalize(a);
        b = Normalize(b);
        ok = true;
        if (a is double x && b is double y)
            return x.CompareTo(y);
        if (a is string s && b is string t)
            return string.CompareOrdinal(s, t);
        ok = false;
        return 0;
    }

    sealed class Parser
    {
        readonly string s;
        int i, depth;
        internal Parser(string text) => s = text;

        internal Func<Func<string, object?>, object?> ParseAll()
        {
            var e = Or();
            Skip();
            if (i < s.Length)
                throw new FormatException("caractère inattendu « " + s[i] + " » à la position " + i);
            return e;
        }

        void Skip()
        {
            while (i < s.Length && char.IsWhiteSpace(s[i]))
                i++;
        }

        bool Take(string token)
        {
            Skip();
            if (string.CompareOrdinal(s, i, token, 0, token.Length) != 0)
                return false;
            i += token.Length;
            return true;
        }

        Func<Func<string, object?>, object?> Or()
        {
            var left = And();
            while (Take("||"))
            {
                var l = left;
                var r = And();
                left = ctx => Truthy(l(ctx)) || Truthy(r(ctx));
            }
            return left;
        }

        Func<Func<string, object?>, object?> And()
        {
            var left = Not();
            while (Take("&&"))
            {
                var l = left;
                var r = Not();
                left = ctx => Truthy(l(ctx)) && Truthy(r(ctx));
            }
            return left;
        }

        Func<Func<string, object?>, object?> Not()
        {
            Skip();
            if (i < s.Length && s[i] == '!' && !(i + 1 < s.Length && s[i + 1] == '='))
            {
                i++;
                if (++depth > 16)
                    throw new FormatException("expression trop imbriquée");
                var inner = Not();
                depth--;
                return ctx => !Truthy(inner(ctx));
            }
            return Comparison();
        }

        Func<Func<string, object?>, object?> Comparison()
        {
            var left = Primary();
            foreach (string op in new[] { "==", "!=", "<=", ">=", "<", ">" })
            {
                if (!Take(op))
                    continue;
                var right = Primary();
                return ctx =>
                {
                    object? a = left(ctx), b = right(ctx);
                    if (op == "==")
                        return Equal(a, b);
                    if (op == "!=")
                        return !Equal(a, b);
                    int c = Compare(a, b, out bool ok);
                    return ok && op switch { "<" => c < 0, "<=" => c <= 0, ">" => c > 0, _ => c >= 0 };
                };
            }
            return left;
        }

        Func<Func<string, object?>, object?> Primary()
        {
            Skip();
            if (i >= s.Length)
                throw new FormatException("expression incomplète");
            char c = s[i];
            if (c == '(')
            {
                i++;
                if (++depth > 16)
                    throw new FormatException("expression trop imbriquée");
                var inner = Or();
                depth--;
                if (!Take(")"))
                    throw new FormatException("parenthèse fermante manquante");
                return inner;
            }
            if (c is '\'' or '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < s.Length && s[i] != c)
                    sb.Append(s[i++]);
                if (i >= s.Length)
                    throw new FormatException("chaîne non terminée");
                i++;
                string text = sb.ToString();
                return _ => text;
            }
            if (char.IsAsciiDigit(c) || c == '-')
            {
                int start = i++;
                while (i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] == '.'))
                    i++;
                if (!double.TryParse(s.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                    throw new FormatException("nombre invalide");
                return _ => n;
            }
            if (char.IsAsciiLetter(c) || c == '_')
            {
                int start = i;
                while (i < s.Length && (char.IsAsciiLetterOrDigit(s[i]) || s[i] is '_' or '.'))
                    i++;
                string word = s[start..i];
                switch (word)
                {
                    case "true":
                        return _ => true;
                    case "false":
                        return _ => false;
                    case "null":
                        return _ => null;
                }
                if (word.EndsWith('.') || word.Contains(".."))
                    throw new FormatException("chemin invalide « " + word + " »");
                return ctx => ctx(word);
            }
            throw new FormatException("caractère inattendu « " + c + " » à la position " + i);
        }
    }
}
