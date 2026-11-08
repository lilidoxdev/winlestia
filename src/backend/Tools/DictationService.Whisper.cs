// DictationService : transcription Whisper et gestion du modèle.

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

internal sealed partial class DictationService
{
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
        catch (Exception ex) { Store.Log("Dictée: détection de langue: " + ex.Message); return null; }
    }

    static async Task<(string text, string lang)> Run(WhisperFactory factory, float[] audio, string language)
    {
        var builder = factory.CreateBuilder().WithLanguage(language).WithThreads(Math.Max(4, Environment.ProcessorCount / 2));
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
        readonly float[] data; int position;
        public ArraySampleProvider(float[] data, int rate)
        {
            this.data = data;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, 1);
        }
        public WaveFormat WaveFormat
        {
            get;
        }
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
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex Credits = new(
        @"(sous-titres?\s+(réalisés?|par|de)|subtitles?\s+by|subtítulos\s+(por|realizados)|traduit\s+par|amara\.org)[^\n]*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
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
                    Safe.Try(() => File.Delete(path));
                    throw new InvalidOperationException("Modèle de dictée illisible, il sera retéléchargé au prochain essai (" + ex.Message
                        + ").");
                }
                // Préchauffage (compilation des shaders Vulkan à froid, ~20 s) sur ~1 s de silence, pendant que l'utilisateur parle.
                // La vraie transcription attend ce même Task. Erreurs ignorées.
                try
                {
                    await Run(factory, new float[16000], "auto").ConfigureAwait(false);
                }
                catch (Exception ex) { Store.Log("Dictée: préchauffage: " + ex.Message); }
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
            using var response = await client.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                await input.CopyToAsync(output).ConfigureAwait(false);
            long received = new FileInfo(part).Length;
            long? expected = response.Content.Headers.ContentLength;
            if (received <= 400_000_000L || (expected.HasValue && received != expected.Value))
                throw new IOException("Téléchargement du modèle incomplet (" + received / 1048576 + " Mo reçus). Réessaie.");
            File.Move(part, path, true);
        }
        catch
        {
            Safe.Try(() => File.Delete(part));
            throw;
        }
        finally { downloading = false; }
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
            Safe.Dispose(task.Result);
    }
}
