// C# 12 / AudioBarsHost.cs / WASAPI loopback of the default output device, only while the dashboard Media panel is visible.
// The sound is never stored or sent: samples go through a 2048-float ring, the worker turns them into 28 levels, the ring dies with the thread.
// Silence: loopback delivers no packet, the bars decay to zero, one idle frame is published, then the worker blocks (no timer, no wake-up).
using System;
using System.Diagnostics;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace PanneauxGlass;

internal sealed class AudioBarsHost : IDisposable
{
    internal const int FrameMs = 33, StopWaitMs = 500;

    internal readonly record struct Frame(bool Running, long Frames, int Seq, bool Idle, int[] Bars);

    sealed class Run
    {
        internal readonly ManualResetEvent Stop = new(false);
        internal Thread? Thread;
        internal volatile bool Exited;
    }

    readonly object gate = new();
    Run? run;
    long frames;
    int seq;
    bool idle = true;
    int[] published = new int[AudioBars.Bars];

    internal bool Running
    {
        get
        {
            lock (gate)
                return run != null && !run.Stop.WaitOne(0);
        }
    }

    internal Frame Snapshot(bool withBars)
    {
        lock (gate)
            return new(run != null && !run.Stop.WaitOne(0), frames, seq, idle, withBars ? (int[])published.Clone() : Array.Empty<int>());
    }

    // Called from the UI thread, never blocks: a stop only raises the flag. A start while the previous worker is still leaving
    // hands it to the new worker, which waits for it (at most StopWaitMs) before opening its own capture, so two captures never overlap.
    internal void SetActive(bool on)
    {
        lock (gate)
        {
            if (on)
            {
                if (run != null && !run.Stop.WaitOne(0))
                    return;
                var prev = run;
                frames = 0;
                seq = 0;
                idle = true;
                published = new int[AudioBars.Bars];
                var next = new Run();
                next.Thread = new Thread(() => Work(next, prev)) { IsBackground = true, Name = "Winlestia audio bars" };
                run = next;
                try
                {
                    next.Thread.Start();
                }
                catch (Exception ex) { run = null; Store.Log("Audio bars: " + ex.Message); }
            }
            else if (run != null)
            {
                run.Stop.Set();
                published = new int[AudioBars.Bars];
                idle = true;
                seq++;
            }
        }
    }

