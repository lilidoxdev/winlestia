// Pure rules for the Wi-Fi list (no wlanapi, no window, no I/O): security classification, SSID grouping and sorting, WLAN profile XML, password validation, error mapping. Linked into tests/WifiPolicy.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PanneauxGlass;

enum WifiSecurity
{
    Open, Wpa2Personal, Wpa3Sae, Wpa3Transition, WpaPersonal, Enterprise, Unsupported
}

sealed record WifiEntry(string Ssid, int Signal, WifiSecurity Security, bool Connected, bool Saved, string Profile);

static class WifiRules
{
    internal const int MaxSsidBytes = 32;
    internal const string ProfileNamespace = "http://www.microsoft.com/networking/WLAN/profile/v1";
    internal const string TransitionNamespace = "http://www.microsoft.com/networking/WLAN/profile/v4";
    // Reason codes of wlanapi.h (WLAN_REASON_CODE_MSMSEC_CONNECT_BASE = 0x48000, NETWORK_NOT_AVAILABLE = 0x2800B).
    internal const uint ReasonMsmsecMin = 0x40000, ReasonMsmsecMax = 0x4FFFF, ReasonKeyFormat = 0x48012, ReasonPskMismatch = 0x48014, ReasonNetworkNotAvailable = 0x2800B;

    // DOT11_AUTH_ALGORITHM / DOT11_CIPHER_ALGORITHM values reported by WlanGetAvailableNetworkList.
    internal static WifiSecurity Classify(int auth, int cipher)
    {
        bool wep = cipher is 0x01 or 0x05 or 0x101;
        switch (auth)
        {
            case 1:
                return cipher == 0 ? WifiSecurity.Open : WifiSecurity.Unsupported;
            case 3 or 6 or 8 or 11:
                return WifiSecurity.Enterprise;
            case 4:
                return wep ? WifiSecurity.Unsupported : WifiSecurity.WpaPersonal;
            case 7:
                return wep ? WifiSecurity.Unsupported : WifiSecurity.Wpa2Personal;
            case 9:
                return wep ? WifiSecurity.Unsupported : WifiSecurity.Wpa3Sae;
            default:
                return WifiSecurity.Unsupported;
        }
    }

    internal static WifiSecurity Merge(WifiSecurity a, WifiSecurity b)
    {
        if (a == b)
            return a;
        if (a == WifiSecurity.Enterprise || b == WifiSecurity.Enterprise)
            return WifiSecurity.Enterprise;
        if (a == WifiSecurity.Unsupported || b == WifiSecurity.Unsupported)
            return WifiSecurity.Unsupported;
        bool aSae = a is WifiSecurity.Wpa3Sae or WifiSecurity.Wpa3Transition, bSae = b is WifiSecurity.Wpa3Sae or WifiSecurity.Wpa3Transition;
        bool aPsk = a is WifiSecurity.Wpa2Personal or WifiSecurity.Wpa3Transition, bPsk = b is WifiSecurity.Wpa2Personal or WifiSecurity.Wpa3Transition;
        if ((aSae && bPsk) || (aPsk && bSae))
            return WifiSecurity.Wpa3Transition;
        return WifiSecurity.Unsupported;
    }

    internal static bool NeedsPassword(WifiSecurity s) => s is WifiSecurity.Wpa2Personal or WifiSecurity.Wpa3Sae or WifiSecurity.Wpa3Transition or WifiSecurity.WpaPersonal;
    internal static bool CanConnect(WifiSecurity s) => s == WifiSecurity.Open || NeedsPassword(s);

    internal static string SecurityLabel(WifiSecurity s) => s switch
    {
        WifiSecurity.Open => "open",
        WifiSecurity.Wpa2Personal => "wpa2",
        WifiSecurity.Wpa3Sae => "wpa3",
        WifiSecurity.Wpa3Transition => "wpa3",
        WifiSecurity.WpaPersonal => "wpa",
        WifiSecurity.Enterprise => "enterprise",
        _ => "unsupported"
    };

    // One row per SSID (hidden networks with an empty name are not handled), best signal kept, flags merged; connected first, then saved, then signal, then name.
    internal static List<WifiEntry> Group(IEnumerable<WifiEntry> entries)
    {
        var map = new Dictionary<string, WifiEntry>(StringComparer.Ordinal);
        foreach (var e in entries)
        {
            if (string.IsNullOrEmpty(e.Ssid))
                continue;
            int signal = Math.Max(0, Math.Min(100, e.Signal));
            if (!map.TryGetValue(e.Ssid, out var old))
            {
                map[e.Ssid] = e with
                {
                    Signal = signal
                };
                continue;
            }
            map[e.Ssid] = old with
            {
                Signal = Math.Max(old.Signal, signal),
                Security = Merge(old.Security, e.Security),
                Connected = old.Connected || e.Connected,
                Saved = old.Saved || e.Saved,
                Profile = old.Profile.Length > 0 ? old.Profile : e.Profile
            };
        }
        return map.Values.OrderByDescending(e => e.Connected).ThenByDescending(e => e.Saved).ThenByDescending(e => e.Signal)
            .ThenBy(e => e.Ssid, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Ssid, StringComparer.Ordinal).ToList();
    }

    internal static int Bars(int signal) => signal >= 75 ? 4 : signal >= 50 ? 3 : signal >= 25 ? 2 : 1;

    // Returns "" when acceptable, else a short key ("short","long","chars","ssid").
    internal static string ValidatePassword(WifiSecurity s, string? password)
    {
        if (!NeedsPassword(s))
            return "";
        if (string.IsNullOrEmpty(password))
            return "short";
        foreach (char c in password)
            if (c < 32 || c > 126)
                return "chars";
        if (IsHexKey(password) && s is WifiSecurity.Wpa2Personal or WifiSecurity.WpaPersonal)
            return "";
        if (password.Length < 8)
            return "short";
        return password.Length > 63 ? "long" : "";
    }

