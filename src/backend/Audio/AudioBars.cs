// C# 12 / AudioBars.cs / DSP pur pour le visualiseur du tableau de bord : échantillons en entrée, niveaux de barres 0-1 en sortie. Aucune
// E/S, aucune API audio, seuls les niveaux sont conservés.
using System;

namespace PanneauxGlass;

internal sealed class AudioBars
{
    internal const int Bars = 28, FftSize = 1024;
    const double LowHz = 50, HighHz = 16000, FloorDb = -60, Rise = 0.6, Fall = 0.12, Epsilon = 0.004;

    readonly double[] re = new double[FftSize], im = new double[FftSize], window = new double[FftSize], raw = new double[Bars],
        levels = new double[Bars];
    readonly double[] cos = new double[FftSize / 2], sin = new double[FftSize / 2];

    internal AudioBars()
    {
        for (int i = 0; i < FftSize; i++)
            window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1));
        for (int i = 0; i < FftSize / 2; i++)
        {
            cos[i] = Math.Cos(2 * Math.PI * i / FftSize);
            sin[i] = -Math.Sin(2 * Math.PI * i / FftSize);
        }
    }

    internal ReadOnlySpan<double> Levels => levels;
    internal bool Silent
    {
        get
        {
            foreach (double v in levels)
                if (v > 0)
                    return false;
            return true;
        }
    }

    // Prend les FftSize derniers échantillons mono (complétés par des zéros s'il y en a moins) et met à jour les niveaux lissés : montée
    // rapide, descente lente.
    internal void Update(ReadOnlySpan<float> mono, int sampleRate)
    {
        Analyze(mono, sampleRate);
        for (int b = 0; b < Bars; b++)
        {
            double target = raw[b], v = levels[b];
            v += (target - v) * (target > v ? Rise : Fall);
            levels[b] = v < Epsilon && target == 0 ? 0 : Math.Clamp(v, 0, 1);
        }
    }

    // Sans entrée : les niveaux retombent vers zéro et l'atteignent exactement.
    internal void Decay()
    {
        for (int b = 0; b < Bars; b++)
        {
            double v = levels[b] * (1 - Fall * 2);
            levels[b] = v < Epsilon * 4 ? 0 : v;
        }
    }

    internal void Reset() => Array.Clear(levels);

    // Les bornes des bandes sont logarithmiques entre LowHz et min(HighHz, Nyquist). Renvoie les niveaux non lissés (valides jusqu'au
    // prochain appel).
    internal ReadOnlySpan<double> Analyze(ReadOnlySpan<float> mono, int sampleRate)
    {
        Array.Clear(raw);
        if (sampleRate < 8000 || sampleRate > 384000)
            return raw;
        int n = Math.Min(mono.Length, FftSize), skip = mono.Length - n;
        bool any = false;
        for (int i = 0; i < FftSize; i++)
        {
            double s = i < n ? mono[skip + i] : 0;
            if (!double.IsFinite(s))
                s = 0;
            if (s != 0)
                any = true;
            re[i] = s * window[i];
            im[i] = 0;
        }
        if (!any)
            return raw;
        Fft();
        double binHz = (double)sampleRate / FftSize, high = Math.Min(HighHz, sampleRate / 2.0 * 0.98), ratio = high / LowHz;
        for (int b = 0; b < Bars; b++)
        {
            double f0 = LowHz * Math.Pow(ratio, (double)b / Bars), f1 = LowHz * Math.Pow(ratio, (double)(b + 1) / Bars);
            int lo = Math.Clamp((int)Math.Floor(f0 / binHz), 1, FftSize / 2 - 1), hi = Math.Clamp((int)Math.Ceiling(f1 / binHz), lo + 1,
                FftSize / 2);
            double peak = 0;
            for (int k = lo; k < hi; k++)
                peak = Math.Max(peak, Math.Sqrt(re[k] * re[k] + im[k] * im[k]));
            double amplitude = peak / (FftSize / 4.0); // a full-scale sine reads about 1.0
            double db = 20 * Math.Log10(Math.Max(1e-9, amplitude));
            raw[b] = Math.Clamp((db - FloorDb) / -FloorDb, 0, 1);
        }
        return raw;
    }

    internal static int BandOf(double hz, int sampleRate)
    {
        double high = Math.Min(HighHz, sampleRate / 2.0 * 0.98);
        if (hz < LowHz || hz > high)
            return -1;
        return Math.Min(Bars - 1, (int)(Math.Log(hz / LowHz) / Math.Log(high / LowHz) * Bars));
    }

    void Fft()
    {
        for (int i = 1, j = 0; i < FftSize; i++)
        {
            int bit = FftSize >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (int len = 2; len <= FftSize; len <<= 1)
        {
            int half = len / 2, step = FftSize / len;
            for (int i = 0; i < FftSize; i += len)
                for (int k = 0; k < half; k++)
                {
                    double wr = cos[k * step], wi = sin[k * step];
                    int a = i + k, c = a + half;
                    double tr = re[c] * wr - im[c] * wi, ti = re[c] * wi + im[c] * wr;
                    re[c] = re[a] - tr;
                    im[c] = im[a] - ti;
                    re[a] += tr;
                    im[a] += ti;
                }
        }
    }
}
