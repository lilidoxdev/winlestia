// language: C# 12, file: ToolsWindow.cs, target: Windows 11 / .NET 8 WPF, Atelier hub and help
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PanneauxGlass;
internal sealed record ToolCard(string Category,string Glyph,string Title,string Hint,string Shortcut,Action Run);
internal sealed class ToolsWindow:GlassWindow {
    readonly Coordinator owner;readonly WrapPanel grid=new();readonly List<(Button Button,string Name)> categories=new();string filter="Tout";
    internal ToolsWindow(Coordinator coordinator):base("Panneaux Glass — atelier"){
        owner=coordinator;Width=720;Height=Math.Min(700,SystemParameters.WorkArea.Height-80);WindowStartupLocation=WindowStartupLocation.CenterOwner;Radius=34;ShowActivated=true;
        ContentHost.Margin=new Thickness(28,24,28,20);ContentHost.RowDefinitions.Add(new(){Height=GridLength.Auto});ContentHost.RowDefinitions.Add(new(){Height=GridLength.Auto});ContentHost.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        var header=new DockPanel();var close=UI.IconButton("\uE711","Fermer l’atelier",Close);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        var titles=new StackPanel();titles.Children.Add(UI.Text("L’atelier.",27,null,true));var sub=UI.Text("Les gestes utiles du quotidien, réunis au même endroit",12,UI.Dim);sub.Margin=new Thickness(0,7,0,0);titles.Children.Add(sub);header.Children.Add(titles);ContentHost.Children.Add(header);
        var chips=new WrapPanel{Margin=new Thickness(0,16,0,14)};foreach(string category in new[]{"Tout","Écran","Système","Fichiers","Confort"}){string local=category;var chip=UI.Button(category,()=>{filter=local;Build();});chip.Padding=new Thickness(13,7,13,7);chip.FontSize=11;chip.Margin=new Thickness(0,0,7,6);chip.Background=filter==category?UI.Brush("#3AD8EEFF"):UI.Brush("#0CFFFFFF");chip.Tag=category;categories.Add((chip,category));chips.Children.Add(chip);}Grid.SetRow(chips,1);ContentHost.Children.Add(chips);
        var scroll=new ScrollViewer{Content=grid,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,6,0)};Grid.SetRow(scroll,2);ContentHost.Children.Add(scroll);
        Loaded+=(_,_)=>{ApplyGlass();Build();};KeyDown+=(_,e)=>{if(e.Key==Key.Escape)Close();};
    }
    IEnumerable<ToolCard> Cards()=>new List<ToolCard>{
        new("Écran","\uE722","Capturer et annoter","Encadre une zone, dessine dessus, copie ou épingle","",()=>owner.OpenCapture()),
        new("Écran","\uE8A5","Texte d’une image","Encadre un texte, il est copié automatiquement","",()=>owner.OpenCapture(textOnly:true)),
        new("Écran","\uE790","Pipette de couleur","Agrandit sous le curseur et copie le code hexadécimal","",()=>owner.OpenColorPicker()),
        new("Écran","\uE7A3","Règle pixel","Mesure une distance à l’écran, en pixels et en centimètres","",()=>owner.OpenRuler()),
        new("Système","\uE840","Toujours au premier plan","Une fenêtre reste devant toutes les autres",owner.PinShortcutLabel,()=>owner.ToggleAlwaysOnTop()),
        new("Système","\uE7F5","Son et micro","Change de casque ou coupe le micro en un clic","",()=>owner.OpenAudio()),
        new("Système","\uE7ED","Notifications","Coupe ou rétablit les notifications Windows",SystemTools.NotificationsOn?"Activées":"Coupées",()=>owner.ToggleNotifications()),
        new("Système","\uE72C","Redémarrer l’Explorateur","Quand la barre des tâches ou le Bureau se bloquent","",()=>owner.RestartExplorer()),
        new("Fichiers","\uE8B7","Gros fichiers","Trouve ce qui remplit le disque, sans rien supprimer","",()=>owner.OpenLargeFiles()),
        new("Fichiers","\uE7C3","Profils d’espace","Rouvre tes applications et la disposition d’un moment","",()=>owner.OpenWorkspaces()),
        new("Confort","\uE70B","Papier pense-bête","Une feuille collée à l’écran : écris, place, garde","",()=>owner.OpenMemo()),
        new("Confort","\uE8C8","Presse-papiers","Tout ce que tu as copié, prêt à revenir","",()=>owner.OpenClipboard()),
        new("Confort","\uE897","Aide et raccourcis","Les gestes de Panneaux, en une page","F1",()=>owner.OpenHelp())
    };
    void Build(){
        grid.Children.Clear();
        foreach(var (chip,name) in categories)chip.Background=filter==name?UI.Brush("#3AD8EEFF"):UI.Brush("#0CFFFFFF");
        var cards=Cards().Where(card=>filter=="Tout"||card.Category==filter).ToList();
        int index=0;
        foreach(var card in cards){
            var border=new Border{Width=306,MinHeight=104,Margin=new Thickness(0,0,10,10),Padding=new Thickness(13),CornerRadius=new CornerRadius(20),Background=UI.Brush("#0EFFFFFF"),BorderBrush=UI.Brush("#20FFFFFF"),BorderThickness=new Thickness(1),Cursor=Cursors.Hand};
            var layout=new Grid();layout.ColumnDefinitions.Add(new(){Width=new GridLength(44)});layout.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            var bulb=new Border{Width=36,Height=36,CornerRadius=new CornerRadius(12),Background=UI.Brush("#1CFFFFFF"),VerticalAlignment=VerticalAlignment.Top};var glyph=UI.Glyph(card.Glyph,17);bulb.Child=glyph;layout.Children.Add(bulb);
            var text=new StackPanel{Margin=new Thickness(9,0,0,0)};var title=UI.Text(card.Title,13,null,true);text.Children.Add(title);
            var hint=UI.Text(card.Hint,10,UI.Dim);hint.TextWrapping=TextWrapping.Wrap;hint.Margin=new Thickness(0,4,0,0);text.Children.Add(hint);
            if(!string.IsNullOrWhiteSpace(card.Shortcut)){var key=UI.Text(card.Shortcut,9,UI.Accent);key.Margin=new Thickness(0,7,0,0);text.Children.Add(key);}
            Grid.SetColumn(text,1);layout.Children.Add(text);
            border.Child=layout;border.MouseLeftButtonUp+=(_,_)=>{Close();Dispatcher.BeginInvoke(card.Run);};
            System.Windows.Automation.AutomationProperties.SetName(border,card.Title);
            border.MouseEnter+=(_,_)=>{border.Background=UI.Brush("#1BFFFFFF");border.BorderBrush=UI.Brush("#48FFFFFF");};
            border.MouseLeave+=(_,_)=>{border.Background=UI.Brush("#0EFFFFFF");border.BorderBrush=UI.Brush("#20FFFFFF");};
            Motion.Stagger(border,index++);grid.Children.Add(border);
        }
        if(cards.Count==0){var empty=UI.Text("Rien dans cette catégorie.",12,UI.Dim);empty.Margin=new Thickness(6,20,0,0);grid.Children.Add(empty);}
    }
}
internal sealed class HelpWindow:GlassWindow {
    readonly Coordinator owner;
    internal HelpWindow(Coordinator coordinator):base("Panneaux Glass — aide"){
        owner=coordinator;Width=660;Height=Math.Min(720,SystemParameters.WorkArea.Height-80);WindowStartupLocation=WindowStartupLocation.CenterOwner;Radius=34;ShowActivated=true;
        ContentHost.Margin=new Thickness(28,24,28,20);ContentHost.RowDefinitions.Add(new(){Height=GridLength.Auto});ContentHost.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});ContentHost.RowDefinitions.Add(new(){Height=GridLength.Auto});
        var header=new DockPanel();var close=UI.IconButton("\uE711","Fermer l’aide",Close);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        var titles=new StackPanel();titles.Children.Add(UI.Text("Trois gestes, et tout est là.",26,null,true));var sub=UI.Text("Panneaux reste discret : une poignée à droite, deux raccourcis, un atelier",12,UI.Dim);sub.TextWrapping=TextWrapping.Wrap;sub.Margin=new Thickness(0,7,0,0);titles.Children.Add(sub);header.Children.Add(titles);ContentHost.Children.Add(header);
        var scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,18,0,12)};
        var body=new StackPanel();scroll.Content=body;Grid.SetRow(scroll,1);ContentHost.Children.Add(scroll);
        void Section(string title,string detail){var label=UI.Text(title,10,UI.Brush("#ABD2E4F1"),true);label.Margin=new Thickness(0,18,0,6);body.Children.Add(label);if(!string.IsNullOrWhiteSpace(detail)){var note=UI.Text(detail,12,UI.Dim);note.TextWrapping=TextWrapping.Wrap;note.Margin=new Thickness(0,0,0,8);body.Children.Add(note);}}
        void Row(string keys,string action){var grid=new Grid{Margin=new Thickness(0,0,0,6)};grid.ColumnDefinitions.Add(new(){Width=new GridLength(150)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});var key=UI.Text(keys,12,UI.Accent);key.VerticalAlignment=VerticalAlignment.Center;grid.Children.Add(key);var text=UI.Text(action,12);text.TextWrapping=TextWrapping.Wrap;Grid.SetColumn(text,1);grid.Children.Add(text);body.Children.Add(grid);}
        Section("OUVRIR","Approche le pointeur du bord droit de l’écran, ou utilise le raccourci global. Un deuxième appui replie tout, avec les fenêtres ouvertes depuis l’espace.");
        Row(GlobalShortcut.Label(Store.State.Shortcut.Modifiers,Store.State.Shortcut.Key),"Ouvre ou replie ton espace");
        Row(owner.PaletteLabel,"Commande rapide : applications, sites, calculs, minuteur, actions");
        Row(owner.PinShortcutLabel,"Toujours au premier plan : épingle la fenêtre active");
        Row("Ctrl 1 … Ctrl 4","Applications, Jeux, Projets, Documents");
        Row("Ctrl F","Place le curseur dans la recherche");
        Row("Ctrl H","Revient à l’accueil");
        Row("Échap","Replie l’espace et ferme ce qui est ouvert avec lui");
        Row("Ctrl Alt Retour arrière","Retrouve le Bureau organisé, même si l’interface ne répond plus");
        Section("L’ATELIER","Treize outils, tous dans la même fenêtre : capture annotée, papier pense-bête, texte d’une image, pipette, règle, presse-papiers, son, profils d’espace, gros fichiers. Un papier garde son texte, sa taille, sa place et sa transparence d’une session à l’autre. Raccourci dans la palette : tape « atelier ».");
        Section("TES RÉGLAGES","Dans Personnaliser : thème, mouvement Calme/Standard/Expressif, raccourcis, préservation du presse-papiers. Rien n’est imposé, tout se règle.");
        var footer=new DockPanel();var done=UI.Button("C’est parti",Close);done.MinWidth=120;DockPanel.SetDock(done,Dock.Right);footer.Children.Add(done);var tip=UI.Text("Cette page reste accessible depuis l’atelier.",10,UI.Dim);tip.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(tip);Grid.SetRow(footer,2);ContentHost.Children.Add(footer);
        Loaded+=(_,_)=>{ApplyGlass();if(!Store.State.TourDone){Store.State.TourDone=true;Store.Save();}};KeyDown+=(_,e)=>{if(e.Key==Key.Escape)Close();};
    }
}
