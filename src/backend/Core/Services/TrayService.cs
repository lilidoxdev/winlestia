// language: C# 12, file: TrayService.cs, target: Windows 11 / .NET 8, icône de la zone de notification et bulles
using System;
using Forms = System.Windows.Forms;

namespace PanneauxGlass;

internal interface ITrayService : IDisposable
{
    event Action? DoubleClicked;
    event Action? RightClicked;
    void Show(string tooltip);
    void SetMenu(Forms.ContextMenuStrip menu);
    void Notify(string title, string text);
}

internal sealed class TrayService : ITrayService
{
    // Durée demandée à Windows ; il l'ignore souvent au profit du réglage d'accessibilité de l'utilisateur.
    internal const int BalloonMilliseconds = 4000;
    readonly Forms.NotifyIcon icon = new();
    public event Action? DoubleClicked;
    public event Action? RightClicked;
    // Seul le clic droit ouvre le menu propre à l'application : le clic gauche reste réservé au double-clic.
    internal static bool OpensMenu(Forms.MouseButtons button) => button == Forms.MouseButtons.Right;
    internal TrayService()
    {
        icon.MouseUp += (_, e) =>
        {
            if (OpensMenu(e.Button))
                RightClicked?.Invoke();
        };
        icon.DoubleClick += (_, _) => DoubleClicked?.Invoke();
    }
    public void Show(string tooltip)
    {
        icon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application;
        icon.Text = tooltip;
        icon.Visible = true;
    }
    public void SetMenu(Forms.ContextMenuStrip menu) => icon.ContextMenuStrip = menu;
    public void Notify(string title, string text)
    {
        try
        {
            icon.BalloonTipTitle = title;
            icon.BalloonTipText = text;
            icon.BalloonTipIcon = Forms.ToolTipIcon.Info;
            icon.ShowBalloonTip(BalloonMilliseconds);
        }
        catch (Exception ex) { Store.Log("Notification: " + ex.Message); }
    }
    public void Dispose()
    {
        icon.Visible = false;
        icon.ContextMenuStrip?.Dispose();
        icon.Dispose();
    }
}
