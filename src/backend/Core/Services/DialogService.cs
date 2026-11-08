// language: C# 12, file: DialogService.cs, target: Windows 11 / .NET 8 WPF, portée des dialogues, boîtes de message et sélecteurs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace PanneauxGlass;

internal interface IDialogService
{
    void Layers(bool foreground);
    void Scope(Action body, bool resetAway = true);
    Task ScopeAsync(Func<Task> body);
    bool Confirm(string text);
    void Message(string text);
    string[] PickFiles();
    string? PickFolder();
}

internal sealed class DialogService : IDialogService
{
    sealed class DialogOwner : Forms.IWin32Window
    {
        public IntPtr Handle { get; }
        public DialogOwner(Window window) => Handle = new WindowInteropHelper(window).Handle;
    }
    readonly ShellState state;
    readonly Func<IEnumerable<Window>> layered;
    readonly Func<Window> parent;
    internal DialogService(ShellState state, Func<IEnumerable<Window>> layered, Func<Window> parent)
    {
        this.state = state;
        this.layered = layered;
        this.parent = parent;
    }
    // Pendant un dialogue les panneaux passent derrière lui : sans cela leur Topmost recouvrirait la boîte.
    public void Layers(bool foreground)
    {
        PinWindow.Lower(foreground);
        foreach (var window in layered())
            window.Topmost = !foreground;
    }
    // Le repli automatique est suspendu (SettingsOpen) le temps du dialogue ; l'état précédent est rétabli même sur exception.
    public void Scope(Action body, bool resetAway = true)
    {
        bool previous = state.SettingsOpen;
        state.SettingsOpen = true;
        Layers(true);
        try
        {
            body();
        }
        finally
        {
            Leave(previous, resetAway);
        }
    }
    public async Task ScopeAsync(Func<Task> body)
    {
        bool previous = state.SettingsOpen;
        state.SettingsOpen = true;
        Layers(true);
        try
        {
            await body();
        }
        finally
        {
            Leave(previous, true);
        }
    }
    void Leave(bool previous, bool resetAway)
    {
        state.SettingsOpen = previous;
        Layers(previous);
        // Le minuteur d'absence repart de zéro : la souris a pu quitter les panneaux pendant le dialogue.
        if (resetAway)
            state.AwaySince = DateTime.MinValue;
    }
    // La fenêtre active du process sert de propriétaire, sinon la boîte s'ouvrirait derrière le dialogue déjà affiché.
    Window ActiveOwner() => Application.Current.Windows.Cast<Window>().FirstOrDefault(w => w.IsActive) ?? parent();
    public bool Confirm(string text)
    {
        bool yes = false;
        Scope(() => yes = MessageBox.Show(ActiveOwner(), text, "Winlestia", MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes);
        return yes;
    }
    public void Message(string text)
        => Scope(() => MessageBox.Show(ActiveOwner(), text, "Winlestia", MessageBoxButton.OK, MessageBoxImage.Information), false);
    public string[] PickFiles()
    {
        var chosen = Array.Empty<string>();
        Scope(() =>
        {
            using var picker = new Forms.OpenFileDialog
            {
                Title = "Choisir un fichier, une application ou un raccourci",
                Filter = "Tous les fichiers|*.*",
                Multiselect = true
            };
            if (picker.ShowDialog(new DialogOwner(parent())) == Forms.DialogResult.OK)
                chosen = picker.FileNames;
        }, false);
        return chosen;
    }
    public string? PickFolder()
    {
        string? chosen = null;
        Scope(() =>
        {
            using var picker = new Forms.FolderBrowserDialog
            {
                Description = "Ajouter un dossier à ton espace",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = false
            };
            if (picker.ShowDialog(new DialogOwner(parent())) == Forms.DialogResult.OK)
                chosen = picker.SelectedPath;
        }, false);
        return chosen;
    }
}
