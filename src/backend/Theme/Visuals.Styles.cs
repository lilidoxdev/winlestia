// UI : styles XAML des contrôles (boutons, champs) régénérés à chaque thème.

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Input;

namespace PanneauxGlass;

internal static partial class UI
{
    static void Restyle()
    {
        if (System.Windows.Application.Current == null)
            return;
        var theme = Theme.Current;
        string themeText = Hex(theme.Text), surfaceRgb = Hex(theme.Surface)[1..], accentRgb = Hex(theme.Accent)[1..];
        string overlay = theme.Unified ? Hex(theme.Text)[1..] : theme.Light ? "000000" : "FFFFFF";
        string popRgb = theme.Unified ? Hex(theme.Role(M3Role.SurfaceHigh))[1..] : surfaceRgb,
            outlineRgb = theme.Unified ? Hex(theme.Role(M3Role.OutlineVariant))[1..] : accentRgb;
        bool plated = theme.Opaque;
        string textRgb = Hex(theme.Text)[1..];
        // Les thèmes opaques peignent de vraies plaques ; le verre garde ses blancs translucides. Mélanger vers
        // l'encre donne à chaque matière ses propres tons surélevé, survol et appui au lieu d'un seul gris.
        string raised = plated ? Hex(Theme.Mix(theme.Surface, theme.Text, theme.Light ? .05 : .09)) : A(15, overlay);
        string hover = plated ? Hex(Theme.Mix(theme.Surface, theme.Accent, theme.Light ? .12 : .20)) : A(45, overlay);
        string pressed = plated ? Hex(Theme.Mix(theme.Surface, theme.Accent, theme.Light ? .20 : .28)) : A(70, overlay);
        string line = plated ? A((byte)(theme.Light ? 72 : 58), textRgb) : A(52, overlay);
        string field = plated ? Hex(Theme.Mix(theme.Surface, theme.Text, theme.Light ? .02 : .06)) : A(15, overlay);
        string fieldLine = plated ? A((byte)(theme.Light ? 74 : 60), textRgb) : A(52, overlay);
        string xaml = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
          xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          __BUTTON__
          __FIELD__
          <Style TargetType="ContextMenu"><Setter Property="Background" Value="#F0232D3D"/><Setter Property="Foreground" Value="#F5F8FF"/>
            <Setter Property="BorderBrush" Value="#60D6E6FC"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Padding"
            Value="7"/><Setter Property="HasDropShadow" Value="True"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ContextMenu">
              <Border Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
              BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="12" Padding="{TemplateBinding Padding}">
              <ItemsPresenter KeyboardNavigation.DirectionalNavigation="Cycle"/></Border></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="MenuItem"><Setter Property="Foreground" Value="#F5F8FF"/><Setter Property="Padding" Value="12,0"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="MenuItem"><Border x:Name="hover" CornerRadius="10"
              Background="Transparent" MinHeight="36"><Grid Margin="{TemplateBinding Padding}" VerticalAlignment="Center">
              <Grid.ColumnDefinitions><ColumnDefinition Width="20"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
              <TextBlock x:Name="check" Text="✓" Visibility="Collapsed" VerticalAlignment="Center"/><ContentPresenter Grid.Column="1"
              ContentSource="Header" VerticalAlignment="Center"/></Grid></Border><ControlTemplate.Triggers><Trigger Property="IsHighlighted"
              Value="True"><Setter TargetName="hover" Property="Background" Value="#HOVERLAYER"/></Trigger><Trigger Property="IsPressed"
              Value="True"><Setter TargetName="hover" Property="Background" Value="#PRESSLAYER"/></Trigger><Trigger Property="IsEnabled"
              Value="False"><Setter Property="Opacity" Value="0.38"/></Trigger><Trigger Property="IsChecked" Value="True">
              <Setter TargetName="check" Property="Visibility" Value="Visible"/></Trigger></ControlTemplate.Triggers></ControlTemplate>
              </Setter.Value></Setter>
          </Style>
          <Style TargetType="CheckBox"><Setter Property="Foreground" Value="#E9F0FF"/><Setter Property="FontSize" Value="14"/>
            <Setter Property="Margin" Value="0,8"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CheckBox"><StackPanel Orientation="Horizontal"
              Background="Transparent"><Border x:Name="switch" Width="38" Height="22" CornerRadius="11" Background="#35FFFFFF"
              BorderBrush="#30FFFFFF" BorderThickness="1"><Ellipse x:Name="knob" Width="16" Height="16" Fill="White"
              HorizontalAlignment="Left" Margin="2,0"/></Border><ContentPresenter Margin="12,0,0,0" VerticalAlignment="Center"/>
              </StackPanel><ControlTemplate.Triggers><Trigger Property="IsChecked" Value="True"><Setter TargetName="switch"
              Property="Background" Value="#C173C8DE"/><Setter TargetName="knob" Property="HorizontalAlignment" Value="Right"/></Trigger>
              <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="switch" Property="BorderBrush" Value="White"/>
              </Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="Slider"><Setter Property="MinHeight" Value="28"/><Setter Property="Template"><Setter.Value>
            <ControlTemplate TargetType="Slider"><Grid VerticalAlignment="Center"><Border Height="5" Background="#22FFFFFF"
            CornerRadius="3"/><Track x:Name="PART_Track"><Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge">
            <RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Height="5" Background="#C8DFF4FF" CornerRadius="3"/>
            </ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.IncreaseRepeatButton>
            <RepeatButton Command="Slider.IncreaseLarge"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton">
            <Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
            <Track.Thumb><Thumb Width="15" Height="15"><Thumb.Template><ControlTemplate TargetType="Thumb"><Ellipse Fill="#F4FFFFFF"
            Stroke="#80FFFFFF" StrokeThickness="1"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></Grid>
            </ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="ComboBoxItem"><Setter Property="Foreground" Value="White"/><Setter Property="Padding" Value="10,8"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem"><Border x:Name="item" CornerRadius="10"
            Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsHighlighted"
            Value="True"><Setter TargetName="item" Property="Background" Value="#HOVERLAYER"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="ComboBox"><Setter Property="Foreground" Value="#F5F8FF"/><Setter Property="FontSize" Value="14"/>
            <Setter Property="Padding" Value="10,6"/><Setter Property="MinHeight" Value="38"/><Setter Property="Template"><Setter.Value>
            <ControlTemplate TargetType="ComboBox"><Grid><ToggleButton Focusable="False"
            IsChecked="{Binding IsDropDownOpen,Mode=TwoWay,RelativeSource={RelativeSource TemplatedParent}}"><ToggleButton.Template>
            <ControlTemplate TargetType="ToggleButton"><Border CornerRadius="13" Background="#14FFFFFF" BorderBrush="#38FFFFFF"
            BorderThickness="1"/></ControlTemplate></ToggleButton.Template></ToggleButton><ContentPresenter Margin="13,8,35,8"
            IsHitTestVisible="False" Content="{TemplateBinding SelectionBoxItem}"
            ContentTemplate="{TemplateBinding " +
                "SelectionBoxItemTemplate}"/><TextBlock Text="⌄" Margin="0,0,14,0" HorizontalAlignment="Right"
            VerticalAlignment="Center" IsHitTestVisible="False"/><Popup x:Name="PART_Popup" Placement="Bottom"
            IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Focusable="False"><Border Background="#FA243041"
            BorderBrush="#60D6E6FC" BorderThickness="1" CornerRadius="14" Padding="6" MinWidth="{TemplateBinding ActualWidth}">
            <ScrollViewer MaxHeight="300"><ItemsPresenter/></ScrollViewer></Border></Popup></Grid></ControlTemplate></Setter.Value></Setter>
            </Style>
          <Style TargetType="ScrollViewer"><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollViewer"><Grid>
            <Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
            <ScrollContentPresenter x:Name="PART_ScrollContentPresenter" Content="{TemplateBinding Content}"
            ContentTemplate="{TemplateBinding ContentTemplate}" CanContentScroll="{TemplateBinding CanContentScroll}"
            Margin="{TemplateBinding Padding}"/><ScrollBar x:Name="PART_VerticalScrollBar" Grid.Column="1" Orientation="Vertical" Width="5"
            Margin="3,6,0,6" Minimum="0" Maximum="{TemplateBinding ScrollableHeight}" ViewportSize="{TemplateBinding ViewportHeight}"
            Value="{TemplateBinding VerticalOffset}" Visibility="{TemplateBinding ComputedVerticalScrollBarVisibility}" Opacity="0.6"/>
            </Grid></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="ScrollBar"><Setter Property="Width" Value="8"/><Setter Property="Template"><Setter.Value>
            <ControlTemplate TargetType="ScrollBar"><Track x:Name="PART_Track" IsDirectionReversed="True"><Track.Thumb>
            <Thumb Background="#60FFFFFF"><Thumb.Template><ControlTemplate TargetType="Thumb">
            <Border Background="{TemplateBinding Background}" CornerRadius="4"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
            </Track></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="ToolTip"><Setter Property="Background" Value="#EF263646"/><Setter Property="Foreground" Value="#EEF7FF"/>
            <Setter Property="BorderBrush" Value="#45D6E6FC"/><Setter Property="Padding" Value="12,8"/><Setter Property="Template">
            <Setter.Value><ControlTemplate TargetType="ToolTip"><Border CornerRadius="12" Background="{TemplateBinding Background}"
            BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" Padding="{TemplateBinding " +
                "Padding}"><ContentPresenter/></Border>
            </ControlTemplate></Setter.Value></Setter></Style>
        </ResourceDictionary>
        """;
        xaml = xaml
            .Replace("#F5F8FF", themeText).Replace("#E9F0FF", themeText).Replace("#EEF7FF", themeText)
            .Replace("#20FFFFFF", A(32, overlay)).Replace("#14FFFFFF", A(15, overlay)).Replace("#38FFFFFF", A(52, overlay))
            .Replace("#2DFFFFFF", A(45, overlay)).Replace("#65FFFFFF",
                                                          A(theme.Light ? (byte)70 : (byte)101,
                                                            overlay)).Replace("#46FFFFFF", A(70, overlay)).Replace("#D6EEFF", themeText)
            .Replace("#HOVERLAYER", A(theme.Unified ? HoverLayer : (byte)48, overlay)).Replace(
                "#PRESSLAYER",
                A(theme.Unified ? PressedLayer : (byte)70, overlay))
            .Replace("#6592CDE7", A(140, accentRgb)).Replace("#F0232D3D", "#F4" + popRgb).Replace(
                "#60D6E6FC",
                theme.Unified ? A(255, outlineRgb) : A(96, accentRgb))
            .Replace("#30FFFFFF", A(48, overlay)).Replace("#35FFFFFF", A(53, overlay)).Replace("#C173C8DE", A(200, accentRgb))
            .Replace("#22FFFFFF", A(34, overlay)).Replace("#C8DFF4FF", A(200, accentRgb)).Replace(
                "#F4FFFFFF",
                theme.Unified ? Hex(theme.Accent) : theme.Light ? "#3A4756" : "#F4FFFFFF")
            .Replace("#80FFFFFF", A(128, overlay)).Replace("#FA243041", "#FA" + popRgb).Replace("#60FFFFFF", A(96, overlay))
            .Replace("#EF263646", "#F4" + popRgb).Replace("#45D6E6FC", theme.Unified ? A(255, outlineRgb) : A(69, accentRgb))
            .Replace("CornerRadius=\"16\"", "CornerRadius=\"" + Radius(16) + "\"").Replace("CornerRadius=\"15\"", "CornerRadius=\""
                + Radius(15) + "\"")
            .Replace("CornerRadius=\"14\"", "CornerRadius=\"" + Radius(14) + "\"").Replace("CornerRadius=\"13\"", "CornerRadius=\""
                + Radius(13) + "\"")
            .Replace("CornerRadius=\"12\"", "CornerRadius=\"" + Radius(12) + "\"").Replace("CornerRadius=\"11\"", "CornerRadius=\""
                + Radius(11) + "\"")
            .Replace("CornerRadius=\"10\"", "CornerRadius=\"" + Radius(10) + "\"")
            .Replace("__BUTTON__", ButtonStyle(theme, raised, hover, pressed, line))
            .Replace("__FIELD__", FieldStyle(theme, field, fieldLine));
        var dictionary = (ResourceDictionary)System.Windows.Markup.XamlReader.Parse(xaml);
        if (style != null)
            System.Windows.Application.Current.Resources.MergedDictionaries.Remove(style);
        System.Windows.Application.Current.Resources.MergedDictionaries.Add(dictionary);
        style = dictionary;
    }
    // Un cadre de bouton par matière : le verre flotte, bento et zen sont à plat, suisse trace des filets, rétro biseaute,
    // néon rayonne. La mécanique (clic, survol, focus, désactivé) est identique partout.
    static string ButtonStyle(ThemeDefinition theme, string raised, string hover, string pressed, string line)
    {
        if (theme.Unified)
            return UnifiedButton(theme);
        int radius = Radius(16);
        string text = Hex(theme.Text), accent = Hex(theme.Accent);
        string setters =
            "<Setter Property=\"Foreground\" Value=\"%TEXT%\"/><Setter Property=\"Background\" Value=\"%FACE%\"/><Setter " +
                "Property=\"BorderBrush\" Value=\"%LINE%\"/><Setter Property=\"BorderThickness\" Value=\"1\"/><Setter " +
                "Property=\"Padding\" Value=\"14,9\"/><Setter Property=\"FontSize\" Value=\"13\"/><Setter Property=\"Cursor\" " +
                "Value=\"Hand\"/><Setter Property=\"FocusVisualStyle\" Value=\"{x:Null}\"/>";
        string template = theme.Frame switch
        {
            "bevel" => """
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
                <Grid>
                  <Border x:Name="body" Background="{TemplateBinding Background}"/>
                  <Rectangle x:Name="lightTop" Height="2" VerticalAlignment="Top" Fill="#FFFFFF"/>
                  <Rectangle x:Name="lightLeft" Width="2" HorizontalAlignment="Left" Fill="#FFFFFF"/>
                  <Rectangle x:Name="darkBottom" Height="2" VerticalAlignment="Bottom" Fill="#404040"/>
                  <Rectangle x:Name="darkRight" Width="2" HorizontalAlignment="Right" Fill="#404040"/>
                  <Border BorderBrush="#808080" BorderThickness="1" Margin="2"/>
                  <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                    VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
                </Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="lightTop" Property="Fill" Value="#D8D8D8"/>
                    <Setter TargetName="lightLeft" Property="Fill" Value="#D8D8D8"/></Trigger>
                  <Trigger Property="IsPressed" Value="True"><Setter TargetName="lightTop" Property="Fill" Value="#404040"/>
                    <Setter TargetName="lightLeft" Property="Fill" Value="#404040"/><Setter TargetName="darkBottom" Property="Fill"
                    Value="#FFFFFF"/><Setter TargetName="darkRight" Property="Fill" Value="#FFFFFF"/><Setter TargetName="body"
                    Property="Background" Value="#B0B0B0"/></Trigger>
                  <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
                </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
            """,
            "outline" => """
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
                <Border x:Name="body" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="%RADIUS%">
                  <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                    VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="body" Property="Background" Value="%HOVER%"/>
                    <Setter TargetName="body" Property="BorderBrush" Value="%TEXT%"/></Trigger>
                  <Trigger Property="IsPressed" Value="True"><Setter TargetName="body" Property="Background" Value="%PRESSED%"/></Trigger>
                  <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.35"/></Trigger>
                </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
            """,
            "neon" => """
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
                <Border x:Name="body" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="1.4" CornerRadius="%RADIUS%">
                  <Border.Effect><DropShadowEffect Color="%ACCENT%" BlurRadius="15" ShadowDepth="0" Opacity="0.55"/></Border.Effect>
                  <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                    VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="body" Property="Background" Value="%HOVER%"/>
                    <Setter TargetName="body" Property="BorderBrush" Value="%ACCENT%"/></Trigger>
                  <Trigger Property="IsPressed" Value="True"><Setter TargetName="body" Property="Background" Value="%PRESSED%"/></Trigger>
                  <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.3"/></Trigger>
                </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
            """,
            "flat" => """
              <Setter Property="BorderThickness" Value="0"/>
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
                <Border x:Name="body" Background="{TemplateBinding Background}" CornerRadius="%RADIUS%">
                  <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                    VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="body" Property="Background" Value="%HOVER%"/></Trigger>
                  <Trigger Property="IsPressed" Value="True"><Setter TargetName="body" Property="Background" Value="%PRESSED%"/></Trigger>
                  <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.35"/></Trigger>
                </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
            """,
            _ => """
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
                <Border x:Name="body" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="%RADIUS%">
                  <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                    VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="body" Property="Background" Value="%HOVER%"/>
                    <Setter TargetName="body" Property="BorderBrush" Value="%BORDER%"/></Trigger>
                  <Trigger Property="IsPressed" Value="True"><Setter TargetName="body" Property="Background" Value="%PRESSED%"/></Trigger>
                  <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="body" Property="BorderBrush" Value="%TEXT%"/>
                    </Trigger>
                  <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.30"/></Trigger>
                </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
            """
        };
        if (theme.Frame == "bevel")
            setters = setters.Replace("BorderThickness=\"1\"", "BorderThickness=\"2\"");
        string border = theme.Frame == "neon" ? accent : A(theme.Light ? (byte)80 : (byte)96, Hex(theme.Text)[1..]);
        return ("<Style TargetType=\"Button\">" + setters + template + "</Style>")
            .Replace("%TEXT%", text).Replace("%FACE%", theme.Frame == "bevel" ? Hex(theme.Surface) : raised).Replace("%LINE%", line)
            .Replace("%BORDER%", border).Replace("%ACCENT%", accent).Replace("%RADIUS%", radius.ToString())
            .Replace("%HOVER%", hover).Replace("%PRESSED%", pressed);
    }
    // Boutons Material-3 : tonal rempli par défaut (secondaryContainer), variantes texte et icône via Tag,
    // une couche d'état survol / appui de onSurface par-dessus, coins issus des jetons d'arrondi partagés.
    static string UnifiedButton(ThemeDefinition theme)
    {
        string face = Hex(theme.Role(M3Role.SecondaryContainer)),
            ink = Hex(theme.Role(M3Role.OnSecondaryContainer)),
            primary = Hex(theme.Role(M3Role.Primary)),
            layer = Hex(theme.Text)[1..];
        return """
        <Style TargetType="Button">
          <Setter Property="Foreground" Value="%INK%"/><Setter Property="Background" Value="%FACE%"/><Setter Property="BorderBrush"
            Value="Transparent"/><Setter Property="BorderThickness" Value="0"/>
          <Setter Property="Padding" Value="15,7"/><Setter Property="FontSize" Value="%FONT%"/><Setter Property="Cursor" Value="Hand"/>
            <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
            <Grid>
              <Border x:Name="body" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
                BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="%RADIUS%"/>
              <Border x:Name="state" Background="Transparent" CornerRadius="%RADIUS%" IsHitTestVisible="False"/>
              <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
            </Grid>
            <ControlTemplate.Triggers>
              <Trigger Property="Tag" Value="m3text"><Setter TargetName="body" Property="Background" Value="Transparent"/>
                <Setter Property="Foreground" Value="%PRIMARY%"/></Trigger>
              <Trigger Property="Tag" Value="m3icon"><Setter TargetName="body" Property="Background" Value="Transparent"/>
                <Setter Property="Foreground" Value="%TEXT%"/></Trigger>
              <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="state" Property="Background" Value="%HOVER%"/></Trigger>
              <Trigger Property="IsPressed" Value="True"><Setter TargetName="state" Property="Background" Value="%PRESSED%"/></Trigger>
              <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="body" Property="BorderBrush" Value="%PRIMARY%"/>
                <Setter TargetName="body" Property="BorderThickness" Value="2"/></Trigger>
              <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.38"/></Trigger>
            </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
        </Style>
        """.Replace("%INK%", ink).Replace("%FACE%", face).Replace("%PRIMARY%", primary).Replace("%TEXT%", Hex(theme.Text)).Replace(
            "%FONT%",
            M3.FontSmall.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("%RADIUS%", ((int)M3.Normal).ToString()).Replace("%HOVER%", A(HoverLayer, layer)).Replace("%PRESSED%", A(PressedLayer,
                layer));
    }
    static string UnifiedField(ThemeDefinition theme)
    {
        string text = Hex(theme.Text),
            fill = Hex(theme.Role(M3Role.SurfaceHigh)),
            line = Hex(theme.Role(M3Role.OutlineVariant)),
            primary = Hex(theme.Role(M3Role.Primary));
        return """
        <Style TargetType="TextBox">
          <Setter Property="Foreground" Value="%TEXT%"/><Setter Property="CaretBrush" Value="%TEXT%"/><Setter Property="SelectionBrush"
            Value="%SELECTION%"/>
          <Setter Property="Background" Value="%FIELD%"/><Setter Property="BorderBrush" Value="%LINE%"/><Setter Property="BorderThickness"
            Value="1"/>
          <Setter Property="Padding" Value="12,9"/><Setter Property="FontSize" Value="%FONT%"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox"><Border x:Name="box" CornerRadius="%RADIUS%"
            Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
            BorderThickness="{TemplateBinding BorderThickness}"><ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding " +
                "Padding}"/>
            </Border>
            <ControlTemplate.Triggers><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="box" Property="BorderBrush"
              Value="%PRIMARY%"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
        </Style>
        """.Replace("%TEXT%", text).Replace("%SELECTION%", A(140, primary[1..])).Replace("%FIELD%", fill).Replace("%LINE%",
            line).Replace("%PRIMARY%", primary)
            .Replace("%FONT%", M3.FontBody.ToString(System.Globalization.CultureInfo.InvariantCulture)).Replace("%RADIUS%",
                ((int)M3.Small).ToString());
    }
    // Le champ suit la même matière : plaque en creux sur papier et bento, boîte à filets en suisse,
    // une fente en creux en rétro, un renfoncement sombre en néon et sur verre.
    static string FieldStyle(ThemeDefinition theme, string field, string fieldLine)
    {
        if (theme.Unified)
            return UnifiedField(theme);
        string text = Hex(theme.Text), accent = Hex(theme.Accent);
        int radius = Radius(15);
        string background = theme.Plated ? field : "#14FFFFFF", border = theme.Plated ? fieldLine : "#38FFFFFF";
        return """
        <Style TargetType="TextBox">
          <Setter Property="Foreground" Value="%TEXT%"/><Setter Property="CaretBrush" Value="%TEXT%"/><Setter Property="SelectionBrush"
            Value="%SELECTION%"/>
          <Setter Property="Background" Value="%FIELD%"/><Setter Property="BorderBrush" Value="%LINE%"/><Setter Property="BorderThickness"
            Value="1"/>
          <Setter Property="Padding" Value="13,9"/><Setter Property="FontSize" Value="14"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox"><Border CornerRadius="%RADIUS%"
            Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"
            BorderThickness="{TemplateBinding BorderThickness}"><ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding " +
                "Padding}"/>
            </Border></ControlTemplate></Setter.Value></Setter>
        </Style>
        """.Replace("%TEXT%", text).Replace("%SELECTION%",
                                            A(140, Hex(theme.Accent)[1..])).Replace("%FIELD%", background).Replace("%LINE%",
                                                border).Replace(
                                                "%RADIUS%",
                                                radius.ToString());
    }
}
