// C# 12, fichier SensorServer.cs, cible Windows 11 / .NET 8, tube nommé WinlestiaSensors servi par l'assistant élevé
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using LibreHardwareMonitor.Hardware;

namespace WinlestiaSensors;

// Aucun minuteur : le matériel n'est ouvert qu'à la première requête et relu uniquement à la demande.
internal sealed class SensorServer : IDisposable
{
    internal const string PipeName = "WinlestiaSensors";
    Computer? computer;
    readonly object gate = new();

    internal static NamedPipeServerStream Create(bool first)
    {
        // Rien n'est lié à l'utilisateur courant (le service tourne en SYSTEM) : SYSTEM et Administrateurs, session interactive en lecture/écriture.
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.InteractiveSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.InOut, 4, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : PipeOptions.None), 0, 0, security);
    }

    // Création dans la boucle : un nom de tube squatté ou une erreur passagère se réessaie (1 s, doublé jusqu'à 30 s), sans boucle active.
    internal async Task Serve(CancellationToken stop)
    {
        NamedPipeServerStream? server = null;
        bool first = true;
        int delay = 1000;
        while (!stop.IsCancellationRequested)
        {
            try
            {
                server ??= Create(first);
                first = false;
                await server.WaitForConnectionAsync(stop);
                var current = server;
                server = null; // Handle en devient propriétaire : un échec de Create plus bas ne doit pas la fermer
                delay = 1000;
                _ = Task.Run(() => Handle(current));
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Log.Write("Tube: " + Log.Describe(ex));
                var broken = server;
                server = null;
                if (broken != null)
                    Safe(() => broken.Dispose());
                try { await Task.Delay(delay, stop); }
                catch (OperationCanceledException) { break; }
                delay = Math.Min(delay * 2, 30000);
            }
        }
        if (server != null)
            Safe(() => server.Dispose());
    }

    // Un client cassé ou muet ne bloque ni ne tue le serveur : lecture (au plus 8 octets) et écriture/vidage bornés à 1 s chacun.
    void Handle(NamedPipeServerStream pipe)
    {
        try
        {
            using (pipe)
            {
                var buffer = new byte[8];
                int total = 0;
                using (var rc = new CancellationTokenSource(1000))
                {
                    while (total < buffer.Length && !(total > 0 && buffer[total - 1] == (byte)'\n'))
                    {
                        int n = pipe.ReadAsync(buffer.AsMemory(total), rc.Token).AsTask().GetAwaiter().GetResult();
                        if (n <= 0)
                            break;
                        total += n;
                    }
                }
                if (System.Text.Encoding.ASCII.GetString(buffer, 0, total).Trim() != "t")
                    return;
                var answer = System.Text.Encoding.ASCII.GetBytes(SensorPick.Format(Read()) + "\n");
                using var wc = new CancellationTokenSource(1000);
                // Un client qui ne lit jamais : à l'échéance, la fermeture du tube débloque l'écriture et le vidage.
                using var reg = wc.Token.Register(() => Safe(pipe.Dispose));
                pipe.WriteAsync(answer, wc.Token).AsTask().GetAwaiter().GetResult();
                pipe.WaitForPipeDrain();
            }
        }
        catch (Exception) { }
    }

    double? Read()
    {
        lock (gate)
        {
            try
            {
                if (computer == null)
                {
                    var c = new Computer { IsCpuEnabled = true };
                    c.Open();
                    computer = c;
                }
                var sensors = new List<(string, double?)>();
                foreach (var hw in computer.Hardware.Where(h => h.HardwareType == HardwareType.Cpu))
                {
                    hw.Update();
                    foreach (var s in hw.Sensors.Where(s => s.SensorType == SensorType.Temperature))
                        sensors.Add((s.Name, s.Value));
                }
                return SensorPick.Choose(sensors);
            }
            catch (Exception ex)
            {
                Log.Write("Lecture: " + ex.GetType().Name);
                return null;
            }
        }
    }

    static void Safe(Action a) { try { a(); } catch (Exception) { } }

    public void Dispose()
    {
        lock (gate)
        {
            Safe(() => computer?.Close());
            computer = null;
        }
    }
}
