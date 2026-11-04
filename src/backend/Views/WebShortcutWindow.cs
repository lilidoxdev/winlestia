// language: C# 12, file: WebShortcutWindow.cs, target: Windows 11 / .NET 8 WPF, manual local images and persistent non-destructive cropping
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace PanneauxGlass.Views;

internal sealed class WebShortcutWindow : GlassWindow
{
    readonly TextBox url = new(),
        name = new();
    readonly Image preview = new()
    {
        Width = 84,
        Height = 84,
        Stretch = Stretch.Uniform,
    };
    readonly TextBlock status = UI.Text(Strings.ChooseYourImageOrKeepThe, 12, UI.Dim);
    readonly CheckBox glass = new() { Content = Strings.ShowAGlassSquareBehindThe };
    readonly Button recrop;
    string sourcePath = "";
    LogoCrop? crop;
    BitmapSource? selected;
    bool manualName;

    internal WebShortcutWindow(Coordinator coordinator, WebShortcut? original, bool chooseImage = false)
        : base("Panneaux Glass — " + (original == null ? Strings.AddASite : Strings.EditASite))
    {
        Width = 650;
        Height = 640;
        url.Height = name.Height = 40;
        ShowActivated = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ContentHost.Margin = new Thickness(30, 24, 30, 24);
        manualName = original != null;
        sourcePath = original?.SourceImagePath ?? "";
        crop = original?.Crop;
        var form = new StackPanel();
        ContentHost.Children.Add(form);
        var header = new DockPanel();
        var close = UI.IconButton("\uE711", Strings.Close, Close);
        DockPanel.SetDock(close, Dock.Right);
        header.Children.Add(close);
        header.Children.Add(
            UI.Text(original == null ? Strings.ASiteYourIcon : Strings.YourShortcutYourWay, 25, null, true)
        );
        form.Children.Add(header);
        var intro = UI.Text(Strings.TheLogoStaysAloneOnScreen, 13, UI.Dim);
        intro.Margin = new Thickness(0, 9, 0, 22);
        form.Children.Add(intro);
        void Label(string text)
        {
            var label = UI.Text(text, 10, UI.Dim, true);
            label.Margin = new Thickness(0, 0, 0, 8);
            form.Children.Add(label);
        }
        Label(Strings.SITEADDRESS);
        url.Text = original?.Url ?? "";
        System.Windows.Automation.AutomationProperties.SetName(url, Strings.WebsiteAddress);
        url.Margin = new Thickness(0, 0, 0, 16);
        form.Children.Add(url);
        Label(Strings.HOVERNAME);
        name.Text = original?.Name ?? "";
        System.Windows.Automation.AutomationProperties.SetName(name, Strings.SiteHoverName);
        name.Margin = new Thickness(0, 0, 0, 20);
        form.Children.Add(name);
        var imageRow = new Grid();
        imageRow.ColumnDefinitions.Add(new() { Width = new GridLength(110) });
        imageRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var previewFrame = new Border
        {
            Width = 96,
            Height = 96,
            CornerRadius = new CornerRadius(21),
            Background = UI.Brush("#12FFFFFF"),
            BorderBrush = UI.Brush("#28FFFFFF"),
            BorderThickness = new Thickness(1),
            Child = preview,
            HorizontalAlignment = HorizontalAlignment.Left,
            AllowDrop = true,
        };
        imageRow.Children.Add(previewFrame);
        var imageTools = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var choose = UI.Button(Strings.ChooseMyImage, () => ChooseImage());
        choose.HorizontalAlignment = HorizontalAlignment.Left;
        choose.Margin = new Thickness(0, 0, 0, 9);
        imageTools.Children.Add(choose);
        recrop = UI.Button(Strings.CropTheImage, () => CropImage());
        recrop.HorizontalAlignment = HorizontalAlignment.Left;
        imageTools.Children.Add(recrop);
        Grid.SetColumn(imageTools, 1);
        imageRow.Children.Add(imageTools);
        form.Children.Add(imageRow);
        previewFrame.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        previewFrame.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                CropImage(files[0], null);
            e.Handled = true;
        };
        if (original != null && File.Exists(original.IconPath))
        {
            try
            {
                preview.Source = WebIcons.Image(original.IconPath);
            }
            catch
            {
                preview.Source = WebStrip.FallbackImage();
            }
            if (!File.Exists(sourcePath))
            {
                sourcePath = original.IconPath;
                crop = null;
            }
        }
        else
            preview.Source = WebStrip.FallbackImage();
        recrop.IsEnabled = File.Exists(sourcePath);
        glass.IsChecked = original?.ShowGlass ?? true;
        glass.Margin = new Thickness(0, 19, 0, 10);
        form.Children.Add(glass);
        status.Height = 35;
        status.TextWrapping = TextWrapping.Wrap;
        form.Children.Add(status);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        actions.Children.Add(UI.Button(Strings.Cancel, Close));
        var save = UI.Button(original == null ? Strings.AddTheSite : Strings.Save, () => Save(coordinator, original));
        save.IsDefault = true;
        save.MinWidth = 150;
        save.Margin = new Thickness(12, 0, 0, 0);
        actions.Children.Add(save);
        form.Children.Add(actions);
        name.TextChanged += (_, _) =>
        {
            if (name.IsKeyboardFocusWithin)
                manualName = true;
        };
        url.TextChanged += (_, _) =>
        {
            if (manualName)
                return;
            try
            {
                var uri = WebIcons.Parse(url.Text);
                name.Text = uri.Host.StartsWith("www.") ? uri.Host[4..] : uri.Host;
            }
            catch { }
        };
        Loaded += (_, _) =>
        {
            url.Focus();
            if (chooseImage)
                Dispatcher.BeginInvoke((Action)ChooseImage);
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
                Close();
        };
    }

    void ChooseImage()
    {
        var dialog = new OpenFileDialog
        {
            Title = Strings.ChooseTheSiteLogo,
            Filter = Strings.ImagesPngJpgJpegIcoBmp,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) == true)
            CropImage(dialog.FileName, null);
    }

    void CropImage() => CropImage(sourcePath, crop);

    void CropImage(string path, LogoCrop? state)
    {
        if (!File.Exists(path))
            return;
        try
        {
            var dialog = new ImageCropWindow(path, state) { Owner = this, ShowInTaskbar = ShowInTaskbar };
            if (dialog.ShowDialog() == true && dialog.Output != null)
            {
                sourcePath = path;
                crop = dialog.Crop;
                selected = dialog.Output;
                preview.Source = selected;
                recrop.IsEnabled = true;
                status.Text = Strings.ImageReadyCropSavedWhenThe;
                status.Foreground = UI.Brush("#ACE8CC");
            }
        }
        catch (Exception ex)
        {
            status.Text = Strings.ThisImageCannotBeOpened + ex.Message;
            status.Foreground = UI.Brush("#F1C7A5");
        }
    }

    void Save(Coordinator coordinator, WebShortcut? original)
    {
        try
        {
            var uri = WebIcons.Parse(url.Text);
            if (string.IsNullOrWhiteSpace(name.Text))
                throw new FormatException(Strings.GiveThisShortcutAName);
            if (Store.State.Websites.Any(x => x.Url == uri.AbsoluteUri && x != original))
                throw new FormatException(Strings.ThisSiteIsAlreadyPresent);
            var site = original ?? new WebShortcut();
            string path = site.IconPath;
            bool fallback = site.Fallback;
            if (selected != null)
            {
                Directory.CreateDirectory(Path.Combine(Store.Root, "web-icons"));
                path = Path.Combine(
                    Store.Root,
                    "web-icons",
                    "custom-" + site.Id + "-" + Guid.NewGuid().ToString("N") + ".png"
                );
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(selected));
                using var output = File.Create(path);
                png.Save(output);
                fallback = false;
            }
            else if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.Combine(Store.Root, "web-icons"));
                path = Path.Combine(Store.Root, "web-icons", "default-" + site.Id + ".png");
                WebIcons.Default(path);
                fallback = true;
            }
            site.Name = name.Text.Trim();
            site.Url = uri.AbsoluteUri;
            site.IconPath = path;
            site.Fallback = fallback;
            site.ShowGlass = glass.IsChecked == true;
            site.SourceImagePath = sourcePath;
            site.Crop = crop;
            if (original == null)
                Store.State.Websites.Add(site);
            Store.Save();
            coordinator.RefreshWebsites();
            Close();
        }
        catch (Exception ex)
        {
            status.Text = ex.Message;
            status.Foreground = UI.Brush("#F1C7A5");
        }
    }
}
