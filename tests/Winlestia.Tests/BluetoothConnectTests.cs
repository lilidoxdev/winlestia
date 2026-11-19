// C#12 / Connexion Bluetooth : analyse de l'identifiant, périphérique inconnu sans plantage, action déclarée au contrat.
using System.Linq;
using PanneauxGlass;
namespace Winlestia.Tests;
public class BluetoothConnectTests
{
    [Theory]
    [InlineData("bt:001122AABBCC", true)]
    [InlineData("bt:001122aabbcc", true)]
    [InlineData("xinput:0", false)]
    [InlineData("bt:XYZ", false)]
    [InlineData("bt:001122AABBCCDD", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ParsesPairedId(string? id, bool ok)
    {
        Assert.Equal(ok, BluetoothConnector.TryParseId(id, out _));
    }

    [Fact]
    public void ParsedAddressIsHex()
    {
        Assert.True(BluetoothConnector.TryParseId("bt:001122AABBCC", out ulong a));
        Assert.Equal(0x001122AABBCCUL, a);
    }

    [Fact]
    public void UnknownDeviceDoesNotThrow()
    {
        Assert.False(BluetoothConnector.SetConnected(0x0000DEADBEEFUL, true));
        Assert.False(BluetoothConnector.SetConnected(0x0000DEADBEEFUL, false));
    }

    [Fact]
    public void ActionIsRegistered()
    {
        Assert.Contains("bluetoothDevice", QtShellBridge.ActionNames.ToList());
    }

    [Fact]
    public void DeviceInfoSizeMatchesNative()
    {
        Assert.Equal(560, System.Runtime.InteropServices.Marshal.SizeOf<BluetoothConnector.BluetoothDeviceInfo>());
    }

    [Theory]
    [InlineData(0x002540u, true)]  // clavier
    [InlineData(0x002580u, true)]  // souris
    [InlineData(0x240404u, false)] // casque audio
    [InlineData(0x000000u, false)]
    public void DetectsInputDevices(uint cod, bool input)
    {
        Assert.Equal(input, BluetoothConnector.IsInputDevice(cod));
    }
}
