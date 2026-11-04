// C#12 / WifiPolicy: security classification, SSID grouping and sorting, profile XML (escaping, hex SSID, types), password validation, error mapping.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using PanneauxGlass;
using PanneauxGlass.Platform;

class Test
{
    static WifiEntry E(string s, int sig, WifiSecurity sec, bool c = false, bool saved = false, string p = "") =>
        new(s, sig, sec, c, saved, p);

    static bool Parses(string xml)
    {
        try
        {
            XDocument.Parse(xml);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static int Main()
    {
        var r = new Dictionary<string, bool>();
        r["open"] =
            WifiRules.Classify(1, 0) == WifiSecurity.Open && WifiRules.Classify(1, 1) == WifiSecurity.Unsupported;
        r["wpa2"] = WifiRules.Classify(7, 4) == WifiSecurity.Wpa2Personal;
        r["wpa3"] = WifiRules.Classify(9, 4) == WifiSecurity.Wpa3Sae;
        r["wpaPersonal"] = WifiRules.Classify(4, 2) == WifiSecurity.WpaPersonal;
        r["enterprise"] =
            WifiRules.Classify(6, 4) == WifiSecurity.Enterprise
            && WifiRules.Classify(3, 2) == WifiSecurity.Enterprise
            && WifiRules.Classify(11, 4) == WifiSecurity.Enterprise;
        r["wepUnsupported"] =
            WifiRules.Classify(2, 5) == WifiSecurity.Unsupported
            && WifiRules.Classify(7, 1) == WifiSecurity.Unsupported
            && WifiRules.Classify(10, 4) == WifiSecurity.Unsupported;
        r["transitionMerge"] =
            WifiRules.Merge(WifiSecurity.Wpa2Personal, WifiSecurity.Wpa3Sae) == WifiSecurity.Wpa3Transition
            && WifiRules.Merge(WifiSecurity.Wpa3Transition, WifiSecurity.Wpa2Personal) == WifiSecurity.Wpa3Transition;
        r["enterpriseDominates"] =
            WifiRules.Merge(WifiSecurity.Enterprise, WifiSecurity.Wpa2Personal) == WifiSecurity.Enterprise
            && WifiRules.Merge(WifiSecurity.Open, WifiSecurity.Wpa2Personal) == WifiSecurity.Unsupported;
        r["canConnect"] =
            WifiRules.CanConnect(WifiSecurity.Open)
            && WifiRules.CanConnect(WifiSecurity.Wpa3Transition)
            && !WifiRules.CanConnect(WifiSecurity.Enterprise)
            && !WifiRules.CanConnect(WifiSecurity.Unsupported);
        var g = WifiRules.Group(
            new[]
            {
                E("b", 40, WifiSecurity.Wpa2Personal),
                E("a", 80, WifiSecurity.Wpa2Personal),
                E("b", 70, WifiSecurity.Wpa3Sae),
                E("", 90, WifiSecurity.Open),
                E("c", 10, WifiSecurity.Open, saved: true, p: "c"),
                E("d", 5, WifiSecurity.Open, c: true),
                E("A", 80, WifiSecurity.Open),
            }
        );
        r["groupOneRowPerSsid"] = g.Count == 5 && g.Count(e => e.Ssid == "b") == 1;
        r["groupMergesBestSignalAndSecurity"] =
            g.First(e => e.Ssid == "b") is { Signal: 70, Security: WifiSecurity.Wpa3Transition };
        r["hiddenDropped"] = !g.Any(e => e.Ssid.Length == 0);
        r["sortConnectedSavedSignal"] =
            g[0].Ssid == "d" && g[1].Ssid == "c" && g[2].Ssid == "A" && g[3].Ssid == "a" && g[4].Ssid == "b";
        r["signalClamped"] = WifiRules.Group(new[] { E("x", 250, WifiSecurity.Open) })[0].Signal == 100;
        r["bars"] =
            WifiRules.Bars(1) == 1 && WifiRules.Bars(30) == 2 && WifiRules.Bars(60) == 3 && WifiRules.Bars(99) == 4;
        r["pwOk"] =
            WifiRules.ValidatePassword(WifiSecurity.Wpa2Personal, "12345678") == ""
            && WifiRules.ValidatePassword(WifiSecurity.Wpa2Personal, new string('a', 63)) == "";
        r["pwShortLong"] =
            WifiRules.ValidatePassword(WifiSecurity.Wpa2Personal, "1234567") == "short"
            && WifiRules.ValidatePassword(WifiSecurity.Wpa2Personal, new string((char)122, 64)) == "long"
            && WifiRules.ValidatePassword(WifiSecurity.Wpa3Sae, null) == "short";
        r["pwHex64"] =
            WifiRules.ValidatePassword(WifiSecurity.Wpa2Personal, new string('A', 64)) == ""
            && WifiRules.ValidatePassword(WifiSecurity.Wpa2Personal, new string('g', 64)) == "long";
        r["pwChars"] = WifiRules.ValidatePassword(WifiSecurity.Wpa2Personal, "motdepasséé") == "chars";
        r["pwOpenNotNeeded"] = WifiRules.ValidatePassword(WifiSecurity.Open, null) == "";
        string wpa2 = WifiRules.BuildProfile("Home", WifiSecurity.Wpa2Personal, "abcdefgh");
        r["wpa2Xml"] =
            Parses(wpa2)
            && wpa2.Contains("<authentication>WPA2PSK</authentication>")
            && wpa2.Contains("<encryption>AES</encryption>")
            && wpa2.Contains("<keyType>passPhrase</keyType>")
            && !wpa2.Contains("<hex>");
        string sae = WifiRules.BuildProfile("Home", WifiSecurity.Wpa3Sae, "abcdefgh");
        r["saeXml"] =
            Parses(sae)
            && sae.Contains("<authentication>WPA3SAE</authentication>")
            && sae.Contains("<keyType>passPhrase</keyType>")
            && !sae.Contains("transitionMode");
        string trans = WifiRules.BuildProfile("Home", WifiSecurity.Wpa3Transition, "abcdefgh");
        r["transitionXml"] = Parses(trans) && trans.Contains("<transitionMode") && trans.Contains("WPA3SAE");
        string wpa = WifiRules.BuildProfile("Home", WifiSecurity.WpaPersonal, "abcdefgh");
        r["wpaXml"] =
            Parses(wpa)
            && wpa.Contains("<authentication>WPAPSK</authentication>")
            && wpa.Contains("<encryption>TKIP</encryption>");
        string open = WifiRules.BuildProfile("Cafe", WifiSecurity.Open, null);
        r["openXml"] =
            Parses(open)
            && open.Contains("<authentication>open</authentication>")
            && open.Contains("<encryption>none</encryption>")
            && !open.Contains("sharedKey");
        string hexKey = WifiRules.BuildProfile("Home", WifiSecurity.Wpa2Personal, new string('F', 64));
        r["hexKeyIsNetworkKey"] = hexKey.Contains("<keyType>networkKey</keyType>");
        string spaced = WifiRules.BuildProfile("Mon Wi-Fi", WifiSecurity.Open, null);
        r["spaceSsidHex"] = Parses(spaced) && spaced.Contains("<hex>4D6F6E2057692D4669</hex>");
        string accent = WifiRules.BuildProfile("Café", WifiSecurity.Open, null);
        r["nonAsciiSsidHex"] = Parses(accent) && accent.Contains("<hex>436166C3A9</hex>");
        string nasty = WifiRules.BuildProfile("a&b<c>\"d'", WifiSecurity.Wpa2Personal, "p&<>\"'ssword");
        r["escapingStrict"] =
            Parses(nasty)
            && nasty.Contains("a&amp;b&lt;c&gt;&quot;d&apos;")
            && nasty.Contains("p&amp;&lt;&gt;&quot;&apos;ssword");
        r["escapeDropsControl"] =
            WifiRules.XmlEscape("a\u0001b\u0000c") == "abc" && WifiRules.XmlEscape("\U0001F600") == "\U0001F600";
        bool threw = false;
        try
        {
            WifiRules.BuildProfile("Home", WifiSecurity.Wpa2Personal, "short");
        }
        catch (ArgumentException e)
        {
            threw = e.Message == "short";
        }
        r["badPasswordRejected"] = threw;
        bool leak = false;
        try
        {
            WifiRules.BuildProfile("Home", WifiSecurity.Wpa2Personal, "abcédefgh");
        }
        catch (ArgumentException e)
        {
            leak = e.Message.Contains("abc");
        }
        r["errorNeverCarriesPassword"] = !leak;
        bool enterpriseRefused = false;
        try
        {
            WifiRules.BuildProfile("Corp", WifiSecurity.Enterprise, "abcdefgh");
        }
        catch (ArgumentException)
        {
            enterpriseRefused = true;
        }
        r["enterpriseNoProfile"] = enterpriseRefused;
        bool longSsid = false;
        try
        {
            WifiRules.BuildProfile(new string('x', 33), WifiSecurity.Open, null);
        }
        catch (ArgumentException)
        {
            longSsid = true;
        }
        r["ssidOver32Bytes"] = longSsid && WifiRules.ValidSsid(new string('x', 32)) && !WifiRules.ValidSsid("");
        r["apiErrors"] =
            WifiRules.MapApiError(0) == ""
            && WifiRules.MapApiError(5) == "denied"
            && WifiRules.MapApiError(1062) == "serviceStopped"
            && WifiRules.MapApiError(9999) == "failed";
        r["reasons"] =
            WifiRules.MapReason(0, false) == ""
            && WifiRules.MapReason(0, true) == "timeout"
            && WifiRules.MapReason(0x48014, false) == "badPassword"
            && WifiRules.MapReason(0x48012, false) == "badPassword"
            && WifiRules.MapReason(0x2800B, false) == "notFound"
            && WifiRules.MapReason(0x10001, false) == "failed";
        r["reasonsNotAuthAreFailed"] =
            WifiRules.MapReason(0x48015, false) == "failed"
            && WifiRules.MapReason(0x48001, false) == "failed"
            && WifiRules.MapReason(5, false) == "failed";
        r["wpa3RejectsHex64"] =
            WifiRules.ValidatePassword(WifiSecurity.Wpa3Sae, new string((char)65, 64)) == "long"
            && WifiRules.ValidatePassword(WifiSecurity.Wpa3Transition, new string((char)65, 64)) == "long"
            && WifiRules.ValidatePassword(WifiSecurity.WpaPersonal, new string((char)65, 64)) == "";
        r["modeOpenManualProtectedAuto"] =
            WifiRules.BuildProfile("Cafe", WifiSecurity.Open, null).Contains("<connectionMode>manual</connectionMode>")
            && WifiRules
                .BuildProfile("Home", WifiSecurity.Wpa2Personal, "abcdefgh")
                .Contains("<connectionMode>auto</connectionMode>");
        r["listStates"] =
            WifiRules.ListState(1062, 0, 0, 0, false) == "serviceStopped"
            && WifiRules.ListState(0, 0, 0, 0, false) == "noInterface"
            && WifiRules.ListState(0, 1, 5, 0, false) == "location"
            && WifiRules.ListState(0, 1, 0, 0, true) == "location"
            && WifiRules.ListState(0, 1, 0, 0, false) == "empty"
            && WifiRules.ListState(0, 1, 0, 3, false) == "ok"
            && WifiRules.ListState(0, 1, 87, 0, false) == "failed";
        r["labels"] =
            WifiRules.SecurityLabel(WifiSecurity.Wpa3Transition) == "wpa3"
            && WifiRules.SecurityLabel(WifiSecurity.Enterprise) == "enterprise";
        int ok = 0;
        foreach (var v in r.Values)
            if (v)
                ok++;
        foreach (var k in r)
            if (!k.Value)
                Console.WriteLine(k.Key + "=false");
        Console.WriteLine("TOTAL " + ok + "/" + r.Count);
        return ok == r.Count ? 0 : 1;
    }
}
