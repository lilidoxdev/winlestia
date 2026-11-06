// C#12 / AudioBars: silence, sine in the right band, no NaN, bounds 0-1, smoothing (pure functions).
using System;
using System.Collections.Generic;
using PanneauxGlass;
class Test
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
    static int Main()
    {
        var r = new Dictionary<string, bool>();
        var a = new AudioBars();
        a.Update(new float[AudioBars.FftSize], 48000);
        r["silenceIsZero"] = a.Silent && a.Levels.Length == AudioBars.Bars;
        a.Update(ReadOnlySpan<float>.Empty, 48000);
        r["emptyInputIsZero"] = a.Silent;
        foreach (var hz in new[] { 100.0, 440, 1000, 4000, 10000 })
        {
            foreach (var rate in new[] { 44100, 48000 })
            {
                var b = new AudioBars();
                var s = Sine(hz, rate, 4096, 0.8);
                var raw = b.Analyze(s, rate);
                int got = Peak(raw), want = AudioBars.BandOf(hz, rate);
                r["sine" + hz + "at" + rate] = Math.Abs(got - want) <= 1 && raw[got] > 0.5;
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
            r["sineIsNarrow"] = far;
        }
        {
            var b = new AudioBars();
            var s = Sine(440, 48000, 2048, 100);
            b.Update(s, 48000);
            r["loudStaysBounded"] = Bounded(b.Levels);
        }
        {
            var b = new AudioBars();
            var s = Sine(440, 48000, 2048, 0.5);
            s[10] = float.NaN;
            s[20] = float.PositiveInfinity;
            s[30] = float.NegativeInfinity;
            b.Update(s, 48000);
            r["noNaN"] = Bounded(b.Levels);
        }
        {
            var b = new AudioBars();
            b.Update(new float[] { float.NaN, float.NaN }, 48000);
            r["onlyNaNIsZero"] = b.Silent;
        }
        {
            var b = new AudioBars();
            b.Update(Sine(440, 48000, 1024, 0.8), 48000);
            b.Update(Sine(440, 48000, 1024, 0.8), 0);
            r["badRateNoThrow"] = Bounded(b.Levels);
        }
        {
            var b = new AudioBars();
            b.Update(Sine(1000, 48000, 1024, 0.8), 48000);
            double first = b.Levels[AudioBars.BandOf(1000, 48000)];
            var z = new float[1024];
            b.Update(z, 48000);
            double after = b.Levels[AudioBars.BandOf(1000, 48000)];
            r["riseFast"] = first > 0.3;
            r["fallSlow"] = after > 0 && after < first && after > first * 0.5;
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
            r["decayReachesExactZero"] = b.Silent && n < 60;
        }
        {
            var b = new AudioBars();
            b.Update(Sine(1000, 48000, 1024, 0.8), 48000);
            b.Reset();
            r["resetClears"] = b.Silent;
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
            r["noiseBounded"] = ok;
        }
        int ok2 = 0;
        foreach (var v in r.Values)
        if (v)
            ok2++;
        foreach (var k in r)
        if (!k.Value)
            Console.WriteLine("FAIL " + k.Key);
        Console.WriteLine("TOTAL " + ok2 + "/" + r.Count);
        return ok2 == r.Count ? 0 : 1;
    }
}
