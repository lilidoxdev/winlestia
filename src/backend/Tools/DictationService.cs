// C# 12 / .NET 8 / Windows 11. Dictée « appuyer pour parler » : hook clavier, micro à la demande, Whisper.net, saisie dans l'appli active.

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

namespace PanneauxGlass.Tools;

internal sealed record DictationSnapshot(
    string State,
    double Level,
    float[] Levels,
    string Lang,
    string Text,
    string Error
);

internal sealed class DictationService : IDisposable
{
    const int Bands = 32,
        Fft = 1024,
        FftLog = 10,
        MaxSeconds = 60;
    const string ModelFile = "ggml-large-v3-turbo-q5_0.bin";
    const string ModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/" + ModelFile;
    static readonly string[] Allowed = { "fr", "en", "es" };

    // --- hook -----------------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    struct KeyData
    {
        public uint Key,
            Scan,
            Flags,
            Time;
        public UIntPtr Extra;
    }

    delegate IntPtr Callback(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookExW(int id, Callback callback, IntPtr module, uint thread);

    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    readonly Dispatcher dispatcher;
    readonly Action<string> send;
    readonly Callback callback;
    IntPtr hook;
    volatile int key = 0xA5;
    volatile bool held,
        disposed,
        swallowedCtrl;
    volatile bool capturing;
    volatile bool enabledPref;
    volatile int pendingUp;
    volatile string lastLang = "fr";

    // --- état partagé (lu par la commande « dictation ») -----------------------------------------
    readonly object gate = new();
    string state = "idle",
        lang = "",
        text = "",
        error = "";
    double level;
    readonly float[] levels = new float[Bands];

    // --- capture --------------------------------------------------------------------------------
    WasapiCapture? capture;
    List<float>? samples;
    int sourceRate = 48000;
    readonly float[] window = new float[Fft];
    int windowFill;
    int recordingId;
    bool recording;
    readonly Complex[] fftBuffer = new Complex[Fft];

    // --- Whisper --------------------------------------------------------------------------------
    readonly object factoryGate = new();
    Task<WhisperFactory>? factoryTask;
    bool downloading;
    int busy;
    readonly Timer unloadTimer,
        idleTimer,
        suspendTimer;

    // Appelé sur le thread UI quand une touche a été capturée (enregistre la préférence) ; non appelé en cas d'annulation.
    internal Action<int>? KeyCaptured;

    static string ModelDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Winlestia", "models");

    internal DictationService(Dispatcher dispatcher, Action<string> send)
    {
        this.dispatcher = dispatcher;
        this.send = send;
        callback = Handle;
        unloadTimer = new Timer(_ => Unload(), null, Timeout.Infinite, Timeout.Infinite);
        idleTimer = new Timer(
            _ =>
            {
                lock (gate)
                {
                    if (state is "done" or "error")
                        state = "idle";
                }
            },
            null,
            Timeout.Infinite,
            Timeout.Infinite
        );
        suspendTimer = new Timer(
            _ =>
            {
                if (capturing && !disposed)
                    dispatcher.BeginInvoke(
                        DispatcherPriority.Normal,
                        () =>
                        {
                            if (capturing)
                            {
                                EndCapture();
                                send("dictation:keycancel");
                            }
                        }
                    );
            },
            null,
            Timeout.Infinite,
            Timeout.Infinite
        );
    }

    internal DictationSnapshot Snapshot()
    {
        lock (gate)
        {
            return new DictationSnapshot(state, level, (float[])levels.Clone(), lang, text, error);
        }
    }

    // Appelé sur le thread UI à chaque changement de préférence : pose ou retire le hook.
    internal void Apply(bool enabled, int virtualKey)
    {
        if (disposed)
            return;
        key = virtualKey is > 0 and < 255 ? virtualKey : 0xA5;
        enabledPref = enabled;
        // Pas de préchargement du modèle ici, volontairement : il n'est chargé qu'au premier appui (rien au repos).
        if (enabled && hook == IntPtr.Zero)
        {
            hook = SetWindowsHookExW(13, callback, Native.GetModuleHandleW(null), 0);
            if (hook == IntPtr.Zero)
                Store.Log("Dictée: hook clavier indisponible.");
        }
        else if (!enabled && hook != IntPtr.Zero && !capturing)
        {
            Unhook();
        }
    }

    void Unhook()
    {
        UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
        pendingUp = 0;
        swallowedCtrl = false;
        if (held)
        {
            held = false;
            Stop();
        }
    }

    // --- capture de touche (thread UI) : le hook sert à choisir la touche de dictée -------------------
    internal void BeginCapture()
    {
        if (disposed)
            return;
        if (hook == IntPtr.Zero)
        {
            hook = SetWindowsHookExW(13, callback, Native.GetModuleHandleW(null), 0);
            if (hook == IntPtr.Zero)
            {
                Store.Log("Dictée: hook clavier indisponible (capture).");
                send("dictation:keycancel");
                return;
            }
        }
        if (held)
        {
            held = false;
            Stop();
        }
        pendingUp = 0;
        swallowedCtrl = false;
        capturing = true;
        try
        {
            suspendTimer.Change(TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException) { }
    }

    internal void EndCapture()
    {
        try
        {
            suspendTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException) { }
        capturing = false;
        if (!enabledPref && hook != IntPtr.Zero)
            Unhook();
    }

    // Hors callback (dispatcher) : fin de capture, enregistrement de la préférence puis notification du front.
    void FinishCapture(int vk)
    {
        EndCapture();
        if (vk > 0)
        {
            try
            {
                KeyCaptured?.Invoke(vk);
            }
            catch (Exception ex)
            {
                Store.Log("Dictée: enregistrement de la touche: " + ex.Message);
            }
            send("dictation:key:" + vk);
        }
        else
            send("dictation:keycancel");
    }

    // Callback du hook : aucun I/O, uniquement des drapeaux et un BeginInvoke.
    IntPtr Handle(int code, IntPtr message, IntPtr data)
    {
        if (code < 0)
            return CallNextHookEx(hook, code, message, data);
        var info = Marshal.PtrToStructure<KeyData>(data);
        long kind = message.ToInt64();
        bool isDown = kind is 0x100 or 0x104,
            isUp = kind is 0x101 or 0x105;
        // Faux Ctrl gauche d'AltGr avalé pendant la capture : son key-up est avalé aussi.
        if (isUp && swallowedCtrl && info.Key == 0xA2 && info.Scan == 0x21D)
        {
            swallowedCtrl = false;
            return new IntPtr(1);
        }
        // Touche capturée (ou Échap) : on avale ses répétitions et son key-up, une fois sortis du mode capture.
        if (pendingUp != 0 && info.Key == (uint)pendingUp && (isDown || isUp))
        {
            if (isUp)
                pendingUp = 0;
            return new IntPtr(1);
        }
        if (capturing)
        {
            if (!isDown && !isUp)
                return CallNextHookEx(hook, code, message, data);
            if (!isDown)
                return new IntPtr(1); // key-up d'une autre touche pendant la capture : avalé
            if (info.Key == 0xA2 && info.Scan == 0x21D)
            {
                swallowedCtrl = true;
                return new IntPtr(1);
            } // AltGr : on attend l'Alt droit qui suit
            capturing = false;
            pendingUp = (int)info.Key;
            int vk = info.Key == 0x1B ? 0 : (int)info.Key;
            if (vk is < 0 or > 254)
                vk = 0;
            dispatcher.BeginInvoke(DispatcherPriority.Send, () => FinishCapture(vk));
            return new IntPtr(1);
        }
        // AltGr (Alt droit) : le clavier AZERTY envoie d'abord un faux Ctrl gauche (scan 0x21D). On l'avale au down
        // et, seulement dans ce cas, à l'up correspondant, pour ne jamais laisser un Ctrl coincé.
        if (key == 0xA5 && info.Key == 0xA2 && info.Scan == 0x21D)
        {
            if (kind is 0x100 or 0x104)
            {
                swallowedCtrl = true;
                return new IntPtr(1);
            }
            if (kind is 0x101 or 0x105)
            {
                if (swallowedCtrl)
                {
                    swallowedCtrl = false;
                    return new IntPtr(1);
                }
            }
            return CallNextHookEx(hook, code, message, data);
        }
        if ((info.Flags & 0x10) != 0 || info.Key != (uint)key)
            return CallNextHookEx(hook, code, message, data);
        if (kind is 0x100 or 0x104)
        {
            if (!held)
            {
                held = true;
                dispatcher.BeginInvoke(DispatcherPriority.Send, Start);
            }
            return new IntPtr(1);
        }
        if (kind is 0x101 or 0x105)
        {
            if (held)
            {
                held = false;
                dispatcher.BeginInvoke(DispatcherPriority.Send, Stop);
                return new IntPtr(1);
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }

    // --- cycle d'enregistrement (thread UI) --------------------------------------------------------
    void Start()
    {
        if (disposed || recording || Volatile.Read(ref busy) != 0)
            return;
        try
        {
            idleTimer.Change(Timeout.Infinite, Timeout.Infinite);
            unloadTimer.Change(Timeout.Infinite, Timeout.Infinite);
            bool needDownload = !File.Exists(Path.Combine(ModelDirectory, ModelFile));
            lock (gate)
            {
                state = needDownload ? "loading" : "listening";
                lang = "";
                text = "";
                error = "";
                level = 0;
                Array.Clear(levels);
            }
            _ = GetFactory()
                .ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                        lock (gate)
                        {
                            if (state == "loading" && recording)
                                state = "listening";
                        }
                }); // chargement paresseux pendant l'enregistrement
            var device = new WasapiCapture();
            int id = ++recordingId;
            sourceRate = device.WaveFormat.SampleRate;
            samples = new List<float>(sourceRate * 5);
            windowFill = 0;
            device.DataAvailable += (_, e) => OnData(device, id, e);
            device.RecordingStopped += (_, e) => OnStopped(device, id, e.Exception);
            capture = device;
            recording = true;
            device.StartRecording();
            send("dictation:start");
        }
        catch (Exception ex)
        {
            recording = false;
            DisposeCapture();
            Fail("Micro indisponible : " + ex.Message);
            send("dictation:start");
            send("dictation:stop");
            ArmUnload();
        }
    }

    void Stop()
    {
        if (!recording)
            return;
        recording = false;
        try
        {
            capture?.StopRecording();
        }
        catch (Exception ex)
        {
            Store.Log("Dictée: arrêt du micro: " + ex.Message);
        }
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
        bool isFloat =
            format.Encoding == WaveFormatEncoding.IeeeFloat
            || (
                format is WaveFormatExtensible ext
                && ext.ToStandardWaveFormat().Encoding == WaveFormatEncoding.IeeeFloat
            );
        var mono = new float[frames];
        double energy = 0;
        for (int i = 0; i < frames; i++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++)
            {
                int offset = (i * channels + c) * bytesPerSample;
                sum +=
                    isFloat ? BitConverter.ToSingle(e.Buffer, offset)
                    : bytesPerSample == 2 ? BitConverter.ToInt16(e.Buffer, offset) / 32768f
                    : bytesPerSample == 3
                        ? ((e.Buffer[offset] << 8 | e.Buffer[offset + 1] << 16 | e.Buffer[offset + 2] << 24) >> 8)
                            / 8388608f
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
            double low = 80,
                high = Math.Min(8000, sourceRate / 2.0 - 1);
            for (int b = 0; b < Bands; b++)
            {
                double f0 = low * Math.Pow(high / low, (double)b / Bands),
                    f1 = low * Math.Pow(high / low, (double)(b + 1) / Bands);
                int i0 = Math.Max(1, (int)Math.Floor(f0 / binHz)),
                    i1 = Math.Max(i0, (int)Math.Ceiling(f1 / binHz));
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

    static double Smooth(double current, double target) =>
        target > current ? current + (target - current) * .6 : current + (target - current) * .12;

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
        Task.Run(() =>
        {
            try
            {
                device.Dispose();
            }
            catch { }
        });
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
        catch (ObjectDisposedException) { }
    }

    void DisposeCapture()
    {
        var device = capture;
        capture = null;
        samples = null;
        if (device != null)
            Task.Run(() =>
            {
                try
                {
                    device.Dispose();
                }
                catch { }
            });
    }

    // --- transcription (thread de fond) -----------------------------------------------------------
    async Task Transcribe(float[] source, int rate)
    {
        try
        {
            float[] audio = Resample(source, rate);
            var factory = await GetFactory().ConfigureAwait(false);
            lock (gate)
            {
                if (state == "loading")
                    state = "transcribing";
            }
            var (raw, detected) = await Run(factory, audio, "auto").ConfigureAwait(false);
            // Langue : vide = non détectée (on garde le texte, langue = dernière langue valide).
            // Une seule inférence en langue auto. Seulement si la langue détectée est hors fr/en/es : détection
            // restreinte à fr/en/es (DetectLanguageWithProbability), sinon dernière langue valide ; puis relance.
            string language = detected.ToLowerInvariant();
            if (language.Length == 0)
                language = lastLang;
            else if (!Allowed.Contains(language))
            {
                language = DetectAllowed(factory, audio) ?? lastLang;
                (raw, _) = await Run(factory, audio, language).ConfigureAwait(false);
            }
            lastLang = language;
            string clean = Clean(raw);
            if (clean.Length == 0)
            {
                lock (gate)
                {
                    state = "idle";
                    lang = language;
                }
                return;
            }
            lock (gate)
            {
                state = "typing";
                lang = language;
                text = clean;
            }
            // Ne jamais injecter tant que la touche de dictée est enfoncée (Alt fausserait la saisie).
            for (int i = 0; i < 500 && held; i++)
                await Task.Delay(20).ConfigureAwait(false);
            if (held)
                throw new InvalidOperationException("Touche maintenue");
            if (!TextInjector.Type(clean))
                throw new InvalidOperationException("Saisie impossible dans cette application.");
            lock (gate)
            {
                state = "done";
            }
            idleTimer.Change(1500, Timeout.Infinite);
        }
        catch (Exception ex)
        {
            Store.Log("Dictée: " + ex);
            Fail(ex.Message);
        }
        finally
        {
            Volatile.Write(ref busy, 0);
            ArmUnload();
        }
    }

    static string? DetectAllowed(WhisperFactory factory, float[] audio)
    {
        try
        {
            using var processor = factory.CreateBuilder().WithLanguage("auto").Build();
            string found = processor.DetectLanguageWithProbability(audio, Allowed).language.ToLowerInvariant();
            return Allowed.Contains(found) ? found : null;
        }
        catch (Exception ex)
        {
            Store.Log("Dictée: détection de langue: " + ex.Message);
            return null;
        }
    }

    static async Task<(string text, string lang)> Run(WhisperFactory factory, float[] audio, string language)
    {
        var builder = factory
            .CreateBuilder()
            .WithLanguage(language)
            .WithThreads(Math.Max(4, Environment.ProcessorCount / 2));
        await using var processor = builder.Build();
        var parts = new StringBuilder();
        string detected = "";
        await foreach (var segment in processor.ProcessAsync(audio))
        {
            parts.Append(segment.Text);
            if (detected.Length == 0 && !string.IsNullOrEmpty(segment.Language))
                detected = segment.Language;
        }
        return (parts.ToString(), detected);
    }

    static float[] Resample(float[] source, int rate)
    {
        if (rate == 16000)
            return source;
        var provider = new WdlResamplingSampleProvider(new ArraySampleProvider(source, rate), 16000);
        var output = new List<float>((int)(source.Length * 16000L / rate) + 1024);
        var block = new float[8192];
        int read;
        while ((read = provider.Read(block, 0, block.Length)) > 0)
            output.AddRange(new ArraySegment<float>(block, 0, read));
        return output.ToArray();
    }

    sealed class ArraySampleProvider : ISampleProvider
    {
        readonly float[] data;
        int position;

        public ArraySampleProvider(float[] data, int rate)
        {
            this.data = data;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, 1);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, data.Length - position);
            if (n <= 0)
                return 0;
            Array.Copy(data, position, buffer, offset, n);
            position += n;
            return n;
        }
    }

    static readonly Regex Brackets = new(@"\[[^\]]*\]|\*[^*]*\*|♪[^♪]*♪?", RegexOptions.Compiled);
    static readonly Regex Noises = new(
        @"\((?:inaudible|musique|music|música|applaudissements|rires|silence|bruit[^)]*|noise|laughter|applause)\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );
    static readonly Regex Credits = new(
        @"(sous-titres?\s+(réalisés?|par|de)|subtitles?\s+by|subtítulos\s+(por|realizados)|traduit\s+par|amara\.org)[^\n]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    static string Clean(string raw)
    {
        string value = Brackets.Replace(raw, "");
        value = Noises.Replace(value, "");
        value = Credits.Replace(value, "");
        value = Regex.Replace(value, @"[\r\n]+", " ");
        value = Regex.Replace(value, @"[ \t]{2,}", " ");
        return value.Trim();
    }

    void Fail(string message)
    {
        lock (gate)
        {
            state = "error";
            error = message;
            level = 0;
            Array.Clear(levels);
        }
        idleTimer.Change(1500, Timeout.Infinite);
    }

    // --- modèle ---------------------------------------------------------------------------------
    Task<WhisperFactory> GetFactory()
    {
        lock (factoryGate)
        {
            if (factoryTask is { IsFaulted: false })
                return factoryTask;
            return factoryTask = Task.Run(async () =>
            {
                string path = Path.Combine(ModelDirectory, ModelFile);
                if (!File.Exists(path))
                    await Download(path).ConfigureAwait(false);
                WhisperFactory factory;
                try
                {
                    // GPU via Vulkan (pilote NVIDIA/AMD/Intel suffit, pas de toolkit CUDA), repli CPU.
                    RuntimeOptions.RuntimeLibraryOrder = [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu];
                    factory = WhisperFactory.FromPath(path);
                }
                catch (Exception ex)
                {
                    // Fichier corrompu : on le supprime pour forcer un retéléchargement au prochain appui.
                    try
                    {
                        File.Delete(path);
                    }
                    catch { }
                    throw new InvalidOperationException(
                        "Modèle de dictée illisible, il sera retéléchargé au prochain essai (" + ex.Message + ")."
                    );
                }
                // Préchauffage (compilation des shaders Vulkan à froid, ~20 s) sur ~1 s de silence, pendant que l'utilisateur parle.
                // La vraie transcription attend ce même Task. Erreurs ignorées.
                try
                {
                    await Run(factory, new float[16000], "auto").ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Store.Log("Dictée: préchauffage: " + ex.Message);
                }
                return factory;
            });
        }
    }

    async Task Download(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string part = path + ".part";
        lock (gate)
        {
            if (state is "listening" or "idle")
                state = "loading";
        }
        downloading = true;
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            using var response = await client
                .GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            await using (
                var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true)
            )
                await input.CopyToAsync(output).ConfigureAwait(false);
            long received = new FileInfo(part).Length;
            long? expected = response.Content.Headers.ContentLength;
            if (received <= 400_000_000L || (expected.HasValue && received != expected.Value))
                throw new IOException(
                    "Téléchargement du modèle incomplet (" + received / 1048576 + " Mo reçus). Réessaie."
                );
            File.Move(part, path, true);
        }
        catch
        {
            try
            {
                File.Delete(part);
            }
            catch { }
            throw;
        }
        finally
        {
            downloading = false;
        }
    }

    void Unload()
    {
        if (disposed || recording || Volatile.Read(ref busy) != 0 || downloading)
            return;
        Task<WhisperFactory>? task;
        lock (factoryGate)
        {
            task = factoryTask;
            factoryTask = null;
        }
        if (task is { IsCompletedSuccessfully: true })
            try
            {
                task.Result.Dispose();
            }
            catch { }
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
            try
            {
                device.StopRecording();
                device.Dispose();
            }
            catch { }
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
            try
            {
                task.Result.Dispose();
            }
            catch { }
    }
}

// Saisie dans l'application active : presse-papiers + Ctrl+V (texte exact, restauré), SendInput Unicode en repli.
// Cause mesurée du 1er caractère perdu / texte altéré avec SendInput dans le Bloc-notes Win11 : lot unique trop gros
// pour le RichEdit + modificateur (Alt de la touche de dictée) encore pris en compte au début du lot.
internal static class TextInjector
{
    [StructLayout(LayoutKind.Sequential)]
    struct MouseInput
    {
        public int Dx,
            Dy;
        public uint Data,
            Flags,
            Time;
        public UIntPtr Extra;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KeyInput
    {
        public ushort Vk,
            Scan;
        public uint Flags,
            Time;
        public UIntPtr Extra;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct Input
    {
        [FieldOffset(0)]
        public uint Type;

        [FieldOffset(8)]
        public MouseInput Mouse;

        [FieldOffset(8)]
        public KeyInput Key;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint count, Input[] inputs, int size);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vk);

    const uint KeyUp = 2,
        Unicode = 4;
    const ushort VkReturn = 0x0D,
        VkControl = 0x11,
        VkV = 0x56;
    static readonly int[] Modifiers = { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 };

    static Input Key(ushort vk, ushort scan, uint flags) =>
        new()
        {
            Type = 1,
            Key = new KeyInput
            {
                Vk = vk,
                Scan = scan,
                Flags = flags,
            },
        };

    // Attend (max 2 s) qu'aucun Shift/Ctrl/Alt(AltGr)/Win ne soit enfoncé, puis laisse ~40 ms au système.
    static void WaitModifiersReleased()
    {
        for (int i = 0; i < 100; i++)
        {
            bool down = false;
            foreach (int vk in Modifiers)
                if ((GetAsyncKeyState(vk) & 0x8000) != 0)
                {
                    down = true;
                    break;
                }
            if (!down)
                break;
            Thread.Sleep(20);
        }
        Thread.Sleep(40);
    }

    internal static bool Type(string text)
    {
        if (text.Length == 0)
            return true;
        WaitModifiersReleased();
        if (Paste(text))
            return true;
        return TypeKeys(text);
    }

    // Repli : 1 caractère UTF-16 (down+up) par appel, léger rythme ; les surrogates partent en paire consécutive.
    static bool TypeKeys(string text)
    {
        if (Marshal.SizeOf<Input>() != 40)
            return false;
        bool any = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
                continue;
            Input[] batch;
            if (c == '\n')
                batch = new[] { Key(VkReturn, 0, 0), Key(VkReturn, 0, KeyUp) };
            else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                char d = text[++i];
                batch = new[]
                {
                    Key(0, c, Unicode),
                    Key(0, d, Unicode),
                    Key(0, c, Unicode | KeyUp),
                    Key(0, d, Unicode | KeyUp),
                };
            }
            else
                batch = new[] { Key(0, c, Unicode), Key(0, c, Unicode | KeyUp) };
            if (SendInput((uint)batch.Length, batch, Marshal.SizeOf<Input>()) != batch.Length)
                return any;
            any = true;
            Thread.Sleep(4);
        }
        return any;
    }

    static bool Paste(string text)
    {
        bool ok = false;
        var thread = new Thread(() =>
        {
            System.Windows.Forms.IDataObject? previous = null;
            System.Windows.Forms.DataObject? backup = null;
            try
            {
                try
                {
                    previous = System.Windows.Forms.Clipboard.GetDataObject();
                    if (previous != null)
                    {
                        backup = new System.Windows.Forms.DataObject();
                        foreach (string format in previous.GetFormats(false))
                        {
                            var data = previous.GetData(format, false);
                            if (data != null)
                                backup.SetData(format, false, data);
                        }
                    }
                }
                catch
                {
                    backup = null;
                }
                if (!SetText(text))
                    return;
                var paste = new[]
                {
                    Key(VkControl, 0, 0),
                    Key(VkV, 0, 0),
                    Key(VkV, 0, KeyUp),
                    Key(VkControl, 0, KeyUp),
                };
                ok = SendInput((uint)paste.Length, paste, Marshal.SizeOf<Input>()) == paste.Length;
                Thread.Sleep(1000);
                try
                {
                    if (backup != null)
                        System.Windows.Forms.Clipboard.SetDataObject(backup, true, 5, 20);
                    else
                        System.Windows.Forms.Clipboard.Clear();
                }
                catch { }
            }
            catch (Exception ex)
            {
                Store.Log("Dictée: presse-papiers: " + ex.Message);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join(5000);
        return ok;
    }

    static bool SetText(string text)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                System.Windows.Forms.Clipboard.SetDataObject(text, true, 5, 20);
                return true;
            }
            catch
            {
                Thread.Sleep(30);
            }
        }
        return false;
    }
}
