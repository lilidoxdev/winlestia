// C#12 / AutostartPolicy: Run to logon-task migration rule and task XML content.
using System;
using System.Collections.Generic;
using PanneauxGlass;
using PanneauxGlass.Platform;

class Test
{
    static int Main()
    {
        var r = new Dictionary<string, bool>();
        r["runInstalledMigrates"] =
            AutostartPolicy.Plan(true, @"C:\Users\u\AppData\Local\Programs\Winlestia\Glass-v5\Panneaux.Glass.exe")
            == AutostartPolicy.Step.CreateTaskThenDropRun;
        r["noRunNoMigration"] = AutostartPolicy.Plan(false, @"C:\x\Glass-v5\a.exe") == AutostartPolicy.Step.None;
        r["devBuildNotMigrated"] =
            AutostartPolicy.Plan(true, @"C:\dev\bin\Panneaux.Glass.exe") == AutostartPolicy.Step.None;
        r["runKeptWhenTaskFails"] = !AutostartPolicy.DropRunAfter(false) && AutostartPolicy.DropRunAfter(true);
        string x = AutostartPolicy.TaskXml(
            @"PC\a&b",
            @"C:\Program Files\W\Glass-v5\Panneaux.Glass.exe",
            @"C:\Program Files\W\Glass-v5"
        );
        r["logonTriggerWithUser"] =
            x.Contains("<LogonTrigger>") && x.Contains("<UserId>PC\\a&amp;b</UserId>") && !x.Contains("<Delay>");
        r["leastPrivilegeInteractive"] =
            x.Contains("<RunLevel>LeastPrivilege</RunLevel>") && x.Contains("<LogonType>InteractiveToken</LogonType>");
        r["settings"] =
            x.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>")
            && x.Contains("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>")
            && x.Contains("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>")
            && x.Contains("<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>")
            && x.Contains("<Priority>4</Priority>");
        r["qtShellNeverShow"] = x.Contains("<Arguments>--qt-shell</Arguments>") && !x.Contains("--show");
        r["commandQuoted"] = x.Contains(
            "<Command>&quot;C:\\Program Files\\W\\Glass-v5\\Panneaux.Glass.exe&quot;</Command>"
        );
        try
        {
            var d = new System.Xml.XmlDocument();
            d.LoadXml(x);
            r["xmlWellFormed"] = true;
        }
        catch
        {
            r["xmlWellFormed"] = false;
        }
        int ok = 0;
        foreach (var v in r.Values)
            if (v)
                ok++;
        foreach (var k in r)
            Console.WriteLine(k.Key + "=" + (k.Value ? "true" : "false"));
        Console.WriteLine("TOTAL " + ok + "/" + r.Count);
        return ok == r.Count ? 0 : 1;
    }
}
