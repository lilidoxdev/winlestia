// language: C# 12, file: QtShellBridge.Actions.Peripherals.cs, target: Windows 11 / .NET 8, actions du pont : batterie des périphériques
// (dock)
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
namespace PanneauxGlass;

internal sealed partial class QtShellBridge
{
    PeripheralBatteryService? peripheralService;

    PeripheralBatteryService Peripherals => peripheralService ??= new PeripheralBatteryService(
        () => _ = dispatcher.BeginInvoke((Action)(() => { if (!disposed) Send("quick"); })));

    // Sondage lent seulement tant que le dock est affiché ET la fonction active ; sinon plus aucun minuteur.
    void UpdatePeripherals()
    {
        bool want = Store.State.PeripheralBatteryEnabled && dockVisible;
        if (want || peripheralService != null)
            Peripherals.SetActive(want);
    }

    // Lecture du cache uniquement : jamais d'appel système dans State().
    object PeripheralsState()
    {
        var snapshot = Store.State.PeripheralBatteryEnabled
            ? PeripheralLogic.Build(Peripherals.Candidates, Store.State.PeripheralHidden.ToHashSet(), Store.State.PeripheralKindOverrides)
            : PeripheralSnapshot.Empty;
        return new
        {
            enabled = Store.State.PeripheralBatteryEnabled,
            devices = snapshot.Devices.Select(d => new
            {
                id = d.Id,
                name = d.Name,
                kind = d.Kind,
                percent = d.Percent,
                charging = d.Charging,
                approximate = d.Approximate,
                hidden = d.Hidden,
                source = d.Source
            }).ToArray(),
            paired = snapshot.Paired.Select(p => new
            {
                id = p.Id,
                name = p.Name,
                kind = p.Kind,
                detectedKind = p.DetectedKind,
                overridden = p.Overridden,
                connected = p.Connected,
                percent = p.Percent,
                canConnect = Peripherals.Candidates.Any(k => k.Id == p.Id && k.Source == "bluetooth"
                    && k.DetectedKind is not ("keyboard" or "mouse" or "gamepad"))
            }).ToArray(),
            busy = bluetoothBusy
        };
    }

    string bluetoothBusy = "";

    [BridgeAction("bluetoothDevice")]
    void OnBluetoothDevice(BridgeCall c)
    {
        string id = Text(c.Args, "id");
        bool connect = Flag(c.Args, "connect");
        if (!BluetoothConnector.TryParseId(id, out ulong address)
            || System.Threading.Interlocked.CompareExchange(ref bluetoothBusy, id, "") != "")
            return;
        Send("quick");
        _ = Task.Run(async () =>
        {
            try
            {
                bool ok = await Task.Run(() => BluetoothConnector.SetConnected(address, connect, m => Store.Log("Bluetooth: " + m)))
                    .WaitAsync(TimeSpan.FromSeconds(15));
                if (!ok)
                    Store.Log("Bluetooth " + (connect ? "connexion" : "déconnexion") + " sans effet: " + id);
            }
            catch (Exception ex) { Store.Log("Bluetooth " + (connect ? "connexion" : "déconnexion") + ": " + ex.Message); }
            try
            {
                Peripherals.RefreshNow(force: true);
                await Task.Delay(1500);
                Peripherals.RefreshNow(force: true);
            }
            finally
            {
                System.Threading.Volatile.Write(ref bluetoothBusy, "");
                _ = dispatcher.BeginInvoke((Action)(() => { if (!disposed) Send("quick"); }));
            }
        });
    }

    static bool ValidPeripheralId(string id) => id.Length is > 3 and <= 64 && (id.StartsWith("bt:", StringComparison.Ordinal)
        || id.StartsWith("xinput:", StringComparison.Ordinal) || id.StartsWith("hid:", StringComparison.Ordinal));

    [BridgeAction("peripheralVisibility")]
    void OnPeripheralVisibility(BridgeCall c)
    {
        string id = Text(c.Args, "id");
        if (!ValidPeripheralId(id))
            return;
        Store.State.PeripheralHidden.RemoveAll(h => h == id);
        if (!Flag(c.Args, "visible"))
            Store.State.PeripheralHidden.Add(id);
        Store.Save();
    }

    [BridgeAction("peripheralKind")]
    void OnPeripheralKind(BridgeCall c)
    {
        string id = Text(c.Args, "id"), kind = Text(c.Args, "kind");
        if (!ValidPeripheralId(id))
            return;
        if (kind == "auto")
            Store.State.PeripheralKindOverrides.Remove(id);
        else if (PeripheralLogic.IsKind(kind))
            Store.State.PeripheralKindOverrides[id] = kind;
        else
            return;
        Store.Save();
    }

    [BridgeAction("peripheralsRefresh")]
    void OnPeripheralsRefresh(BridgeCall c) => Peripherals.RefreshNow(force: true);

    [BridgeAction("openBluetooth")]
    void OnOpenBluetooth(BridgeCall c)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) { Store.Log("Réglages Bluetooth: " + ex.Message); }
    }
}
