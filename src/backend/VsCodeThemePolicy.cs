// C# 12 / VsCodeThemePolicy.cs / Pure logic, no dependency beyond System: minimal JSONC tokenizer, surgical edit of the top-level "workbench.colorCustomizations" key (everything else stays byte-identical), and Material token -> VS Code colour mapping.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PanneauxGlass;

internal static class VsCodeThemePolicy
{
    internal const string Key = "workbench.colorCustomizations";

    sealed record Member(string Name, int KeyStart, int ValueStart, int ValueEnd, int CommaEnd);
    sealed record Doc(bool Empty, int Open, int Close, List<Member> Members);

    // ---------- tokenizer / validating JSONC reader ----------
    sealed class Reader
    {
        readonly string s; int i; internal string Error = "";
        internal Reader(string text)
        {
            s = text;
        }
        internal int Pos => i;
        bool Fail(string message)
        {
            if (Error.Length == 0)
                Error = message + " (offset " + i + ")";
            return false;
        }
        internal bool Skip()
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == (char)0xFEFF)
                    i++;
                else if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    i += 2;
                    while (i < s.Length && s[i] != '\n' && s[i] != '\r')
                        i++;
                }
                else if (c == '/' && i + 1 < s.Length && s[i + 1] == '*')
                {
                    int end = s.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    if (end < 0)
                        return Fail("unterminated comment");
                    i = end + 2;
                }
                else
                    break;
            }
            return true;
        }
        internal bool AtEnd()
        {
            return i >= s.Length;
        }
        internal bool Peek(char c)
        {
            return i < s.Length && s[i] == c;
        }
        internal void Advance()
        {
            i++;
        }
        internal bool Str(out string value)
        {
            value = "";
            if (!Peek('"'))
                return Fail("string expected");
            var sb = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"')
                {
                    value = sb.ToString();
                    return true;
                }
                if (c == '\n' || c == '\r')
                    return Fail("newline in string");
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (i >= s.Length)
                    break;
                char e = s[i++];
                switch (e)
                {
                    case '"':
                        sb.Append('"');
                        break;
                    case '\\':
                        sb.Append('\\');
                        break;
                    case '/':
                        sb.Append('/');
                        break;
                    case 'b':
                        sb.Append('\b');
                        break;
                    case 'f':
                        sb.Append('\f');
                        break;
                    case 'n':
                        sb.Append('\n');
                        break;
                    case 'r':
                        sb.Append('\r');
                        break;
                    case 't':
                        sb.Append('\t');
                        break;
                    case 'u':
                        if (i + 4 > s.Length || !ushort.TryParse(s.AsSpan(i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ushort code))
                            return Fail("bad unicode escape");
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default:
                        return Fail("bad escape");
                }
            }
            return Fail("unterminated string");
        }
        internal bool Value(int depth)
        {
            if (depth > 64)
                return Fail("too deep");
            if (!Skip())
                return false;
            if (i >= s.Length)
                return Fail("value expected");
            char c = s[i];
            if (c == '"')
                return Str(out _);
            if (c == '{')
                return Object(depth, null);
            if (c == '[')
                return Array(depth);
            if (c == '-' || (c >= '0' && c <= '9'))
            {
                int start = i;
                while (i < s.Length && (char.IsAsciiDigit(s[i]) || s[i] is '-' or '+' or '.' or 'e' or 'E'))
                    i++;
                return double.TryParse(s.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? true : Fail("bad number");
            }
            foreach (var literal in new[] { "true", "false", "null" })
                if (string.CompareOrdinal(s, i, literal, 0, literal.Length) == 0)
                {
                    i += literal.Length;
                    return true;
                }
            return Fail("unexpected character");
        }
        bool Array(int depth)
        {
            i++;
            while (true)
            {
                if (!Skip())
                    return false;
                if (Peek(']'))
                {
                    i++;
                    return true;
                }
                if (!Value(depth + 1))
                    return false;
                if (!Skip())
                    return false;
                if (Peek(','))
                {
                    i++;
                    continue;
                }
                if (Peek(']'))
                {
                    i++;
                    return true;
                }
                return Fail("',' or ']' expected");
            }
        }
        // members != null: record the members of this (top-level) object.
        internal bool Object(int depth, List<Member>? members)
        {
            i++;
            while (true)
            {
                if (!Skip())
                    return false;
                if (Peek('}'))
                {
                    i++;
                    return true;
                }
                int keyStart = i;
                if (!Str(out string name))
                    return false;
                if (!Skip())
                    return false;
                if (!Peek(':'))
                    return Fail("':' expected");
                i++;
                if (!Skip())
                    return false;
                int valueStart = i;
                if (!Value(depth + 1))
                    return false;
                int valueEnd = i, comma = -1;
                if (!Skip())
                    return false;
                if (Peek(','))
                {
                    i++;
                    comma = i;
                    members?.Add(new Member(name, keyStart, valueStart, valueEnd, comma));
                    continue;
                }
                members?.Add(new Member(name, keyStart, valueStart, valueEnd, -1));
                if (Peek('}'))
                {
                    i++;
                    return true;
                }
                return Fail("',' or '}' expected");
            }
        }
    }

    static bool Parse(string text, out Doc doc, out string error)
    {
        doc = new Doc(true, -1, -1, new List<Member>());
        error = "";
        var r = new Reader(text);
        if (!r.Skip())
        {
            error = r.Error;
            return false;
        }
        if (r.AtEnd())
            return true;   // empty file (whitespace, BOM or comments only)
        if (!r.Peek('{'))
        {
            error = "settings.json must be a JSON object.";
            return false;
        }
        int open = r.Pos;
        var members = new List<Member>();
        if (!r.Object(0, members))
        {
            error = r.Error;
            return false;
        }
        int close = r.Pos - 1;
        if (!r.Skip())
        {
            error = r.Error;
            return false;
        }
        if (!r.AtEnd())
        {
            error = "Unexpected content after the closing brace.";
            return false;
        }
        doc = new Doc(false, open, close, members);
        return true;
    }

    static Member? Find(Doc doc)
    {
        Member? found = null;
        foreach (var m in doc.Members)
            if (m.Name == Key)
                found = m;
        return found;
    }

    // ---------- layout helpers ----------
    static string Eol(string text)
    {
        int n = text.IndexOf('\n');
        return n > 0 && text[n - 1] == '\r' ? "\r\n" : "\n";
    }
    static string LineIndent(string text, int pos)
    {
        int line = pos;
        while (line > 0 && text[line - 1] != '\n' && text[line - 1] != '\r')
            line--;
        int end = line;
        while (end < text.Length && (text[end] == ' ' || text[end] == '\t'))
            end++;
        return text.Substring(line, end - line);
    }
    static bool OnlyWhitespace(string text, int from, int to)
    {
        for (int k = from; k < to; k++)
            if (text[k] is not (' ' or '\t' or '\r' or '\n'))
                return false;
        return true;
    }
    static string MemberIndent(string text, Doc doc)
    {
        if (doc.Members.Count > 0)
        {
            var m = doc.Members[0];
            if (OnlyWhitespace(text, LineStartOf(text, m.KeyStart), m.KeyStart))
                return LineIndent(text, m.KeyStart);
        }
        return "  ";
    }
    static int LineStartOf(string text, int pos)
    {
        while (pos > 0 && text[pos - 1] != '\n' && text[pos - 1] != '\r')
            pos--;
        return pos;
    }

    // ---------- public edit API ----------
    // Raw JSON text of the key's current value; null when the key is absent.
    internal static bool TryGetValue(string text, out string? raw, out string error)
    {
        raw = null;
        if (!Parse(text, out var doc, out error))
            return false;
        var m = Find(doc);
        if (m != null)
            raw = text.Substring(m.ValueStart, m.ValueEnd - m.ValueStart);
        return true;
    }

    // Builds the colour object for this file's layout and sets it.
    internal static bool TryApplyColors(string text, IReadOnlyDictionary<string, string> colors, out string result, out string error)
    {
        result = text;
        if (!Parse(text, out var doc, out error))
            return false;
        string eol = Eol(text), indent = MemberIndent(text, doc);
        var m = Find(doc);
        string baseIndent = m != null ? LineIndent(text, m.KeyStart) : indent;
        var sb = new StringBuilder("{");
        bool first = true;
        foreach (var pair in colors)
        {
            sb.Append(first ? "" : ",").Append(eol).Append(baseIndent).Append("  ").Append(Quote(pair.Key)).Append(": ").Append(Quote(pair.Value));
            first = false;
        }
        sb.Append(eol).Append(baseIndent).Append('}');
        return Set(text, doc, sb.ToString(), out result, out error);
    }

    // Puts back an exact raw value (as saved before our first write).
    internal static bool TrySetRaw(string text, string raw, out string result, out string error)
    {
        result = text;
        if (!Parse(text, out var doc, out error))
            return false;
        return Set(text, doc, raw, out result, out error);
    }

    static bool Set(string text, Doc doc, string raw, out string result, out string error)
    {
        error = "";
        result = text;
        string eol = Eol(text);
        var m = Find(doc);
        if (m != null)
        {
            result = text.Substring(0, m.ValueStart) + raw + text.Substring(m.ValueEnd);
            return true;
        }
        string member = Quote(Key) + ": " + raw;
        if (doc.Empty)
        {
            // Keep BOM and any leading comments; the object follows them.
            string head = text.TrimEnd(' ', '\t', '\r', '\n');
            result = head + (head.Length > 0 && head != ((char)0xFEFF).ToString() ? eol : "") + "{" + eol + "  " + member + eol + "}" + eol;
            return true;
        }
        string indent = MemberIndent(text, doc);
        if (doc.Members.Count == 0)
        {
            bool multiline = text.IndexOf((char)10, doc.Open, doc.Close - doc.Open) >= 0;
            result = text.Substring(0, doc.Open + 1) + eol + indent + member + (multiline ? "" : eol) + text.Substring(doc.Open + 1);
            return true;
        }
        var last = doc.Members[^1];
        if (last.CommaEnd >= 0)
            result = text.Substring(0, last.CommaEnd) + eol + indent + member + "," + text.Substring(last.CommaEnd);
        else
            result = text.Substring(0, last.ValueEnd) + "," + eol + indent + member + text.Substring(last.ValueEnd);
        return true;
    }

    // Removes only our key (and its comma / indentation); absent key = unchanged text.
    internal static bool TryRemove(string text, out string result, out string error)
    {
        result = text;
        if (!Parse(text, out var doc, out error))
            return false;
        var m = Find(doc);
        if (m == null)
            return true;
        int index = doc.Members.IndexOf(m);
        int wsStart = m.KeyStart;
        while (wsStart > 0 && text[wsStart - 1] is ' ' or '\t' or '\r' or '\n')
            wsStart--;
        if (m.CommaEnd >= 0)
        {
            result = text.Remove(wsStart, m.CommaEnd - wsStart);
            return true;
        }
        if (index > 0)
        {
            var prev = doc.Members[index - 1];
            if (prev.CommaEnd >= 0 && OnlyWhitespace(text, prev.CommaEnd, m.KeyStart))
            {
                result = text.Remove(prev.CommaEnd - 1, m.ValueEnd - (prev.CommaEnd - 1));
                return true;
            }
        }
        else if (OnlyWhitespace(text, doc.Open + 1, m.KeyStart) && text.Substring(m.ValueEnd, doc.Close - m.ValueEnd) == Eol(text))
        {
            result = text.Remove(doc.Open + 1, doc.Close - doc.Open - 1);
            return true;
        }
        result = text.Remove(wsStart, m.ValueEnd - wsStart);
        return true;
    }

    // True when the current value is an object made only of keys this feature writes (safe to remove without a backup).
    internal static bool LooksOwned(string text)
    {
        if (!Parse(text, out var doc, out _))
            return false;
        var m = Find(doc);
        if (m == null)
            return false;
        string value = text.Substring(m.ValueStart, m.ValueEnd - m.ValueStart);
        var r = new Reader(value);
        var keys = new List<Member>();
        if (!r.Skip() || !r.Peek('{') || !r.Object(0, keys) || keys.Count == 0)
            return false;
        foreach (var k in keys)
            if (Array.IndexOf(OwnedKeys, k.Name) < 0)
                return false;
        return true;
    }

    internal static string Quote(string value)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in value)
        {
            if (c == '"')
                sb.Append("\\\"");
            else if (c == '\\')
                sb.Append("\\\\");
            else if (c < ' ')
                sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else
                sb.Append(c);
        }
        return sb.Append('"').ToString();
    }

    // ---------- colour mapping ----------
    internal static readonly string[] OwnedKeys = {
        "foreground", "descriptionForeground", "errorForeground", "focusBorder", "selection.background", "textLink.foreground", "textLink.activeForeground",
        "editor.background", "editor.foreground", "editor.selectionBackground", "editor.lineHighlightBackground", "editorCursor.foreground", "editorLineNumber.foreground", "editorLineNumber.activeForeground",
        "editorGroup.border", "editorGroupHeader.tabsBackground", "editorWidget.background", "editorWidget.border",
        "sideBar.background", "sideBar.foreground", "sideBar.border", "sideBarTitle.foreground", "sideBarSectionHeader.background", "sideBarSectionHeader.foreground",
        "activityBar.background", "activityBar.foreground", "activityBar.inactiveForeground", "activityBar.border", "activityBar.activeBorder", "activityBarBadge.background", "activityBarBadge.foreground",
        "statusBar.background", "statusBar.foreground", "statusBar.border", "statusBar.noFolderBackground", "statusBar.debuggingBackground", "statusBar.debuggingForeground",
        "titleBar.activeBackground", "titleBar.activeForeground", "titleBar.inactiveBackground", "titleBar.inactiveForeground", "titleBar.border",
        "tab.activeBackground", "tab.activeForeground", "tab.inactiveBackground", "tab.inactiveForeground", "tab.border", "tab.activeBorderTop", "tab.hoverBackground",
        "panel.background", "panel.border", "panelTitle.activeForeground", "panelTitle.inactiveForeground", "panelTitle.activeBorder",
        "terminal.background", "terminal.foreground", "terminalCursor.foreground", "terminal.selectionBackground",
        "list.activeSelectionBackground", "list.activeSelectionForeground", "list.inactiveSelectionBackground", "list.hoverBackground", "list.highlightForeground", "list.focusOutline",
        "button.background", "button.foreground", "button.hoverBackground", "button.secondaryBackground", "button.secondaryForeground",
        "input.background", "input.foreground", "input.border", "input.placeholderForeground", "inputOption.activeBorder", "dropdown.background", "dropdown.foreground", "dropdown.border",
        "badge.background", "badge.foreground", "menu.background", "menu.foreground", "menu.selectionBackground", "menu.selectionForeground",
        "scrollbarSlider.background", "scrollbarSlider.hoverBackground", "scrollbarSlider.activeBackground", "progressBar.background"
    };

    static bool Hex(string? v, out int r, out int g, out int b)
    {
        r = g = b = 0;
        if (v == null || v.Length != 7 || v[0] != '#')
            return false;
        return int.TryParse(v.AsSpan(1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out r) && int.TryParse(v.AsSpan(3, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out g) && int.TryParse(v.AsSpan(5, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out b);
    }
    static string Blend(string a, string b, double f)
    {
        Hex(a, out int ar, out int ag, out int ab);
        Hex(b, out int br, out int bg, out int bb);
        int Mix(int x, int y) => (int)Math.Round(x * (1 - f) + y * f);
        return "#" + Mix(ar, br).ToString("x2", CultureInfo.InvariantCulture) + Mix(ag, bg).ToString("x2", CultureInfo.InvariantCulture) + Mix(ab, bb).ToString("x2", CultureInfo.InvariantCulture);
    }
    static double Lum(string c)
    {
        Hex(c, out int r, out int g, out int b);
        double F(int v)
        {
            double x = v / 255.0;
            return x <= .03928 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4);
        }
        return .2126 * F(r) + .7152 * F(g) + .0722 * F(b);
    }
    internal static double Contrast(string a, string b)
    {
        double x = Lum(a), y = Lum(b);
        if (x < y)
            (x, y) = (y, x);
        return (x + .05) / (y + .05);
    }
    // Moves a foreground toward the main text colour until it reaches the ratio against its background.
    static string Ensure(string fg, string bg, string text, double ratio)
    {
        if (Contrast(fg, bg) >= ratio)
            return fg;
        for (int step = 1; step <= 10; step++)
        {
            string c = Blend(fg, text, step / 10.0);
            if (Contrast(c, bg) >= ratio)
                return c;
        }
        return text;
    }

    // Material tokens (m3surface, m3onSurface, m3primary...) -> VS Code colours. Null when the three base tokens are missing or not #rrggbb.
    internal static SortedDictionary<string, string>? Map(IReadOnlyDictionary<string, string>? palette)
    {
        if (palette == null)
            return null;
        string? T(string name)
        {
            return palette.TryGetValue(name, out var v) && Hex(v, out _, out _, out _) ? v.ToLowerInvariant() : null;
        }
        string? surface = T("m3surface"), text = T("m3onSurface"), primary = T("m3primary");
        if (surface == null || text == null || primary == null)
            return null;
        string onPrimary = T("m3onPrimary") ?? (Contrast(primary, surface) > 1 ? surface : text);
        if (Contrast(onPrimary, primary) < 4.5)
            onPrimary = Contrast(surface, primary) >= Contrast(text, primary) ? surface : text;
        string low = T("m3surfaceContainerLow") ?? Blend(surface, text, .03), container = T("m3surfaceContainer") ?? Blend(surface, text, .06);
        string high = T("m3surfaceContainerHigh") ?? Blend(surface, text, .10), highest = T("m3surfaceContainerHighest") ?? Blend(surface, text, .14);
        string variant = T("m3onSurfaceVariant") ?? Blend(surface, text, .76);
        string outline = T("m3outline") ?? Blend(surface, text, .54), outlineVariant = T("m3outlineVariant") ?? Blend(surface, text, .2);
        string Fg(string c, string bg) => Ensure(c, bg, text, 4.5);
        string Dim(string c, string bg) => Ensure(c, bg, text, 3.0);
        string Alpha(string c, string a) => c + a;
        var k = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["foreground"] = text,
            ["descriptionForeground"] = Fg(variant, surface),
            ["focusBorder"] = primary,
            ["selection.background"] = Alpha(primary, "59"),
            ["textLink.foreground"] = Fg(primary, surface),
            ["textLink.activeForeground"] = Fg(primary, surface),
            ["editor.background"] = surface,
            ["editor.foreground"] = text,
            ["editor.selectionBackground"] = Alpha(primary, "40"),
            ["editor.lineHighlightBackground"] = low,
            ["editorCursor.foreground"] = primary,
            ["editorLineNumber.foreground"] = Dim(outline, surface),
            ["editorLineNumber.activeForeground"] = text,
            ["editorGroup.border"] = outlineVariant,
            ["editorGroupHeader.tabsBackground"] = low,
            ["editorWidget.background"] = high,
            ["editorWidget.border"] = outlineVariant,
            ["sideBar.background"] = low,
            ["sideBar.foreground"] = Fg(variant, low),
            ["sideBar.border"] = outlineVariant,
            ["sideBarTitle.foreground"] = Fg(text, low),
            ["sideBarSectionHeader.background"] = container,
            ["sideBarSectionHeader.foreground"] = Fg(text, container),
            ["activityBar.background"] = container,
            ["activityBar.foreground"] = Fg(text, container),
            ["activityBar.inactiveForeground"] = Dim(outline, container),
            ["activityBar.border"] = outlineVariant,
            ["activityBar.activeBorder"] = primary,
            ["activityBarBadge.background"] = primary,
            ["activityBarBadge.foreground"] = onPrimary,
            ["statusBar.background"] = container,
            ["statusBar.foreground"] = Fg(variant, container),
            ["statusBar.border"] = outlineVariant,
            ["statusBar.noFolderBackground"] = container,
            ["statusBar.debuggingBackground"] = primary,
            ["statusBar.debuggingForeground"] = onPrimary,
            ["titleBar.activeBackground"] = container,
            ["titleBar.activeForeground"] = Fg(text, container),
            ["titleBar.inactiveBackground"] = low,
            ["titleBar.inactiveForeground"] = Dim(outline, low),
            ["titleBar.border"] = outlineVariant,
            ["tab.activeBackground"] = surface,
            ["tab.activeForeground"] = text,
            ["tab.inactiveBackground"] = low,
            ["tab.inactiveForeground"] = Fg(variant, low),
            ["tab.border"] = outlineVariant,
            ["tab.activeBorderTop"] = primary,
            ["tab.hoverBackground"] = container,
            ["panel.background"] = surface,
            ["panel.border"] = outlineVariant,
            ["panelTitle.activeForeground"] = text,
            ["panelTitle.inactiveForeground"] = Fg(variant, surface),
            ["panelTitle.activeBorder"] = primary,
            ["terminal.background"] = surface,
            ["terminal.foreground"] = text,
            ["terminalCursor.foreground"] = primary,
            ["terminal.selectionBackground"] = Alpha(primary, "40"),
            ["list.activeSelectionBackground"] = highest,
            ["list.activeSelectionForeground"] = Fg(text, highest),
            ["list.inactiveSelectionBackground"] = high,
            ["list.hoverBackground"] = container,
            ["list.highlightForeground"] = Fg(primary, surface),
            ["list.focusOutline"] = primary,
            ["button.background"] = primary,
            ["button.foreground"] = onPrimary,
            ["button.hoverBackground"] = Blend(primary, onPrimary, .12),
            ["button.secondaryBackground"] = high,
            ["button.secondaryForeground"] = Fg(text, high),
            ["input.background"] = high,
            ["input.foreground"] = Fg(text, high),
            ["input.border"] = outlineVariant,
            ["input.placeholderForeground"] = Dim(outline, high),
            ["inputOption.activeBorder"] = primary,
            ["dropdown.background"] = high,
            ["dropdown.foreground"] = Fg(text, high),
            ["dropdown.border"] = outlineVariant,
            ["badge.background"] = primary,
            ["badge.foreground"] = onPrimary,
            ["menu.background"] = high,
            ["menu.foreground"] = Fg(text, high),
            ["menu.selectionBackground"] = highest,
            ["menu.selectionForeground"] = Fg(text, highest),
            ["scrollbarSlider.background"] = Alpha(outlineVariant, "80"),
            ["scrollbarSlider.hoverBackground"] = Alpha(outline, "80"),
            ["scrollbarSlider.activeBackground"] = Alpha(primary, "80"),
            ["progressBar.background"] = primary
        };
        string? error = T("m3error");
        if (error != null)
            k["errorForeground"] = Fg(error, surface);
        return k;
    }
}
