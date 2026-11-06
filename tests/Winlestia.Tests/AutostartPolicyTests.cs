// C#12 / AutostartPolicy: Run to logon-task migration rule and task XML content.
using System;
using System.Collections.Generic;
using PanneauxGlass;
namespace Winlestia.Tests;
public class AutostartPolicyTests
{
    [Fact]
    public void AllChecks()
    {
        Assert.True(AutostartPolicy.Plan(true, @"C:\Users\u\AppData\Local\Programs\Winlestia\Glass-v5\Panneaux.Glass.exe") == AutostartPolicy.Step.CreateTaskThenDropRun, "runInstalledMigrates");
        Assert.True(AutostartPolicy.Plan(false, @"C:\x\Glass-v5\a.exe") == AutostartPolicy.Step.None, "noRunNoMigration");
        Assert.True(AutostartPolicy.Plan(true, @"C:\dev\bin\Panneaux.Glass.exe") == AutostartPolicy.Step.None, "devBuildNotMigrated");
        Assert.True(!AutostartPolicy.DropRunAfter(false) && AutostartPolicy.DropRunAfter(true), "runKeptWhenTaskFails");
        string x = AutostartPolicy.TaskXml(@"PC\a&b", @"C:\Program Files\W\Glass-v5\Panneaux.Glass.exe", @"C:\Program Files\W\Glass-v5");
        Assert.True(x.Contains("<LogonTrigger>") && x.Contains("<UserId>PC\\a&amp;b</UserId>") && !x.Contains("<Delay>"), "logonTriggerWithUser");
        Assert.True(x.Contains("<RunLevel>LeastPrivilege</RunLevel>") && x.Contains("<LogonType>InteractiveToken</LogonType>"), "leastPrivilegeInteractive");
        Assert.True(x.Contains("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>") && x.Contains("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>") && x.Contains("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>") && x.Contains("<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>") && x.Contains("<Priority>4</Priority>"), "settings");
        Assert.True(x.Contains("<Arguments>--qt-shell</Arguments>") && !x.Contains("--show"), "qtShellNeverShow");
        Assert.True(x.Contains("<Command>&quot;C:\\Program Files\\W\\Glass-v5\\Panneaux.Glass.exe&quot;</Command>"), "commandQuoted");
        try
        {
            var d = new System.Xml.XmlDocument();
            d.LoadXml(x);
            Assert.True(true, "xmlWellFormed");
        }
        catch { Assert.True(false, "xmlWellFormed"); }
    }
}
