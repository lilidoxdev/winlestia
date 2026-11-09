// language: C# 12, file: QuickTabRules.cs, target: Windows 11 / .NET 8, décisions pures des onglets rapides (cibles glass:)
using System;

namespace PanneauxGlass;

internal enum QuickTabView
{
    Unknown, Home, Apps, Games, Projects, Documents, Workspace, Note, Focus, Settings, Tools, Capture, Text, Clipboard, Audio,
    Workspaces, Files, Help, Pin, Memo, Controls, Pc, Palette
}

// Extraites de RunQuickTab : le Coordinator ne garde que l'aiguillage vers ses propres vues.
internal static class QuickTabRules
{
    internal const string Prefix = "glass:";
    // Une cible glass: désigne une vue interne ; toute autre cible est un raccourci Windows lancé tel quel.
    internal static bool IsInternal(string? target) => (target ?? "").StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
    // Le nom est insensible à la casse et aux espaces : les onglets saisis à la main ne doivent pas échouer pour une majuscule.
    internal static QuickTabView Parse(string? target)
    {
        if (!IsInternal(target))
            return QuickTabView.Unknown;
        return target![Prefix.Length..].Trim().ToLowerInvariant() switch
        {
            "home" => QuickTabView.Home,
            "apps" => QuickTabView.Apps,
            "games" => QuickTabView.Games,
            "projects" => QuickTabView.Projects,
            "documents" => QuickTabView.Documents,
            "workspace" => QuickTabView.Workspace,
            "note" => QuickTabView.Note,
            "focus" => QuickTabView.Focus,
            "settings" => QuickTabView.Settings,
            "tools" => QuickTabView.Tools,
            "capture" => QuickTabView.Capture,
            "text" => QuickTabView.Text,
            "clipboard" => QuickTabView.Clipboard,
            "audio" => QuickTabView.Audio,
            "workspaces" => QuickTabView.Workspaces,
            "files" => QuickTabView.Files,
            "help" => QuickTabView.Help,
            "pin" => QuickTabView.Pin,
            "memo" => QuickTabView.Memo,
            "controls" => QuickTabView.Controls,
            "pc" => QuickTabView.Pc,
            "palette" => QuickTabView.Palette,
            _ => QuickTabView.Unknown
        };
    }
}
