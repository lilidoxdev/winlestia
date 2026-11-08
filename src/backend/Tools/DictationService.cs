// C# 12 / .NET 8 / Windows 11. Dictée « appuyer pour parler » : hook clavier, micro à la demande, Whisper.net, saisie dans l'appli
// active.

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

internal sealed record DictationSnapshot(string State, double Level, float[] Levels, string Lang, string Text, string Error);

internal sealed partial class DictationService : IDisposable
{
    const int Bands = 32, Fft = 1024, FftLog = 10, MaxSeconds = 60;
    const string ModelFile = "ggml-large-v3-turbo-q5_0.bin";
    const string ModelUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/" + ModelFile;
    static readonly string[] Allowed = { "fr", "en", "es" };

    // --- hook -----------------------------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    struct KeyData
    {
        public uint Key, Scan, Flags, Time; public UIntPtr Extra;
    }
    delegate IntPtr Callback(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookExW(int id, Callback callback, IntPtr module,
        uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    readonly Dispatcher dispatcher; readonly Action<string> send; readonly Callback callback;
    IntPtr hook; volatile int key = 0xA5; volatile bool held, disposed, swallowedCtrl; volatile bool capturing; volatile bool enabledPref;
    volatile int pendingUp; volatile string lastLang = "fr";

    // --- état partagé (lu par la commande « dictation ») -----------------------------------------
    readonly object gate = new();
    string state = "idle", lang = "", text = "", error = ""; double level; readonly float[] levels = new float[Bands];

    // --- capture --------------------------------------------------------------------------------
    WasapiCapture? capture; List<float>? samples; int sourceRate = 48000; readonly float[] window = new float[Fft]; int windowFill;
    int recordingId; bool recording;
    readonly Complex[] fftBuffer = new Complex[Fft];

    // --- Whisper --------------------------------------------------------------------------------
    readonly object factoryGate = new(); Task<WhisperFactory>? factoryTask; bool downloading; int busy;
    readonly Timer unloadTimer, idleTimer, suspendTimer;
    // Appelé sur le thread UI quand une touche a été capturée (enregistre la préférence) ; non appelé en cas d'annulation.
    internal Action<int>? KeyCaptured;

    static string ModelDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Winlestia",
        "models");

    internal DictationService(Dispatcher dispatcher, Action<string> send)
    {
        this.dispatcher = dispatcher;
        this.send = send;
        callback = Handle;
        unloadTimer = new Timer(_ => Unload(), null, Timeout.Infinite, Timeout.Infinite);
        idleTimer = new Timer(_ => { lock (gate) { if (state is "done" or "error") state = "idle"; } }, null, Timeout.Infinite,
            Timeout.Infinite);
        suspendTimer = new Timer(_ =>
        {
            if (capturing && !disposed)
                dispatcher.BeginInvoke(DispatcherPriority.Normal, () =>
                {
                    if (capturing)
                    {
                        EndCapture();
                        send("dictation:keycancel");
                    }
                });
        }, null, Timeout.Infinite, Timeout.Infinite);
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
        catch (ObjectDisposedException)
        {
            // Minuterie déjà libérée : arrêt en cours.
        }
    }

    internal void EndCapture()
    {
        try
        {
            suspendTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Minuterie déjà libérée : arrêt en cours.
        }
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
            catch (Exception ex) { Store.Log("Dictée: enregistrement de la touche: " + ex.Message); }
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
        bool isDown = kind is 0x100 or 0x104, isUp = kind is 0x101 or 0x105;
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
                return new IntPtr(1);   // key-up d'une autre touche pendant la capture : avalé
            if (info.Key == 0xA2 && info.Scan == 0x21D)
            {
                swallowedCtrl = true;
                return new IntPtr(1);
            }   // AltGr : on attend l'Alt droit qui suit
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
            // chargement paresseux pendant l'enregistrement
            _ = GetFactory().ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully)
                    lock (gate)
                    {
                        if (state == "loading"
                && recording)
                            state = "listening";
                    }
            });
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
}
