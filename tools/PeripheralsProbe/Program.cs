using System;
using System.Linq;
using PanneauxGlass;

if (args.Contains("--hid-raw"))
{
    foreach (var model in HidHeadsets.Models)
    {
        var paths = HidHeadsets.InterfacePaths(HidHeadsets.SteelSeries, model.Pid);
        foreach (var target in model.Targets)
        {
            var path = paths.FirstOrDefault(p => p.Contains(target.Interface, StringComparison.OrdinalIgnoreCase));
            if (path == null)
            {
                Console.WriteLine($"{model.Name} {target.Interface}: interface absente");
                continue;
            }
            var reports = HidHeadsets.Exchange(path, model.Request, target.UsagePage, 500, out var caps,
                m => Console.WriteLine("  trace: " + m));
            Console.WriteLine($"{path} usage={caps.UsagePage:X4}/{caps.Usage:X4} in={caps.InputReportByteLength} "
                + $"out={caps.OutputReportByteLength} reports={reports.Count}");
            foreach (var r in reports)
                Console.WriteLine("  " + BitConverter.ToString(r));
        }
    }
}
var all = await PeripheralScanner.ScanAsync();
Console.WriteLine($"{all.Count} peripheral(s)");
foreach (var c in all.OrderBy(c => c.Name))
    Console.WriteLine($"{c.Id} | {c.Name} | src={c.Source} | kind={c.DetectedKind} | connected={c.Connected} | "
        + $"battery={(c.Percent?.ToString() ?? "n/a")} | charging={(c.Charging?.ToString() ?? "n/a")} | approximate={c.Approximate}");
