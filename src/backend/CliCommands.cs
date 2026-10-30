// C# 12 / CliCommands.cs / Pure parsing and allow-list for the `winlestia` command line. No I/O besides the injected file check.
using System;
using System.IO;
using System.Linq;

namespace PanneauxGlass;

internal enum CliKind { Help, Status, Panel, DesktopNext, DesktopPrev, DesktopNew, DesktopGo, Wallpaper, Palette }

internal sealed record CliCommand(CliKind Kind, string Arg = "", int Number = 0);

internal static class CliCommands {
    // Explicit allow-list. Nothing here shuts down, restarts, signs out or deletes anything.
    internal static readonly string[] Panels = { "settings", "clipboard", "tools", "audio", "mixer", "capture", "palette" };
    internal static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif" };
    internal const int MaxArgs = 4;

    internal const string HelpText =
        "Usage: winlestia <command>\n" +
        "  status                       print the shell state as JSON\n" +
        "  panel <name>                 open a panel: settings, clipboard, tools, audio, mixer, capture, palette\n" +
        "  desktop next|prev|new|<n>    switch virtual desktop (n starts at 1) or create one\n" +
        "  wallpaper <path>             set the wallpaper to an existing image file\n" +
        "  palette                      open the quick command palette\n" +
        "  help                         show this text\n" +
        "Exit codes: 0 success, 1 refused by the shell, 2 unknown command or bad argument, 3 shell not running.";

    internal static bool TryParse(string[]? args, out CliCommand? command, out string error) {
        command = null; error = "";
        if (args == null || args.Length == 0) { command = new(CliKind.Help); return true; }
        if (args.Length > MaxArgs) { error = "Too many arguments."; return false; }
        string name = (args[0] ?? "").ToLowerInvariant();
        switch (name) {
            case "help" or "--help" or "-h" or "/?" when args.Length == 1: command = new(CliKind.Help); return true;
            case "status" when args.Length == 1: command = new(CliKind.Status); return true;
            case "palette" when args.Length == 1: command = new(CliKind.Palette); return true;
            case "panel" when args.Length == 2: {
                string panel = (args[1] ?? "").ToLowerInvariant();
                if (Array.IndexOf(Panels, panel) < 0) { error = "Unknown panel '" + Clip(args[1]) + "'. Known: " + string.Join(", ", Panels) + "."; return false; }
                command = new(CliKind.Panel, panel); return true;
            }
            case "desktop" when args.Length == 2: {
                string target = (args[1] ?? "").ToLowerInvariant();
                switch (target) {
                    case "next": command = new(CliKind.DesktopNext); return true;
                    case "prev": case "previous": command = new(CliKind.DesktopPrev); return true;
                    case "new": command = new(CliKind.DesktopNew); return true;
                }
                if (target.Length is >= 1 and <= 2 && target.All(char.IsAsciiDigit) && int.Parse(target) is >= 1 and <= 20) { command = new(CliKind.DesktopGo, "", int.Parse(target)); return true; }
                error = "Desktop must be next, prev, new or a number from 1 to 20."; return false;
            }
            case "wallpaper" when args.Length == 2: command = new(CliKind.Wallpaper, args[1] ?? ""); return true;
        }
        error = "Unknown command or wrong arguments: '" + Clip(name) + "'. Try 'winlestia help'."; return false;
    }

    // Absolute local path, image extension, existing file. UNC and device paths are refused (no network authentication triggered by a script).
    internal static bool ValidWallpaper(string? path, Func<string, bool> fileExists, out string error) {
        error = "";
        if (string.IsNullOrWhiteSpace(path) || path.Length > 259) { error = "Wallpaper path is empty or too long."; return false; }
        if (path.StartsWith(@"\\") || path.StartsWith("//") || path.IndexOf('\0') >= 0 || path.IndexOf(':', 2) >= 0 || !Path.IsPathFullyQualified(path)) { error = "Wallpaper path must be a local absolute path."; return false; }
        if (Array.IndexOf(ImageExtensions, Path.GetExtension(path).ToLowerInvariant()) < 0) { error = "Wallpaper must be a .jpg, .jpeg, .png, .bmp or .gif file."; return false; }
        if (!fileExists(path)) { error = "Wallpaper file not found."; return false; }
        return true;
    }

    static string Clip(string? text) { text ??= ""; return text.Length > 40 ? text[..40] : text; }
}
