// language: C# 12, file: QtShellBridge.Frontend.cs, target: Windows 11 / .NET 8, processus du frontend Qt : Job Object, nom d'utilisateur
// et erreur fatale
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Devices.Radios;
namespace PanneauxGlass;

internal sealed partial class QtShellBridge
{
    static IntPtr frontendJob;
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode,
        SetLastError = true)]
    static extern IntPtr CreateJobObject(
        IntPtr attributes,
        string? name);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetInformationJobObject(
        IntPtr job,
        int infoClass,
        IntPtr info,
        uint length);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)] static extern bool AssignProcessToJobObject(
        IntPtr job, IntPtr process);
    static void AttachFrontendJob(Process process)
    {
        try
        {
            if (frontendJob == IntPtr.Zero)
            {
                var job = CreateJobObject(IntPtr.Zero, null);
                if (job == IntPtr.Zero)
                {
                    Store.Log("Job Object frontend: création impossible (" + System.Runtime.InteropServices.Marshal.GetLastWin32Error()
                        + ")");
                    return;
                }
                // JOBOBJECT_EXTENDED_LIMIT_INFORMATION (x64 = 144 octets) ; LimitFlags à l'offset 16 de BasicLimitInformation.
                int size = IntPtr.Size == 8 ? 144 : 112;
                var info = System.Runtime.InteropServices.Marshal.AllocHGlobal(size);
                try
                {
                    for (int i = 0; i < size; i++)
                        System.Runtime.InteropServices.Marshal.WriteByte(info, i, 0);
                    System.Runtime.InteropServices.Marshal.WriteInt32(info, 16, 0x2000);
                    if (!SetInformationJobObject(job, 9, info, (uint)size))
                    {
                        Store.Log("Job Object frontend: configuration impossible ("
                            + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")");
                        return;
                    }
                }
                finally { System.Runtime.InteropServices.Marshal.FreeHGlobal(info); }
                frontendJob = job;
            }
            if (!AssignProcessToJobObject(frontendJob, process.Handle))
                Store.Log("Job Object frontend: assignation impossible (" + System.Runtime.InteropServices.Marshal.GetLastWin32Error()
                    + ")");
        }
        catch (Exception error) { Store.Log("Job Object frontend: " + error.Message); }
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(uint pid);
    [System.Runtime.InteropServices.DllImport("secur32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern bool GetUserNameEx(
        int format,
        System.Text.StringBuilder name,
        ref uint size);
    static readonly Lazy<string> displayUserName = new(() =>
    {
        try
        {
            string file = Path.Combine(Store.DataDir, "profil.json");
            if (File.Exists(file))
            {
                byte[] raw = File.ReadAllBytes(file);
                if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF)
                    raw = raw[3..];
                string text;
                try
                {
                    text = new UTF8Encoding(false, true).GetString(raw);
                }
                catch { text = CodePagesEncodingProvider.Instance.GetEncoding(1252)!.GetString(raw); }
                using var doc = System.Text.Json.JsonDocument.Parse(text);
                if (doc.RootElement.TryGetProperty("displayName", out var dn)
                    && dn.ValueKind == System.Text.Json.JsonValueKind.String
                    && dn.GetString() is { } s
                    && !string.IsNullOrWhiteSpace(s))
                    return s.Trim();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Store.Log("QtShellBridge.(initialiseur) : " + ex.GetType().Name + ": " + ex.Message);
        }
        try
        {
            var name = new StringBuilder(256);
            uint size = 256;
            if (GetUserNameEx(3, name, ref size) && name.Length > 0)
                return name.ToString();
        }
        catch (Exception ex)
        {
            Store.Log("QtShellBridge.(initialiseur) : " + ex.GetType().Name + ": " + ex.Message);
        }
        return Environment.UserName;
    });

    static string FrontendLog => Path.Combine(Store.DataDir, "Caelestia", "frontend.log");

    // Un moteur mort laissait un bureau sans barre et sans explication : on le dit, puis on rend la main.
    static void Fail() => System.Windows.Application.Current?.Dispatcher.Invoke(() =>
    {
        System.Windows.MessageBox.Show(
            "Winlestia n'a pas pu démarrer son interface.\n\n" +
            "Le détail de l'incident est dans :\n" + FrontendLog + "\n\n" +
            "La barre des tâches Windows a été rétablie. Réinstalle Winlestia si cela se reproduit.",
            "Winlestia", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        System.Windows.Application.Current.Shutdown();
    });
}
