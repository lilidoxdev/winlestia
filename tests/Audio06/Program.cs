// C#12 / Program.cs / Windows11. Low-level sine fixture verifies frequency bins and process isolation.
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using PanneauxGlass;
using PanneauxGlass.Storage;

class Test
{
    static int Main()
    {
        var type = typeof(Entry).Assembly.GetType("PanneauxGlass.Media.WindowsSpectrum");
        var spectrum = Activator.CreateInstance(type, true);
        var flags = BindingFlags.NonPublic | BindingFlags.Instance;
        double[] Read() => (double[])type.GetProperty("Values", flags).GetValue(spectrum);
        void Target(int pid) => type.GetMethod("SetTarget", flags).Invoke(spectrum, new object[] { pid });
        try
        {
            using var output = new WaveOutEvent();
            output.Init(
                new SignalGenerator(44100, 2)
                {
                    Frequency = 440,
                    Gain = .01,
                    Type = SignalGeneratorType.Sin,
                }
            );
            Target(Environment.ProcessId);
            output.Play();
            Thread.Sleep(1800);
            var own = Read();
            bool animated = own.Max() > .02 && own.Max() - own.Min() > .02;
            Target(999999);
            Thread.Sleep(1300);
            bool isolated = Read().Max() < .01;
            Console.WriteLine(
                $"FFT frequency variation={animated}; unrelated process silence={isolated}; peak={own.Max():F3}"
            );
            return animated && isolated ? 0 : 1;
        }
        finally
        {
            ((IDisposable)spectrum).Dispose();
        }
    }
}