    void Work(Run self, Run? prev)
    {
        var sw = Stopwatch.StartNew();
        var ring = new float[AudioBars.FftSize * 2];
        var frame = new float[AudioBars.FftSize];
        var bars = new AudioBars();
        var fresh = new AutoResetEvent(false);
        var changed = new AutoResetEvent(false);
        object ringGate = new();
        int write = 0, filled = 0, rate = 0;
        bool hasData = false;
        MMDeviceEnumerator? enumerator = null;
        MMDevice? device = null;
        Notifier? notifier = null;
        WasapiLoopbackCapture? capture = null;

        void Close()
        {
            var c = capture;
            capture = null;
            var d = device;
            device = null;
            if (c != null)
            {
                Safe.Try(() => c.StopRecording());
                Safe.Dispose(c);
            }
            Safe.Dispose(d);
            if (c == null)
                return;
            lock (ringGate)
            {
                write = 0;
                filled = 0;
                hasData = false;
                Array.Clear(ring);
            }
        }
        void Open()
        {
            try
            {
                device = enumerator!.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
                var c = new WasapiLoopbackCapture(device);
                var wf = c.WaveFormat;
                bool isFloat = wf.Encoding == WaveFormatEncoding.IeeeFloat || (wf.Encoding == WaveFormatEncoding.Extensible && wf.BitsPerSample == 32);
                if (wf.Channels < 1 || wf.Channels > 16 || !(isFloat || wf.BitsPerSample == 16))
                {
                    c.Dispose();
                    device.Dispose();
                    device = null;
                    return;
                }
                int channels = wf.Channels;
                c.DataAvailable += (_, e) =>
                {
                    try
                    {
                        int step = channels * (isFloat ? 4 : 2), count = e.BytesRecorded / step;
                        if (count <= 0)
                            return;
                        lock (ringGate)
                        {
                            if (self.Stop.WaitOne(0))
                                return;
                            rate = wf.SampleRate;
                            for (int i = 0; i < count; i++)
                            {
                                double sum = 0;
                                for (int ch = 0; ch < channels; ch++)
                                {
                                    int at = i * step + ch * (isFloat ? 4 : 2);
                                    sum += isFloat ? BitConverter.ToSingle(e.Buffer, at) : BitConverter.ToInt16(e.Buffer, at) / 32768.0;
                                }
                                ring[write] = (float)(sum / channels);
                                write = (write + 1) % ring.Length;
                            }
                            filled = Math.Min(ring.Length, filled + count);
                            hasData = true;
                        }
                        fresh.Set();
                    }
                    catch (Exception)
                    {
                        // Callback audio à haute fréquence : une trame illisible est simplement sautée.
                    }
                };
                c.StartRecording();
                capture = c;
            }
            catch (Exception ex) { capture = null; Safe.Dispose(device); device = null; Store.Log("Audio bars: " + ex.Message); }
        }

        try
        {
            if (prev != null && !prev.Exited && prev.Thread != null && !prev.Thread.Join(StopWaitMs))
            {
                Store.Log("Audio bars: previous capture still stopping");
                lock (gate)
                {
                    if (run == self)
                        run = null;
                }
                return;
            }
            prev = null;
            enumerator = new MMDeviceEnumerator();
            notifier = new Notifier(changed);
            enumerator.RegisterEndpointNotificationCallback(notifier);
            Open();
            var handles = new WaitHandle[] { self.Stop, changed, fresh };
            long next = 0;
            while (true)
            {
                bool quiet = bars.Silent;
                bool pending;
                lock (ringGate)
                    pending = hasData;
                int timeout = quiet && !pending ? Timeout.Infinite : (int)Math.Max(0, next - sw.ElapsedMilliseconds);
                int which = WaitHandle.WaitAny(handles, timeout);
                if (which == 0)
                    break;
                if (which == 1)
                {
                    Close();
                    Open();
                    continue;
                }
                long now = sw.ElapsedMilliseconds;
                if (now < next && self.Stop.WaitOne((int)(next - now)))
                    break;
                bool got = false;
                int count = 0, frameRate = 0;
                lock (ringGate)
                {
                    if (hasData && filled > 0)
                    {
                        int n = Math.Min(filled, frame.Length), start = (write - n + ring.Length) % ring.Length;
                        for (int i = 0; i < n; i++)
                            frame[i] = ring[(start + i) % ring.Length];
                        got = true;
                        hasData = false;
                        count = n;
                        frameRate = rate;
                    }
                }
                if (got)
                    Publish(self, bars, frame.AsSpan(0, count), frameRate);
                else
                {
                    if (bars.Silent)
                        continue;
                    bars.Decay();
                    Publish(self, bars, default, 0, true);
                }
                next = sw.ElapsedMilliseconds + FrameMs;
            }
        }
        catch (Exception ex)
        {
            Store.Log("Audio bars: " + ex.Message);
        }
        finally
        {
            Safe.Try(() => Close());
            Safe.Try(() =>
            {
                if (notifier != null)
                    enumerator?.UnregisterEndpointNotificationCallback(notifier);

            });
            Safe.Dispose(enumerator);
            fresh.Dispose();
            changed.Dispose();
            self.Exited = true;
        }
    }

    void Publish(Run self, AudioBars bars, ReadOnlySpan<float> samples, int rate, bool decayed = false)
    {
        if (!decayed)
            bars.Update(samples, rate);
        var levels = bars.Levels;
        lock (gate)
        {
            if (run != self || self.Stop.WaitOne(0))
                return;
            for (int i = 0; i < published.Length && i < levels.Length; i++)
                published[i] = (int)Math.Round(Math.Clamp(levels[i], 0, 1) * 100);
            idle = bars.Silent;
            seq++;
            frames++;
        }
    }

    sealed class Notifier : IMMNotificationClient
    {
        readonly AutoResetEvent changed;
        internal Notifier(AutoResetEvent changed)
        {
            this.changed = changed;
        }
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Console)
            {
                try
                {
                    changed.Set();
                }
                catch (ObjectDisposedException)
                {
                    // Signal déjà libéré à l'arrêt : plus personne à réveiller.
                }
            }
        }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
        }
        public void OnDeviceAdded(string pwstrDeviceId)
        {
        }
        public void OnDeviceRemoved(string deviceId)
        {
        }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
        {
        }
    }

    public void Dispose() => SetActive(false);
}
