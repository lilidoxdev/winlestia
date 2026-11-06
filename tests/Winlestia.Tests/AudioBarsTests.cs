// C#12 / AudioBars: silence, sine in the right band, no NaN, bounds 0-1, smoothing (pure functions).
using System;
using System.Collections.Generic;
using PanneauxGlass;
namespace Winlestia.Tests;
public class AudioBarsTests
{
    static float[] Sine(double hz, int rate, int n, double amp)
    {
        var s = new float[n];
        for (int i = 0; i < n; i++)
            s[i] = (float)(amp * Math.Sin(2 * Math.PI * hz * i / rate));
        return s;
    }
    static int Peak(ReadOnlySpan<double> v)
    {
        int best = 0;
        for (int i = 1; i < v.Length; i++)
        if (v[i] > v[best])
            best = i;
        return best;
    }
    static bool Bounded(ReadOnlySpan<double> v)
    {
        foreach (var x in v)
        if (!double.IsFinite(x) || x < 0 || x > 1)
            return false;
        return true;
    }
    [Fact]
    public void AllChecks()
    {
        var a = new AudioBars();
        a.Update(new float[AudioBars.FftSize], 48000);
        Assert.True(a.Silent && a.Levels.Length == AudioBars.Bars, "silenceIsZero");
        a.Update(ReadOnlySpan<float>.Empty, 48000);
        Assert.True(a.Silent, "emptyInputIsZero");
        foreach (var hz in new[] { 100.0, 440, 1000, 4000, 10000 })
        {
            foreach (var rate in new[] { 44100, 48000 })
            {
                var b = new AudioBars();
                var s = Sine(hz, rate, 4096, 0.8);
                var raw = b.Analyze(s, rate);
                int got = Peak(raw), want = AudioBars.BandOf(hz, rate);
                Assert.True(Math.Abs(got - want) <= 1 && raw[got] > 0.5, "sine" + hz + "at" + rate);
            }
        }
        {
            var b = new AudioBars();
            var raw = b.Analyze(Sine(1000, 48000, 1024, 0.8), 48000);
            int p = Peak(raw);
            bool far = true;
            for (int i = 0; i < raw.Length; i++)
            if (Math.Abs(i - p) > 5 && raw[i] > raw[p] * 0.6)
                far = false;
            Assert.True(far, "sineIsNarrow");
        }
        {
            var b = new AudioBars();
            var s = Sine(440, 48000, 2048, 100);
            b.Update(s, 48000);
            Assert.True(Bounded(b.Levels), "loudStaysBounded");
        }
        {
            var b = new AudioBars();
            var s = Sine(440, 48000, 2048, 0.5);
            s[10] = float.NaN;
            s[20] = float.PositiveInfinity;
            s[30] = float.NegativeInfinity;
            b.Update(s, 48000);
            Assert.True(Bounded(b.Levels), "noNaN");
        }
        {
            var b = new AudioBars();
            b.Update(new float[] { float.NaN, float.NaN }, 48000);
            Assert.True(b.Silent, "onlyNaNIsZero");
        }
        {
            var b = new AudioBars();
            b.Update(Sine(440, 48000, 1024, 0.8), 48000);
            b.Update(Sine(440, 48000, 1024, 0.8), 0);
            Assert.True(Bounded(b.Levels), "badRateNoThrow");
        }
        {
            var b = new AudioBars();
            b.Update(Sine(1000, 48000, 1024, 0.8), 48000);
            double first = b.Levels[AudioBars.BandOf(1000, 48000)];
            var z = new float[1024];
            b.Update(z, 48000);
            double after = b.Levels[AudioBars.BandOf(1000, 48000)];
            Assert.True(first > 0.3, "riseFast");
            Assert.True(after > 0 && after < first && after > first * 0.5, "fallSlow");
        }
        {
            var b = new AudioBars();
            b.Update(Sine(1000, 48000, 1024, 0.8), 48000);
            int n = 0;
            while (!b.Silent && n < 500)
            {
                b.Decay();
                n++;
            }
            Assert.True(b.Silent && n < 60, "decayReachesExactZero");
        }
        {
            var b = new AudioBars();
            b.Update(Sine(1000, 48000, 1024, 0.8), 48000);
            b.Reset();
            Assert.True(b.Silent, "resetClears");
        }
        {
            var b = new AudioBars();
            bool ok = true;
            var rnd = new Random(7);
            for (int k = 0; k < 50; k++)
            {
                var s = new float[rnd.Next(0, 3000)];
                for (int i = 0; i < s.Length; i++)
                    s[i] = (float)(rnd.NextDouble() * 2 - 1);
                b.Update(s, rnd.Next(0, 2) == 0 ? 44100 : 96000);
                ok &= Bounded(b.Levels);
            }
            Assert.True(ok, "noiseBounded");
        }
    }
}
