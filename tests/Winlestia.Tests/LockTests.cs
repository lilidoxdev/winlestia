// Ecran de verrouillage : empreinte du mot de passe, essais, inactivite, reglages, touches bloquees (aucun acces au bureau).
using System;
using PanneauxGlass;
namespace Winlestia.Tests;
public class LockTests
{
    static Preferences WithPassword(string password = "secret")
    {
        var p = new Preferences();
        Assert.Equal("ok", LockRules.SetPassword(p, "", password));
        return p;
    }

    [Fact]
    public void WatchdogReleasesOnlyAnUnacknowledgedLock()
    {
        Assert.Equal(8, LockRules.WatchdogSeconds);
        Assert.True(LockRules.WatchdogShouldRelease(true, false));
        Assert.False(LockRules.WatchdogShouldRelease(true, true));
        Assert.False(LockRules.WatchdogShouldRelease(false, false));
    }

    [Fact]
    public void StaleGenerationIsNotCurrent()
    {
        Assert.True(LockRules.IsCurrent(3, 3));
        Assert.False(LockRules.IsCurrent(3, 5));
    }

    [Fact]
    public void RetryAfterIsRemainingSecondsFromDeadline()
    {
        var a = new LockAttempts();
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < LockAttempts.FreeAttempts + 1; i++)
            a.Fail(t0);
        Assert.Equal(30, a.RetryAfter(t0));
        Assert.Equal(20, a.RetryAfter(t0.AddSeconds(10)));
        Assert.Equal(0, a.RetryAfter(t0.AddSeconds(30)));
    }

    [Fact]
    public void SecretRoundTripAndWrongPassword()
    {
        var (hash, salt, iterations) = LockSecret.Create("motdepasse");
        Assert.Equal(200000, iterations);
        Assert.True(LockSecret.Verify("motdepasse", hash, salt, iterations));
        Assert.False(LockSecret.Verify("motdepassE", hash, salt, iterations));
        Assert.False(LockSecret.Verify("", hash, salt, iterations));
        Assert.False(LockSecret.Verify(null, hash, salt, iterations));
    }

    [Fact]
    public void DifferentSaltsGiveDifferentHashes()
    {
        var a = LockSecret.Create("meme-mot-de-passe");
        var b = LockSecret.Create("meme-mot-de-passe");
        Assert.NotEqual(a.Salt, b.Salt);
        Assert.NotEqual(a.Hash, b.Hash);
        Assert.Equal(16, Convert.FromBase64String(a.Salt).Length);
        Assert.Equal(32, Convert.FromBase64String(a.Hash).Length);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(3, false)]
    [InlineData(4, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void PasswordLengthLimits(int length, bool valid)
        => Assert.Equal(valid, LockSecret.ValidLength(new string('a', length)));

    [Fact]
    public void CorruptDataIsNotUsableAndNeverVerifies()
    {
        var (hash, salt, iterations) = LockSecret.Create("abcd");
        Assert.True(LockSecret.IsUsable(hash, salt, iterations));
        Assert.False(LockSecret.IsUsable("", salt, iterations));
        Assert.False(LockSecret.IsUsable(hash, "pas du base64 !", iterations));
        Assert.False(LockSecret.IsUsable(hash, salt, 0));
        Assert.False(LockSecret.Verify("abcd", hash, "AAAA", iterations));
    }

    [Fact]
    public void SetPasswordRules()
    {
        var p = new Preferences();
        Assert.Equal("invalid", LockRules.SetPassword(p, "", "abc"));
        Assert.False(LockRules.HasPassword(p));
        Assert.Equal("ok", LockRules.SetPassword(p, "", "abcd"));
        Assert.True(LockRules.HasPassword(p));
        Assert.Equal("wrong", LockRules.SetPassword(p, "nope", "nouveau-mdp"));
        Assert.Equal("invalid", LockRules.SetPassword(p, "abcd", "abc"));
        Assert.Equal("ok", LockRules.SetPassword(p, "abcd", "nouveau-mdp"));
        Assert.True(LockSecret.Verify("nouveau-mdp", p.LockHash, p.LockSalt, p.LockIterations));
    }

    [Fact]
    public void EnabledIsRefusedWithoutPassword()
    {
        var p = new Preferences();
        LockRules.ApplySettings(p, true, null);
        Assert.False(p.LockEnabled);
        var q = WithPassword();
        LockRules.ApplySettings(q, true, null);
        Assert.True(q.LockEnabled);
        LockRules.ApplySettings(q, false, null);
        Assert.False(q.LockEnabled);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    [InlineData(1, 30)]
    [InlineData(29, 30)]
    [InlineData(30, 30)]
    [InlineData(600, 600)]
    [InlineData(86400, 86400)]
    [InlineData(999999, 86400)]
    public void IdleSecondsAreClamped(int input, int expected)
    {
        Assert.Equal(expected, LockRules.ClampIdle(input));
        var p = new Preferences();
        LockRules.ApplySettings(p, null, input);
        Assert.Equal(expected, p.LockIdleSeconds);
    }

    [Fact]
    public void DefaultsAreOffAndCtrlAltL()
    {
        var p = new Preferences();
        Assert.False(p.LockEnabled);
        Assert.Equal(0, p.LockIdleSeconds);
        Assert.Equal((3u, 0x4Cu), (p.LockShortcut.Modifiers, p.LockShortcut.Key));
        Assert.NotNull(ShortcutCatalog.Find("lock"));
    }

    [Fact]
    public void FailuresImposeDelayFromTheFifth()
    {
        var a = new LockAttempts();
        var t = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < 4; i++)
        {
            a.Fail(t);
            Assert.Equal(0, a.RetryAfter(t));
        }
        a.Fail(t);
        Assert.Equal(5, a.Failures);
        Assert.Equal(30, a.RetryAfter(t));
        Assert.Equal(20, a.RetryAfter(t.AddSeconds(10)));
        Assert.Equal(0, a.RetryAfter(t.AddSeconds(30)));
        a.Fail(t.AddSeconds(31));
        Assert.Equal(30, a.RetryAfter(t.AddSeconds(31)));
        a.Reset();
        Assert.Equal(0, a.Failures);
        Assert.Equal(0, a.RetryAfter(t.AddSeconds(31)));
    }

    [Fact]
    public void IdleSchedulingSingleTimer()
    {
        // inactif : aucun minuteur
        Assert.Equal(new IdleStep(false, null), LockRules.NextIdle(false, true, 60, false, 0));
        Assert.Equal(new IdleStep(false, null), LockRules.NextIdle(true, false, 60, false, 0));
        Assert.Equal(new IdleStep(false, null), LockRules.NextIdle(true, true, 0, false, 0));
        Assert.Equal(new IdleStep(false, null), LockRules.NextIdle(true, true, 60, true, 0));
        // temps restant avant le seuil
        Assert.Equal(new IdleStep(false, 60000), LockRules.NextIdle(true, true, 60, false, 0));
        Assert.Equal(new IdleStep(false, 20000), LockRules.NextIdle(true, true, 60, false, 40000));
        // plancher d'une seconde
        Assert.Equal(new IdleStep(false, 1000), LockRules.NextIdle(true, true, 60, false, 59900));
        // seuil atteint : verrouillage, sauf plein ecran (nouvel essai plus tard)
        Assert.Equal(new IdleStep(true, null), LockRules.NextIdle(true, true, 60, false, 60000));
        Assert.Equal(new IdleStep(false, LockRules.FullscreenRetryMs), LockRules.NextIdle(true, true, 60, false, 90000, true));
        // duree trop courte : ramenee a 30 s
        Assert.Equal(new IdleStep(false, 30000), LockRules.NextIdle(true, true, 5, false, 0));
    }

    [Theory]
    [InlineData(0x5B, false, false, true)]
    [InlineData(0x5C, false, false, true)]
    [InlineData(0x09, true, false, true)]
    [InlineData(0x09, false, false, false)]
    [InlineData(0x1B, true, false, true)]
    [InlineData(0x1B, false, true, true)]
    [InlineData(0x1B, false, false, false)]
    [InlineData(0x73, true, false, true)]
    [InlineData(0x73, false, false, false)]
    [InlineData(0x20, true, false, true)]
    [InlineData(0x20, false, false, false)]
    [InlineData(0x41, true, true, false)]
    public void BlockedKeys(uint vk, bool alt, bool ctrl, bool blocked)
        => Assert.Equal(blocked, LockKeyRules.Blocks(vk, alt, ctrl));
}
