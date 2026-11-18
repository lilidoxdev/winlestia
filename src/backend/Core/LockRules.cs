// language: C# 12, file: LockRules.cs, target: Windows 11 / .NET 8, règles pures de l'écran de verrouillage (sans accès au bureau)
using System;
namespace PanneauxGlass;

internal readonly record struct IdleStep(bool Lock, int? DelayMs);

// Échecs consécutifs : à partir du 5e, 30 s d'attente avant l'essai suivant.
internal sealed class LockAttempts
{
    internal const int FreeAttempts = 4, DelaySeconds = 30;
    DateTime until = DateTime.MinValue;
    internal int Failures
    {
        get; private set;
    }
    internal int RetryAfter(DateTime now) => now >= until ? 0 : (int)Math.Ceiling((until - now).TotalSeconds);
    internal void Fail(DateTime now)
    {
        Failures++;
        if (Failures > FreeAttempts)
            until = now.AddSeconds(DelaySeconds);
    }
    internal void Reset()
    {
        Failures = 0;
        until = DateTime.MinValue;
    }
}

internal static class LockRules
{
    internal const int MinIdle = 30, MaxIdle = 86400, FullscreenRetryMs = 60000, MinDelayMs = 1000;

    internal static int ClampIdle(int seconds) => seconds <= 0 ? 0 : Math.Clamp(seconds, MinIdle, MaxIdle);

    // Garde-fou : sans « lockShown » dans ce délai, le verrou est levé pour ne jamais enfermer l'utilisateur.
    internal const int WatchdogSeconds = 8;

    internal static bool WatchdogShouldRelease(bool locked, bool shown) => locked && !shown;

    // Une suite asynchrone ne vaut que pour le verrou (génération) qui l'a lancée.
    internal static bool IsCurrent(int captured, int current) => captured == current;

    internal static bool HasPassword(Preferences p) => LockSecret.IsUsable(p.LockHash, p.LockSalt, p.LockIterations);

    // Un seul minuteur à usage unique : durée restante avant le seuil, ou verrouillage immédiat si le seuil est atteint.
    // DelayMs == null et Lock == false : aucun minuteur à armer.
    internal static IdleStep NextIdle(bool enabled, bool hasPassword, int idleSeconds, bool locked, long idleMs, bool fullscreen = false)
    {
        int seconds = ClampIdle(idleSeconds);
        if (!enabled || !hasPassword || seconds == 0 || locked)
            return new(false, null);
        long remaining = seconds * 1000L - Math.Max(0, idleMs);
        if (remaining > 0)
            return new(false, (int)Math.Max(MinDelayMs, Math.Min(remaining, int.MaxValue)));
        return fullscreen ? new(false, FullscreenRetryMs) : new(true, null);
    }

    // enabled=true est refusé tant qu'aucun mot de passe n'existe ; la durée 0 désactive, sinon 30..86400 s.
    internal static void ApplySettings(Preferences p, bool? enabled, int? idleSeconds)
    {
        if (enabled is bool e)
            p.LockEnabled = e && HasPassword(p);
        if (idleSeconds is int s)
            p.LockIdleSeconds = ClampIdle(s);
    }

    // "ok" | "wrong" (mot de passe actuel incorrect) | "invalid" (longueur du nouveau mot de passe).
    internal static string SetPassword(Preferences p, string? current, string? password)
    {
        if (HasPassword(p) && !LockSecret.Verify(current, p.LockHash, p.LockSalt, p.LockIterations))
            return "wrong";
        if (!LockSecret.ValidLength(password))
            return "invalid";
        var (hash, salt, iterations) = LockSecret.Create(password!);
        p.LockHash = hash;
        p.LockSalt = salt;
        p.LockIterations = iterations;
        return "ok";
    }
}
