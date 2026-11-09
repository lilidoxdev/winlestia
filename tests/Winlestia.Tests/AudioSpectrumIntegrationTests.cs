// C#12 / WindowsSpectrum / DESKTOP: plays a real 440 Hz sine on the default audio output (very low gain) and reads the process-loopback
// spectrum. Never run in CI.
using System;
using System.Reflection;
using System.Threading;
using System.Linq;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using PanneauxGlass;
namespace Winlestia.Tests;
[Trait("Category", "Desktop")]
public class AudioSpectrumIntegrationTests
{
    [Fact]
    public void SineIsSeenInOwnProcessAndIgnoredForOthers()
    {
        var type = typeof(Entry).Assembly.GetType("PanneauxGlass.WindowsSpectrum")!;
        var spectrum = Activator.CreateInstance(type, true)!;
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        double[] Read() => (double[])type.GetProperty("Values", flags)!.GetValue(spectrum)!;
        void Target(int pid) => type.GetMethod("SetTarget", flags)!.Invoke(spectrum, new object[] { pid });
        try
        {
            using var output = new WaveOutEvent();
            output.Init(new SignalGenerator(44100, 2) { Frequency = 440, Gain = .01, Type = SignalGeneratorType.Sin });
            Target(Environment.ProcessId);
            output.Play();
            Thread.Sleep(1800);
            var own = Read();
            bool animated = own.Max() > .02 && own.Max() - own.Min() > .02;
            Assert.True(animated, $"FFT frequency variation expected for own process, peak={own.Max():F3}");
            Target(999999);
            Thread.Sleep(1300);
            Assert.True(Read().Max() < .01, "unrelated process must give silence");
        }
        finally { ((IDisposable)spectrum).Dispose(); }
    }
}
