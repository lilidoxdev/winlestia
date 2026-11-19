// language: C# 12, file: Coordinator.Feedback.cs, target: Windows 11 / .NET 8 WPF, toasts, notifications et boîtes de message
namespace PanneauxGlass;

internal sealed partial class Coordinator
{
    internal void ShowToast(string text) => Toast.Show(text, Anchor);
    internal void Notify(string title, string text) => tray.Notify(title, text);
    internal bool Confirm(string text) => dialogs.Confirm(text);
    internal void Message(string text) => dialogs.Message(text);
}
