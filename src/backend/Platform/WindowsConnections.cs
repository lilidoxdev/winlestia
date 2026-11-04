// language: C# 12, file: WindowsConnections.cs, target: Windows 11 / .NET 8 WinRT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.Devices.Radios;
using Windows.Networking.Connectivity;

namespace PanneauxGlass.Platform;

internal sealed record RadioSnapshot(bool Present, bool On, string Name);

internal sealed record ConnectionSnapshot(
    RadioSnapshot Wifi,
    RadioSnapshot Bluetooth,
    bool Internet,
    string Network,
    bool Ethernet,
    string Error = ""
);

internal sealed class RadioAccessDeniedException(string message) : InvalidOperationException(message);

internal sealed class WindowsConnections
{
    IReadOnlyList<Radio> radios = Array.Empty<Radio>();
    RadioAccessStatus? access;

    internal async Task<ConnectionSnapshot> ReadAsync()
    {
        string error = "";
        try
        {
            radios = await Radio.GetRadiosAsync();
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        RadioSnapshot Read(RadioKind kind)
        {
            var r = radios.FirstOrDefault(r => r.Kind == kind);
            return new(r != null, r?.State == RadioState.On, r?.Name ?? "Non disponible");
        }
        bool internet = false,
            ethernet = false;
        string network = Strings.NotConnected;
        try
        {
            var profile = NetworkInformation.GetInternetConnectionProfile();
            if (profile != null)
            {
                internet = profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.InternetAccess;
                ethernet = profile.NetworkAdapter?.IanaInterfaceType == 6;
                network =
                    profile.IsWlanConnectionProfile ? profile.WlanConnectionProfileDetails.GetConnectedSsid()
                    : ethernet ? "Ethernet"
                    : profile.ProfileName;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }
        return new(Read(RadioKind.WiFi), Read(RadioKind.Bluetooth), internet, network, ethernet, error);
    }

    internal async Task<bool> ToggleAsync(RadioKind kind)
    {
        var radio = radios.FirstOrDefault(r => r.Kind == kind);
        if (radio == null)
            return false;
        if (access != RadioAccessStatus.Allowed)
            access = await Radio.RequestAccessAsync();
        if (access != RadioAccessStatus.Allowed)
            throw new RadioAccessDeniedException(Strings.WindowsDoesNotAllowThisCommand);
        var result = await radio.SetStateAsync(radio.State == RadioState.On ? RadioState.Off : RadioState.On);
        if (result != RadioAccessStatus.Allowed)
        {
            access = null;
            throw new InvalidOperationException(Strings.ConnectionChangeNotApplied);
        }
        return true;
    }
}
