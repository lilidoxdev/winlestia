// C# 12 / CliCommands.cs / Pure parsing and allow-list for the `winlestia` command line. No I/O besides the injected file check.
using System;
using System.IO;
using System.Linq;

namespace PanneauxGlass;

internal enum CliKind
{
    Help,
    Status,
    Panel,
    DesktopNext,
    DesktopPrev,
    DesktopNew,
    DesktopGo,
    Wallpaper,
    WallpaperList,
    Palette,
    FindWindows,
    FindFiles,
    Brightness,
    BrightnessSet,
    Visualizer,
    Quiet,
    Wifi,
}

internal sealed record CliCommand(CliKind Kind, string Arg = "", int Number = 0, int Value = 0);

internal static class CliCommands
{
    // Explicit allow-list. Nothing here shuts down, restarts, signs out or deletes anything.
    internal static readonly string[] Panels =
    {
        "settings",
        "clipboard",
        "tools",
        "audio",
        "mixer",
        "capture",
        "palette",
        "dashboard",
    };
    internal static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };
    internal const int MaxArgs = 4;

    internal const string HelpText =
        "Usage: winlestia <command>\n"
        + "  status                       print the shell state as JSON\n"
        + "  panel <name>                 open a panel: settings, clipboard, tools, audio, mixer, capture, palette, dashboard\n"
        + "  desktop next|prev|new|<n>    switch virtual desktop (n starts at 1) or create one\n"
        + "  wallpaper <path>             set the wallpaper to an existing image file\n"
        + "  wallpaper <path> --display <id>  set it on one display only (ids from 'wallpaper list')\n"
        + "  wallpaper list               print {\"displays\":[{\"id\":n,\"name\":s,\"path\":s}],\"animated\":[s]} (animated wallpaper software running)\n"
        + "  palette                      open the quick command palette\n"
        + "  find windows                 print {\"count\":n} for the open windows (no titles)\n"
        + "  find files <term>            print {\"count\":n,\"indexAvailable\":b} for the Windows index (no paths)\n"
        + "  brightness                   print the displays as JSON [{\"id\":n,\"name\":s,\"value\":0-100,\"available\":b}]\n"
        + "  brightness <id> <0-100>      set the brightness of one display (DDC/CI)\n"
        + "  visualizer                   print {\"running\":b,\"frames\":n,\"panelOpen\":b,\"wanted\":b} for the dashboard audio bars (frames since the last start)\n"
        + "  quiet                        print {\"quiet\":b,\"forced\":\"auto|on|off\",\"detected\":b} for the fullscreen quiet mode\n"
        + "  quiet on|off|auto            force the quiet mode without a fullscreen window (not saved: back to auto when the shell restarts)\n"
        + "  wifi                         print {\"available\":b,\"connected\":b,\"networks\":n,\"reason\":s} for the Wi-Fi (read-only, no network name)\n"
        + "  help                         show this text\n"
        + "Exit codes: 0 success, 1 refused by the shell, 2 unknown command or bad argument, 3 shell not running.";

    internal static bool TryParse(string[]? args, out CliCommand? command, out string error)
    {
        command = null;
        error = "";
        if (args == null || args.Length == 0)
        {
            command = new(CliKind.Help);
            return true;
        }
        if (args.Length > MaxArgs)
        {
            error = "Too many arguments.";
            return false;
        }
        string name = (args[0] ?? "").ToLowerInvariant();
        switch (name)
        {
            case "help" or "--help" or "-h" or "/?" when args.Length == 1:
                command = new(CliKind.Help);
                return true;
            case "status" when args.Length == 1:
                command = new(CliKind.Status);
                return true;
            case "visualizer" when args.Length == 1:
                command = new(CliKind.Visualizer);
                return true;
            case "palette" when args.Length == 1:
                command = new(CliKind.Palette);
                return true;
            case "wifi" when args.Length == 1:
                command = new(CliKind.Wifi);
                return true;
            case "quiet" when args.Length == 1:
                command = new(CliKind.Quiet);
                return true;
            case "quiet" when args.Length == 2:
            {
                if (!QuietPolicy.TryParse(args[1], out var forced))
                {
                    error = "Quiet mode must be on, off or auto.";
                    return false;
                }
                command = new(CliKind.Quiet, QuietPolicy.Label(forced));
                return true;
            }
            case "panel" when args.Length == 2:
            {
                string panel = (args[1] ?? "").ToLowerInvariant();
                if (Array.IndexOf(Panels, panel) < 0)
                {
                    error = "Unknown panel '" + Clip(args[1]) + "'. Known: " + string.Join(", ", Panels) + ".";
                    return false;
                }
                command = new(CliKind.Panel, panel);
                return true;
            }
            case "desktop" when args.Length == 2:
            {
                string target = (args[1] ?? "").ToLowerInvariant();
                switch (target)
                {
                    case "next":
                        command = new(CliKind.DesktopNext);
                        return true;
                    case "prev":
                    case "previous":
                        command = new(CliKind.DesktopPrev);
                        return true;
                    case "new":
                        command = new(CliKind.DesktopNew);
                        return true;
                }
                if (
                    target.Length is >= 1 and <= 2
                    && target.All(char.IsAsciiDigit)
                    && int.Parse(target) is >= 1 and <= 20
                )
                {
                    command = new(CliKind.DesktopGo, "", int.Parse(target));
                    return true;
                }
                error = "Desktop must be next, prev, new or a number from 1 to 20.";
                return false;
            }
            case "find" when args.Length == 2 && (args[1] ?? "").ToLowerInvariant() == "windows":
                command = new(CliKind.FindWindows);
                return true;
            case "find" when args.Length >= 3 && (args[1] ?? "").ToLowerInvariant() == "files":
            {
                string term = string.Join(" ", args.Skip(2)).Trim();
                if (term.Length is < 2 or > 64 || term.Any(char.IsControl))
                {
                    error = "Search term must have 2 to 64 characters.";
                    return false;
                }
                command = new(CliKind.FindFiles, term);
                return true;
            }
            case "brightness" when args.Length == 1:
                command = new(CliKind.Brightness);
                return true;
            case "brightness" when args.Length == 3:
            {
                string id = args[1] ?? "",
                    value = args[2] ?? "";
                if (id.Length is < 1 or > 2 || !id.All(char.IsAsciiDigit) || int.Parse(id) < 1)
                {
                    error = "Display id must be a number from 1 to 99 (see 'winlestia brightness').";
                    return false;
                }
                if (value.Length is < 1 or > 3 || !value.All(char.IsAsciiDigit) || int.Parse(value) > 100)
                {
                    error = "Brightness must be a whole number from 0 to 100.";
                    return false;
                }
                command = new(CliKind.BrightnessSet, "", int.Parse(id), int.Parse(value));
                return true;
            }
            case "wallpaper" when args.Length == 2 && (args[1] ?? "").ToLowerInvariant() == "list":
                command = new(CliKind.WallpaperList);
                return true;
            case "wallpaper" when args.Length >= 2 && (args[1] ?? "").ToLowerInvariant() == "--display":
                error =
                    args.Length == 2
                        ? "--display needs an image path first, then a display id: wallpaper <path> --display <id>."
                        : "--display must come after the image path: wallpaper <path> --display <id>.";
                return false;
            case "wallpaper" when args.Length == 3 && (args[2] ?? "").ToLowerInvariant() == "--display":
                error = "--display needs a display id (see 'winlestia wallpaper list').";
                return false;
            case "wallpaper" when args.Length == 2:
                command = new(CliKind.Wallpaper, args[1] ?? "");
                return true;
            case "wallpaper" when args.Length == 4 && (args[2] ?? "").ToLowerInvariant() == "--display":
            {
                string id = args[3] ?? "";
                if (id.Length is < 1 or > 2 || !id.All(char.IsAsciiDigit) || int.Parse(id) < 1)
                {
                    error = "Display id must be a number from 1 to 99 (see 'winlestia wallpaper list').";
                    return false;
                }
                command = new(CliKind.Wallpaper, args[1] ?? "", int.Parse(id));
                return true;
            }
        }
        error = "Unknown command or wrong arguments: '" + Clip(name) + "'. Try 'winlestia help'.";
        return false;
    }

    // Absolute local path, image extension, existing file. UNC and device paths are refused (no network authentication triggered by a script).
    internal static bool ValidWallpaper(string? path, Func<string, bool> fileExists, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(path) || path.Length > 259)
        {
            error = "Wallpaper path is empty or too long.";
            return false;
        }
        if (
            path.StartsWith(@"\\")
            || path.StartsWith("//")
            || path.IndexOf('\0') >= 0
            || path.IndexOf(':', 2) >= 0
            || !Path.IsPathFullyQualified(path)
        )
        {
            error = "Wallpaper path must be a local absolute path.";
            return false;
        }
        if (Array.IndexOf(ImageExtensions, Path.GetExtension(path).ToLowerInvariant()) < 0)
        {
            error = "Wallpaper must be a .jpg, .jpeg, .png, .bmp, .gif or .webp file.";
            return false;
        }
        if (!fileExists(path))
        {
            error = "Wallpaper file not found.";
            return false;
        }
        return true;
    }

    static string Clip(string? text)
    {
        text ??= "";
        return text.Length > 40 ? text[..40] : text;
    }
}
