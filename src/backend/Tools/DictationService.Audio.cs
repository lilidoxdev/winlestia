// C# 12 / .NET 8 / Windows 11. Dictée « appuyer pour parler » : hook clavier, micro à la demande, Whisper.net, saisie dans l'appli

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace PanneauxGlass;


internal sealed partial class DictationService : IDisposable
{
    void Stop()
    {
        if (!recording)
            return;
        recording = false;
        try
        {
            capture?.StopRecording();
        }
        catch (Exception ex) { Store.Log("Dictée: arrêt du micro: " + ex.Message); }
        lock (gate)
        {
            level = 0;
            Array.Clear(levels);
        }
        send("dictation:stop");
    }

    void OnData(WasapiCapture device, int id, WaveInEventArgs e)
    {
        if (id != recordingId || samples == null)
            return;
        var format = device.WaveFormat;
        int channels = Math.Max(1, format.Channels);
        int bytesPerSample = format.BitsPerSample / 8;
        int frames = e.BytesRecorded / (bytesPerSample * channels);
        bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat || (format is WaveFormatExtensible ext
            && ext.ToStandardWaveFormat().Encoding == WaveFormatEncoding.IeeeFloat);
        var mono = new float[frames];
        double energy = 0;
        for (int i = 0; i < frames; i++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++)
            {
                int offset = (i * channels + c) * bytesPerSample;
                sum += isFloat ? BitConverter.ToSingle(e.Buffer, offset)
                    : bytesPerSample == 2 ? BitConverter.ToInt16(e.Buffer, offset) / 32768f
                    : bytesPerSample == 3 ? ((e.Buffer[offset] << 8 | e.Buffer[offset + 1] << 16 | e.Buffer[offset
                        + 2] << 24) >> 8) / 8388608f
                    : BitConverter.ToInt32(e.Buffer, offset) / 2147483648f;
            }
            float v = sum / channels;
            mono[i] = v;
            energy += v * v;
        }
        samples.AddRange(mono);
        // Fenêtre glissante pour la FFT.
        foreach (float v in mono)
        {
            window[windowFill % Fft] = v;
            windowFill++;
        }
        UpdateLevels(frames > 0 ? Math.Sqrt(energy / frames) : 0);
        if (samples.Count >= sourceRate * MaxSeconds && recording)
            dispatcher.BeginInvoke(DispatcherPriority.Send, Stop);
    }

    void UpdateLevels(double rms)
    {
        double db = rms > 1e-6 ? 20 * Math.Log10(rms) : -120;
        double target = Math.Clamp((db + 60) / 60, 0, 1);
        double[] bands = new double[Bands];
        if (windowFill >= Fft)
        {
            for (int i = 0; i < Fft; i++)
            {
                float v = window[(windowFill + i) % Fft];
                double w = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Fft - 1));
                fftBuffer[i].X = (float)(v * w);
                fftBuffer[i].Y = 0;
            }
            FastFourierTransform.FFT(true, FftLog, fftBuffer);
            double binHz = (double)sourceRate / Fft;
            double low = 80, high = Math.Min(8000, sourceRate / 2.0 - 1);
            for (int b = 0; b < Bands; b++)
            {
                double f0 = low * Math.Pow(high / low, (double)b / Bands), f1 = low * Math.Pow(high / low, (double)(b + 1) / Bands);
                int i0 = Math.Max(1, (int)Math.Floor(f0 / binHz)), i1 = Math.Max(i0, (int)Math.Ceiling(f1 / binHz));
                double peak = 0;
                for (int i = i0; i <= i1 && i < Fft / 2; i++)
                {
                    double m = Math.Sqrt(fftBuffer[i].X * fftBuffer[i].X + fftBuffer[i].Y * fftBuffer[i].Y);
                    if (m > peak)
                        peak = m;
                }
                double bdb = peak > 1e-9 ? 20 * Math.Log10(peak) : -120;
                bands[b] = Math.Clamp((bdb + 75) / 60, 0, 1);
            }
        }
        lock (gate)
        {
            level = Smooth(level, target);
            for (int b = 0; b < Bands; b++)
                levels[b] = (float)Smooth(levels[b], bands[b]);
        }
    }
    static double Smooth(double current, double target) => target > current ? current + (target - current) * .6 : current
        + (target - current) * .12;

    void OnStopped(WasapiCapture device, int id, Exception? failure)
    {
        float[]? data = null;
        int rate = sourceRate;
        if (id == recordingId)
        {
            data = samples?.ToArray();
            samples = null;
            recording = false;
        }
        if (ReferenceEquals(capture, device))
            capture = null;
        // Libération hors du thread de capture.
        Task.Run(() => { Safe.Dispose(device); });
        if (data == null)
        {
            ArmUnload();
            return;
        }
        if (failure != null)
        {
            Fail("Micro : " + failure.Message);
            ArmUnload();
            return;
        }
        if (data.Length < rate * 0.3)
        {
            lock (gate)
            {
                state = "idle";
            }
            ArmUnload();
            return;
        }
        if (Interlocked.Exchange(ref busy, 1) != 0)
        {
            ArmUnload();
            return;
        }
        lock (gate)
        {
            state = "transcribing";
        }
        _ = Task.Run(() => Transcribe(data, rate));
    }

    // Réarme le déchargement du modèle (10 min d'inactivité).
    void ArmUnload()
    {
        if (disposed)
            return;
        try
        {
            unloadTimer.Change(TimeSpan.FromMinutes(10), Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Minuterie déjà libérée : arrêt en cours.
        }
    }

    void DisposeCapture()
    {
        var device = capture;
        capture = null;
        samples = null;
        if (device != null)
            Task.Run(() => { Safe.Dispose(device); });
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(hook);
            hook = IntPtr.Zero;
        }
        recording = false;
        var device = capture;
        capture = null;
        if (device != null)
            Safe.Try(() =>
            {
                device.StopRecording();
                device.Dispose();

            });
        unloadTimer.Dispose();
        idleTimer.Dispose();
        suspendTimer.Dispose();
        Task<WhisperFactory>? task;
        lock (factoryGate)
        {
            task = factoryTask;
            factoryTask = null;
        }
        if (task is { IsCompletedSuccessfully: true })
            Safe.Dispose(task.Result);
    }
}
