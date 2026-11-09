// language: C# 12, file: QtShellBridge.Pipe.cs, target: Windows 11 / .NET 8, serveur du pipe, enveloppe des requêtes et réponse
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
    int firstReply; internal void Send(string value)
    {
        if (value == "palette")
            dispatcher.BeginInvoke(DispatcherPriority.Background, (Action)owner.RefreshCatalogueIfStale);
        if (frontendWindowPid > 0
            && (value is "toggle" or "access" or "media" or "performance" or "controls" or "palette" or "settings"
                    or "settings:social" or "settings:bookmarks" or "clipboard" or "audio" or "microphone" or "tools"
                    or "capture" or "wallpaper-switcher"
                || value.StartsWith("workspace:")))
            AllowSetForegroundWindow(frontendWindowPid);
        if (events.Count < 32)
            events.Enqueue((value, frontendEpoch));
        wake.Set();
    }

    // Avertit si le client parle une autre version du protocole (ou n'en annonce aucune : client ancien).
    static void CheckProtocol(JsonElement request)
    {
        int? announced = request.TryGetProperty("args", out var a) && a.ValueKind == JsonValueKind.Object
            && a.TryGetProperty("protocol", out var p) && p.TryGetInt32(out int v) ? v : null;
        if (announced != ProtocolVersion)
            Store.Log("Qt IPC: version de protocole différente (client " + (announced?.ToString() ?? "non annoncée")
                + ", serveur " + ProtocolVersion + ") : des commandes peuvent être ignorées.");
    }

    async Task Serve()
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                using var server =
                    new NamedPipeServerStream("PanneauxCaelestia",
                                              PipeDirection.InOut,
                                              1,
                                              PipeTransmissionMode.Byte,
                                              PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                pipe = server;
                await server.WaitForConnectionAsync(cancellation.Token);
                using var reader = new StreamReader(server, new UTF8Encoding(false), false, 65536, true);
                using var writer = new StreamWriter(server, new UTF8Encoding(false), 65536, true) { AutoFlush = true };
                bool versionChecked = false;
                while (server.IsConnected && !cancellation.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync(cancellation.Token);
                    if (line == null || line.Length > 131072)
                        break;
                    using var request = JsonDocument.Parse(line);
                    var element = request.RootElement.Clone();
                    if (!versionChecked)
                    {
                        versionChecked = true;
                        CheckProtocol(element);
                    }
                    var operation = await dispatcher.InvokeAsync(() => Handle(element));
                    string reply = await operation;
                    await writer.WriteLineAsync(reply.AsMemory(), cancellation.Token);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { if (!disposed) Store.Log("Qt IPC: " + ex.Message); }
            finally
            {
                pipe = null;
                if (!disposed)
                    _ = dispatcher.BeginInvoke((Action)(() =>
                    {
                        dockPreview.Close();
                        active = false;
                        CapturingShortcut = false;
                        dismissal.SetActive(false);
                        tiler.SetEnabled(false);
                        StopSampling();
                    }));
            }
        }
    }

    static string Text(JsonElement args, string name)
        => args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    static double Number(JsonElement args, string name)
        => args.TryGetProperty(name, out var value) && value.TryGetDouble(out double number) && double.IsFinite(number) ? number : 0;

    static bool Flag(JsonElement args, string name) => args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    string? lateNotice;
    object? lateWallpaperPicker;
    async Task<string> Handle(JsonElement request)
    {
        string? notice = null;
        object? wallpaperCapture = null;
        object? bookmarksItems = null;
        object? windowItems = null;
        object? wallpaperPicker = null;
        object? fileItems = null;
        try
        {
            string command = Text(request, "command");
            var args = request.TryGetProperty("args", out var a) ? a : default;
            string id = args.ValueKind == JsonValueKind.Object ? Text(args, "id") : "";

            if (args.ValueKind == JsonValueKind.Object && args.TryGetProperty("epoch", out _))
            {
                long epoch = (long)Number(args, "epoch");
                bool navigationChanged = epoch != frontendEpoch;
                if (navigationChanged)
                {
                    DesktopRecents.RefreshSoon();
                    dockPreview.Close();
                }
                if (Interlocked.Exchange(ref firstReply, 1) == 0)
                    Store.Log("Premier affichage Qt · t+" + (int)(DateTime.Now - Process.GetCurrentProcess().StartTime).TotalMilliseconds
                        + " ms");
                frontendEpoch = epoch;
                frontendWindowPid = (uint)Number(args, "pid");
                bool shown = Flag(args, "visible");
                tiler.ExcludedProcess = frontendWindowPid;
                explorer.ExcludedProcess = frontendWindowPid;
                tiler.SetEnabled(Store.State.AutoTile && !options.IsAutomated);
                if (Store.State.ScreenFrame)
                    frame.Enable();
                else
                    frame.Disable();
                if (!shown)
                    CapturingShortcut = false;
                page = (int)Number(args, "page");
                dockVisible = Flag(args, "dock");
                controls = Flag(args, "controls");
                mixerVisible = Flag(args, "mixer");
                wifiVisible = Flag(args, "wifi");
                barsVisible = Flag(args, "bars");
                if (active != shown || navigationChanged)
                {
                    active = shown;
                    dismissal.SetActive(active);
                }
                lastReply = DateTime.UtcNow;
                wifi.SetOpen(shown && wifiVisible && !Quiet);
                UpdateSampling();
            }
            var call = new BridgeCall(args, id);
            // Les sorties déjà posées par un handler survivent à une exception, comme avant le découpage.
            string? raw = await actionTable.Run(this, command, call, Store.Log);
            notice = call.Notice;
            wallpaperCapture = call.WallpaperCapture;
            bookmarksItems = call.BookmarksItems;
            windowItems = call.WindowItems;
            wallpaperPicker = call.WallpaperPicker;
            fileItems = call.FileItems;
            if (raw != null)
                return raw;
        }
        catch (Exception ex)
        {
            notice = ex.Message;
            Store.Log("Commande Qt: " + ex.Message);
        }
        notice ??= lateNotice;
        lateNotice = null;
        wallpaperPicker ??= lateWallpaperPicker;
        lateWallpaperPicker = null;
        var queued = events.Where(e => KeepEvent(e.name, e.epoch, frontendEpoch)).Select(e => e.name).ToArray();
        events.Clear();
        return JsonSerializer.Serialize(new
        {
            state = State(),
            events = queued,
            notice,
            wallpaperCapture,
            bookmarksItems,
            windows = windowItems,
            fileResults = fileItems,
            wallpaperPicker
        }, Json);
    }
}