    internal static bool IsHexKey(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    internal static bool NeedsHex(string ssid) => ssid.Any(c => c == ' ' || c < 33 || c > 126);

    internal static string Hex(string ssid) => Convert.ToHexString(new UTF8Encoding(false).GetBytes(ssid));

    internal static bool ValidSsid(string? ssid) => !string.IsNullOrEmpty(ssid) && new UTF8Encoding(false).GetByteCount(ssid) <= MaxSsidBytes;

    // Strict escaping; characters that XML 1.0 cannot carry are dropped.
    internal static string XmlEscape(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (c == '&')
                sb.Append("&amp;");
            else if (c == '<')
                sb.Append("&lt;");
            else if (c == '>')
                sb.Append("&gt;");
            else if (c == '"')
                sb.Append("&quot;");
            else if (c == '\'')
                sb.Append("&apos;");
            else if (c == '\t' || c == '\n' || c == '\r' || (c >= 0x20 && c < 0xD800) || (c >= 0xE000 && c <= 0xFFFD))
                sb.Append(c);
            else if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                sb.Append(c).Append(value[i + 1]);
                i++;
            }
        }
        return sb.ToString();
    }

    // WLANProfile for an infrastructure network. The profile name is the SSID text; the SSID itself goes in hex when it has spaces or non-ASCII characters.
    // Throws ArgumentException (message never contains the password) for an invalid SSID, an unsupported type or an invalid password.
    internal static string BuildProfile(string ssid, WifiSecurity security, string? password)
    {
        if (!ValidSsid(ssid))
            throw new ArgumentException("ssid");
        if (!CanConnect(security))
            throw new ArgumentException("unsupported");
        string check = ValidatePassword(security, password);
        if (check.Length > 0)
            throw new ArgumentException(check);
        string name = XmlEscape(ssid);
        string ssidXml = NeedsHex(ssid) ? "<hex>" + Hex(ssid) + "</hex><name>" + name + "</name>" : "<name>" + name + "</name>";
        string auth, cipher, extra = "";
        switch (security)
        {
            case WifiSecurity.Open:
                auth = "open";
                cipher = "none";
                break;
            case WifiSecurity.WpaPersonal:
                auth = "WPAPSK";
                cipher = "TKIP";
                break;
            case WifiSecurity.Wpa3Sae:
                auth = "WPA3SAE";
                cipher = "AES";
                break;
            case WifiSecurity.Wpa3Transition:
                auth = "WPA3SAE";
                cipher = "AES";
                extra = "<transitionMode xmlns=\"" + TransitionNamespace + "\">true</transitionMode>";
                break;
            default:
                auth = "WPA2PSK";
                cipher = "AES";
                break;
        }
        var sb = new StringBuilder(640);
        sb.Append("<?xml version=\"1.0\"?><WLANProfile xmlns=\"").Append(ProfileNamespace).Append("\"><name>").Append(name).Append("</name>")
          .Append("<SSIDConfig><SSID>").Append(ssidXml).Append("</SSID></SSIDConfig><connectionType>ESS</connectionType><connectionMode>").Append(security == WifiSecurity.Open ? "manual" : "auto").Append("</connectionMode>")
          .Append("<MSM><security><authEncryption><authentication>").Append(auth).Append("</authentication><encryption>").Append(cipher)
          .Append("</encryption><useOneX>false</useOneX>").Append(extra).Append("</authEncryption>");
        if (NeedsPassword(security))
        {
            string keyType = IsHexKey(password!) && security is WifiSecurity.Wpa2Personal or WifiSecurity.WpaPersonal ? "networkKey" : "passPhrase";
            sb.Append("<sharedKey><keyType>").Append(keyType).Append("</keyType><protected>false</protected><keyMaterial>").Append(XmlEscape(password!)).Append("</keyMaterial></sharedKey>");
        }
        sb.Append("</security></MSM></WLANProfile>");
        return sb.ToString();
    }

    // Win32 error of a wlanapi call to a short key ("" for success).
    internal static string MapApiError(int code) => code switch
    {
        0 => "",
        5 => "denied",
        1062 => "serviceStopped",
        1168 => "notFound",
        183 => "exists",
        1169 => "notFound",
        1 or 50 or 120 => "unsupported",
        _ => "failed"
    };

    // Outcome of a connection attempt: reason code of the notification (0 = connected), whether the 20 s bound elapsed. Only authentication reason codes mean a wrong password; anything else is a plain failure.
    internal static string MapReason(uint reason, bool timedOut)
    {
        if (reason == 0 && !timedOut)
            return "";
        if (timedOut)
            return "timeout";
        if (reason == ReasonPskMismatch || reason == ReasonKeyFormat)
            return "badPassword";
        if (reason == ReasonNetworkNotAvailable)
            return "notFound";
        return "failed";
    }

    // Reason shown for an empty or refused list. state: "ok" with entries, else the key the interface translates.
    internal static string ListState(int openCode, int interfaces, int listCode, int count, bool locationDenied)
    {
        if (openCode == 1062)
            return "serviceStopped";
        if (openCode != 0)
            return "noInterface";
        if (interfaces == 0)
            return "noInterface";
        if (listCode == 5 || (listCode == 0 && count == 0 && locationDenied))
            return "location";
        if (listCode != 0)
            return "failed";
        return count == 0 ? "empty" : "ok";
    }
}
