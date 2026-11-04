// language: C# 12, file: Models.cs, target: Windows 11 / .NET 8, persistent local workspace
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace PanneauxGlass.Storage;

public sealed class Entry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    public string Section { get; set; } = "";
    public string Category { get; set; } = "";
    public string IconPath { get; set; } = "";
}

public sealed class Catalogue
{
    public List<Entry> Entries { get; set; } = new();
    public string GeneratedAt { get; set; } = "";
    public string[] Diagnostics { get; set; } = Array.Empty<string>();
}

public sealed class BoardState
{
    public bool FavoritesOnly { get; set; }
    public PanelPlacement? Placement { get; set; }
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Accent { get; set; } = "#7ADCF4";
    public string Filter { get; set; } = "Tous";
}

public sealed class WebShortcut
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string IconPath { get; set; } = "";
    public bool Fallback { get; set; }
    public string SourceImagePath { get; set; } = "";
    public LogoCrop? Crop { get; set; }
    public bool ShowGlass { get; set; } = true;
}

public sealed class LogoCrop
{
    public double Zoom { get; set; } = 1;
    public double X { get; set; }
    public double Y { get; set; }
    public bool Centered { get; set; } = true;
}

public sealed class WorkspaceProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public List<string> Targets { get; set; } = new();
    public Dictionary<string, PanelPlacement?> Layout { get; set; } = new();
}

public sealed class ClipboardItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "text";
    public string Text { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public bool Pinned { get; set; }
    public string At { get; set; } = "";
}

public sealed class LauncherTool
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
}

// A sheet taped to the screen: a captured image or a handwritten memo. Everything that makes it
// feel placed — text, width, position, transparency, angle — is kept here and restored at startup.
public sealed class PinNote
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "memo";
    public string Text { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Width { get; set; } = 300;
    public double Height { get; set; } = 0;
    public double Opacity { get; set; } = 1;
    public double Angle { get; set; } = -2.4;
    public string Created { get; set; } = DateTime.Now.ToString("O");
}
