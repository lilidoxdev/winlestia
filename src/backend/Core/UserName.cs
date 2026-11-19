// language: C# 12, file: UserName.cs, target: Windows 11 / .NET 8, prénom de l'utilisateur Windows pour les libellés
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace PanneauxGlass;

// Prénom affiché dans les salutations : nom d'affichage Windows, sinon nom de session. Lu une seule fois.
internal static class UserName
{
    static string? cached;
    public static string First => cached ??= FirstOf(DisplayName(), Environment.UserName);

    // Partie pure : prénom du nom d'affichage, sinon repli sur le nom de session.
    // Active Directory rend parfois « Nom, Prénom » : la virgule inverse l'ordre, donc on prend ce qui la suit.
    internal static string FirstOf(string? display, string? fallback)
    {
        string name = (display ?? "").Trim();
        int comma = name.IndexOf(',');
        if (comma >= 0)
            name = name[(comma + 1)..];
        string first = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
        if (first.Length == 0)
            first = (fallback ?? "").Trim();
        return first.Length == 0 ? "toi" : first;
    }

    static string? DisplayName()
    {
        try
        {
            int size = 256;
            var sb = new StringBuilder(size);
            return GetUserNameEx(3, sb, ref size) ? sb.ToString() : null;
        }
        catch (Exception) { return null; }
    }

    [DllImport("secur32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetUserNameEx(int nameFormat, StringBuilder name, ref int size);
}
