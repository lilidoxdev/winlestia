// language: C# 12, file: LockSecret.cs, target: Windows 11 / .NET 8, empreinte PBKDF2 du mot de passe de l'écran de verrouillage
using System;
using System.Security.Cryptography;
using System.Text;
namespace PanneauxGlass;

// Le mot de passe n'est jamais conservé ni journalisé : seuls le sel et l'empreinte (base64) sont stockés dans les préférences.
internal static class LockSecret
{
    internal const int MinLength = 4, MaxLength = 128, SaltBytes = 16, HashBytes = 32, DefaultIterations = 200000;
    const int MinIterations = 1000, MaxIterations = 5_000_000;

    internal static bool ValidLength(string? password) => password != null && password.Length is >= MinLength and <= MaxLength;

    // Nouveau sel aléatoire à chaque appel : deux mots de passe identiques donnent deux empreintes différentes.
    internal static (string Hash, string Salt, int Iterations) Create(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Derive(password, salt, DefaultIterations, HashBytes);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt), DefaultIterations);
    }

    // Vrai si les données stockées sont exploitables (présentes, base64 valides, tailles et itérations plausibles).
    internal static bool IsUsable(string? hash, string? salt, int iterations)
        => iterations is >= MinIterations and <= MaxIterations && Decode(hash, HashBytes) != null && Decode(salt, SaltBytes) != null;

    internal static bool Verify(string? password, string? hash, string? salt, int iterations)
    {
        if (password == null || !IsUsable(hash, salt, iterations))
            return false;
        try
        {
            byte[] expected = Decode(hash, HashBytes)!;
            byte[] actual = Derive(password, Decode(salt, SaltBytes)!, iterations, expected.Length);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (Exception)
        {
            return false;
        }
    }

    static byte[] Derive(string password, byte[] salt, int iterations, int length)
        => Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256, length);

    static byte[]? Decode(string? value, int expectedLength)
    {
        if (string.IsNullOrEmpty(value))
            return null;
        try
        {
            byte[] bytes = Convert.FromBase64String(value);
            return bytes.Length == expectedLength ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
