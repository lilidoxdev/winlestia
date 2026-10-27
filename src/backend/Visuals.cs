// language: C# 12, file: Visuals.cs, target: Windows 11 / .NET 8 WPF, glass surfaces and motion
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
internal static class UI {
    static readonly Dictionary<string,SolidColorBrush> brushes=new(StringComparer.OrdinalIgnoreCase);
    static ResourceDictionary? style;static bool installed;
    // Mutable on purpose: a theme swap recolours every TextBlock already on screen.
    internal static readonly SolidColorBrush White=new(System.Windows.Media.Color.FromRgb(0xF5,0xF7,0xFC)),Dim=new(System.Windows.Media.Color.FromRgb(0xB9,0xC9,0xDB)),Accent=new(System.Windows.Media.Color.FromRgb(0x9F,0xE4,0xF7));
    internal static readonly FontFamily Display=new("Segoe UI Variable Display"),Serif=new("Georgia"),Mono=new("Consolas"),Poster=new("Arial Black"),Pixel=new("MS Sans Serif");
    internal static FontFamily Font=>Theme.Current.Kind switch{SurfaceKind.Ink=>Mono,SurfaceKind.Retro=>Pixel,_=>Display};
    internal static FontFamily Heading=>Theme.Current.Kind switch{
        SurfaceKind.Poster=>Poster,SurfaceKind.Retro=>Pixel,
        _=>Theme.Current.Heading switch{"serif"=>Serif,"mono"=>Mono,"pixel"=>Pixel,_=>Display}};
    internal static readonly FontFamily Icons=new("Segoe Fluent Icons");
    internal static SolidColorBrush Brush(string hex){
        if(brushes.TryGetValue(hex,out var value))return value;
        value=new SolidColorBrush(Resolve(hex));brushes[hex]=value;return value;
    }
    internal static SolidColorBrush Alpha(Color color,byte alpha){var brush=new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha,color.R,color.G,color.B));brush.Freeze();return brush;}
    // Light themes turn the translucent white plates of the chrome into ink washes, once, everywhere.
    static Color Resolve(string hex){
        var color=(Color)ColorConverter.ConvertFromString(hex)!;
        // On paper, every pale plate and label becomes an ink wash of the same hue.
        double luminance=.2126*color.R+.7152*color.G+.0722*color.B;
        if(Theme.Current.Light&&color.A<255&&luminance>170)return System.Windows.Media.Color.FromArgb(color.A,(byte)(color.R*.18),(byte)(color.G*.18),(byte)(color.B*.18));
        return color;
    }
    internal static void Repaint(){foreach(var pair in brushes)pair.Value.Color=Resolve(pair.Key);}
    internal static void ApplyTheme(){
        var theme=Theme.Current;
        White.Color=theme.Text;Dim.Color=theme.Dim;Accent.Color=theme.Accent;
        Repaint();
        if(installed)Restyle();
        if(System.Windows.Application.Current!=null)foreach(Window window in System.Windows.Application.Current.Windows)ApplyTypography(window);
    }
    // Keeps live windows in the theme's voice: serif titles on paper, mono everywhere in ink.
    internal static void ApplyTypography(DependencyObject root){
        if(root is TextBlock text){if(!(text.FontFamily?.Source.Contains("Fluent Icons",StringComparison.OrdinalIgnoreCase)??false))text.FontFamily=text.FontWeight>=FontWeights.SemiBold&&text.FontSize>=15?Heading:Font;}
        else if(root is TextBox box)box.FontFamily=Font;
        else if(root is Control control&&root is not ScrollViewer)control.FontFamily=Font;
        int count=VisualTreeHelper.GetChildrenCount(root);
        for(int i=0;i<count;i++)ApplyTypography(VisualTreeHelper.GetChild(root,i));
    }
    internal static string Hex(Color color)=>"#"+color.R.ToString("X2")+color.G.ToString("X2")+color.B.ToString("X2");
    static string A(byte alpha,string rgb)=>"#"+alpha.ToString("X2")+rgb;
    static int Radius(int basis)=>Math.Clamp((int)Math.Round(basis*Theme.Current.RadiusScale),2,30);
    internal static void Install(){if(installed)return;installed=true;Restyle();}
    static void Restyle(){
        if(System.Windows.Application.Current==null)return;
        var theme=Theme.Current;string themeText=Hex(theme.Text),surfaceRgb=Hex(theme.Surface)[1..],accentRgb=Hex(theme.Accent)[1..];string overlay=theme.Light?"000000":"FFFFFF";
        bool plated=theme.Opaque;string textRgb=Hex(theme.Text)[1..];
        // Opaque themes paint real plates; glass keeps its translucent whites. Mixing toward the
        // ink gives every material its own raised, hover and pressed tone instead of one grey.
        string raised=plated?Hex(Theme.Mix(theme.Surface,theme.Text,theme.Light?.05:.09)):A(15,overlay);
        string hover=plated?Hex(Theme.Mix(theme.Surface,theme.Accent,theme.Light?.12:.20)):A(45,overlay);
        string pressed=plated?Hex(Theme.Mix(theme.Surface,theme.Accent,theme.Light?.20:.28)):A(70,overlay);
        string line=plated?A((byte)(theme.Light?72:58),textRgb):A(52,overlay);
        string field=plated?Hex(Theme.Mix(theme.Surface,theme.Text,theme.Light?.02:.06)):A(15,overlay);
        string fieldLine=plated?A((byte)(theme.Light?74:60),textRgb):A(52,overlay);
        string xaml="""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          __BUTTON__
          __FIELD__
          <Style TargetType="ContextMenu"><Setter Property="Background" Value="#F0232D3D"/><Setter Property="Foreground" Value="#F5F8FF"/><Setter Property="BorderBrush" Value="#60D6E6FC"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Padding" Value="7"/><Setter Property="HasDropShadow" Value="True"/></Style>
          <Style TargetType="MenuItem"><Setter Property="Foreground" Value="#F5F8FF"/><Setter Property="Padding" Value="12,8"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="MenuItem"><Border x:Name="hover" CornerRadius="10" Background="Transparent"><Grid Margin="{TemplateBinding Padding}"><Grid.ColumnDefinitions><ColumnDefinition Width="20"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions><TextBlock x:Name="check" Text="✓" Visibility="Collapsed"/><ContentPresenter Grid.Column="1" ContentSource="Header"/></Grid></Border><ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="hover" Property="Background" Value="#30FFFFFF"/></Trigger><Trigger Property="IsChecked" Value="True"><Setter TargetName="check" Property="Visibility" Value="Visible"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="CheckBox"><Setter Property="Foreground" Value="#E9F0FF"/><Setter Property="FontSize" Value="14"/><Setter Property="Margin" Value="0,8"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CheckBox"><StackPanel Orientation="Horizontal" Background="Transparent"><Border x:Name="switch" Width="38" Height="22" CornerRadius="11" Background="#35FFFFFF" BorderBrush="#30FFFFFF" BorderThickness="1"><Ellipse x:Name="knob" Width="16" Height="16" Fill="White" HorizontalAlignment="Left" Margin="2,0"/></Border><ContentPresenter Margin="12,0,0,0" VerticalAlignment="Center"/></StackPanel><ControlTemplate.Triggers><Trigger Property="IsChecked" Value="True"><Setter TargetName="switch" Property="Background" Value="#C173C8DE"/><Setter TargetName="knob" Property="HorizontalAlignment" Value="Right"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="switch" Property="BorderBrush" Value="White"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="Slider"><Setter Property="MinHeight" Value="28"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Slider"><Grid VerticalAlignment="Center"><Border Height="5" Background="#22FFFFFF" CornerRadius="3"/><Track x:Name="PART_Track"><Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Height="5" Background="#C8DFF4FF" CornerRadius="3"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton><Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge"><RepeatButton.Template><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton><Track.Thumb><Thumb Width="15" Height="15"><Thumb.Template><ControlTemplate TargetType="Thumb"><Ellipse Fill="#F4FFFFFF" Stroke="#80FFFFFF" StrokeThickness="1"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></Grid></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="ComboBoxItem"><Setter Property="Foreground" Value="White"/><Setter Property="Padding" Value="10,8"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem"><Border x:Name="item" CornerRadius="10" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="item" Property="Background" Value="#30FFFFFF"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="ComboBox"><Setter Property="Foreground" Value="#F5F8FF"/><Setter Property="FontSize" Value="14"/><Setter Property="Padding" Value="10,6"/><Setter Property="MinHeight" Value="38"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox"><Grid><ToggleButton Focusable="False" IsChecked="{Binding IsDropDownOpen,Mode=TwoWay,RelativeSource={RelativeSource TemplatedParent}}"><ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border CornerRadius="13" Background="#14FFFFFF" BorderBrush="#38FFFFFF" BorderThickness="1"/></ControlTemplate></ToggleButton.Template></ToggleButton><ContentPresenter Margin="13,8,35,8" IsHitTestVisible="False" Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"/><TextBlock Text="⌄" Margin="0,0,14,0" HorizontalAlignment="Right" VerticalAlignment="Center" IsHitTestVisible="False"/><Popup x:Name="PART_Popup" Placement="Bottom" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Focusable="False"><Border Background="#FA243041" BorderBrush="#60D6E6FC" BorderThickness="1" CornerRadius="14" Padding="6" MinWidth="{TemplateBinding ActualWidth}"><ScrollViewer MaxHeight="300"><ItemsPresenter/></ScrollViewer></Border></Popup></Grid></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="ScrollViewer"><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollViewer"><Grid><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions><ScrollContentPresenter x:Name="PART_ScrollContentPresenter" Content="{TemplateBinding Content}" ContentTemplate="{TemplateBinding ContentTemplate}" CanContentScroll="{TemplateBinding CanContentScroll}" Margin="{TemplateBinding Padding}"/><ScrollBar x:Name="PART_VerticalScrollBar" Grid.Column="1" Orientation="Vertical" Width="5" Margin="3,6,0,6" Minimum="0" Maximum="{TemplateBinding ScrollableHeight}" ViewportSize="{TemplateBinding ViewportHeight}" Value="{TemplateBinding VerticalOffset}" Visibility="{TemplateBinding ComputedVerticalScrollBarVisibility}" Opacity="0.6"/></Grid></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="ScrollBar"><Setter Property="Width" Value="8"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar"><Track x:Name="PART_Track" IsDirectionReversed="True"><Track.Thumb><Thumb Background="#60FFFFFF"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="{TemplateBinding Background}" CornerRadius="4"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></ControlTemplate></Setter.Value></Setter></Style>
          <Style TargetType="ToolTip"><Setter Property="Background" Value="#EF263646"/><Setter Property="Foreground" Value="#EEF7FF"/><Setter Property="BorderBrush" Value="#45D6E6FC"/><Setter Property="Padding" Value="12,8"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ToolTip"><Border CornerRadius="12" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border></ControlTemplate></Setter.Value></Setter></Style>
        </ResourceDictionary>
        """;
        xaml=xaml
            .Replace("#F5F8FF",themeText).Replace("#E9F0FF",themeText).Replace("#EEF7FF",themeText)
            .Replace("#20FFFFFF",A(32,overlay)).Replace("#14FFFFFF",A(15,overlay)).Replace("#38FFFFFF",A(52,overlay))
            .Replace("#2DFFFFFF",A(45,overlay)).Replace("#65FFFFFF",A(theme.Light?(byte)70:(byte)101,overlay)).Replace("#46FFFFFF",A(70,overlay)).Replace("#D6EEFF",themeText)
            .Replace("#6592CDE7",A(140,accentRgb)).Replace("#F0232D3D","#F4"+surfaceRgb).Replace("#60D6E6FC",A(96,accentRgb))
            .Replace("#30FFFFFF",A(48,overlay)).Replace("#35FFFFFF",A(53,overlay)).Replace("#C173C8DE",A(200,accentRgb))
            .Replace("#22FFFFFF",A(34,overlay)).Replace("#C8DFF4FF",A(200,accentRgb)).Replace("#F4FFFFFF",theme.Light?"#3A4756":"#F4FFFFFF")
            .Replace("#80FFFFFF",A(128,overlay)).Replace("#FA243041","#FA"+surfaceRgb).Replace("#60FFFFFF",A(96,overlay))
            .Replace("#EF263646","#EF"+surfaceRgb).Replace("#45D6E6FC",A(69,accentRgb))
            .Replace("CornerRadius=\"16\"","CornerRadius=\""+Radius(16)+"\"").Replace("CornerRadius=\"15\"","CornerRadius=\""+Radius(15)+"\"")
            .Replace("CornerRadius=\"14\"","CornerRadius=\""+Radius(14)+"\"").Replace("CornerRadius=\"13\"","CornerRadius=\""+Radius(13)+"\"")
            .Replace("CornerRadius=\"12\"","CornerRadius=\""+Radius(12)+"\"").Replace("CornerRadius=\"11\"","CornerRadius=\""+Radius(11)+"\"")
            .Replace("CornerRadius=\"10\"","CornerRadius=\""+Radius(10)+"\"")
            .Replace("__BUTTON__",ButtonStyle(theme,raised,hover,pressed,line))
            .Replace("__FIELD__",FieldStyle(theme,field,fieldLine));
        var dictionary=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse(xaml);
        if(style!=null)System.Windows.Application.Current.Resources.MergedDictionaries.Remove(style);
        System.Windows.Application.Current.Resources.MergedDictionaries.Add(dictionary);style=dictionary;
    }
    // One button frame per material: glass floats, bento and zen sit flat, suisse rules, rétro bevels,
    // néon glows. The mechanics (click, hover, focus, disabled) are identical everywhere.
    static string ButtonStyle(ThemeDefinition theme,string raised,string hover,string pressed,string line){
        int radius=Radius(16);string text=Hex(theme.Text),accent=Hex(theme.Accent);
        string setters="<Setter Property=\"Foreground\" Value=\"%TEXT%\"/><Setter Property=\"Background\" Value=\"%FACE%\"/><Setter Property=\"BorderBrush\" Value=\"%LINE%\"/><Setter Property=\"BorderThickness\" Value=\"1\"/><Setter Property=\"Padding\" Value=\"14,9\"/><Setter Property=\"FontSize\" Value=\"13\"/><Setter Property=\"Cursor\" Value=\"Hand\"/><Setter Property=\"FocusVisualStyle\" Value=\"{x:Null}\"/>";
        string template=theme.Frame switch{
            "bevel"=>"""
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
                <Grid>
                  <Border x:Name="body" Background="{TemplateBinding Background}"/>
                  <Rectangle x:Name="lightTop" Height="2" VerticalAlignment="Top" Fill="#FFFFFF"/>
                  <Rectangle x:Name="lightLeft" Width="2" HorizontalAlignment="Left" Fill="#FFFFFF"/>
                  <Rectangle x:Name="darkBottom" Height="2" VerticalAlignment="Bottom" Fill="#404040"/>
                  <Rectangle x:Name="darkRight" Width="2" HorizontalAlignment="Right" Fill="#404040"/>
                  <Border BorderBrush="#808080" BorderThickness="1" Margin="2"/>
                  <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
                </Grid>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="lightTop" Property="Fill" Value="#D8D8D8"/><Setter TargetName="lightLeft" Property="Fill" Value="#D8D8D8"/></Trigger>
                  <Trigger Property="IsPressed" Value="True"><Setter TargetName="lightTop" Property="Fill" Value="#404040"/><Setter TargetName="lightLeft" Property="Fill" Value="#404040"/><Setter TargetName="darkBottom" Property="Fill" Value="#FFFFFF"/><Setter TargetName="darkRight" Property="Fill" Value="#FFFFFF"/><Setter TargetName="body" Property="Background" Value="#B0B0B0"/></Trigger>
                  <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger>
                </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
            """,
            "outline"=>"""
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
                <Border x:Name="body" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="%RADIUS%">
                  <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="body" Property="Background" Value="%HOVER%"/><Setter TargetName="body" Property="BorderBrush" Value="%TEXT%"/></Trigger>
                  <Trigger Property="IsPressed" Value="True"><Setter TargetName="body" Property="Background" Value="%PRESSED%"/></Trigger>
                  <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.35"/></Trigger>
                </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
            """,
            "neon"=>"""
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
                <Border x:Name="body" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1.4" CornerRadius="%RADIUS%">
                  <Border.Effect><DropShadowEffect Color="%ACCENT%" BlurRadius="15" ShadowDepth="0" Opacity="0.55"/></Border.Effect>
                  <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="body" Property="Background" Value="%HOVER%"/><Setter TargetName="body" Property="BorderBrush" Value="%ACCENT%"/></Trigger>
                  <Trigger Property="IsPressed" Value="True"><Setter TargetName="body" Property="Background" Value="%PRESSED%"/></Trigger>
                  <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.3"/></Trigger>
                </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
            """,
            "flat"=>"""
              <Setter Property="BorderThickness" Value="0"/>
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
                <Border x:Name="body" Background="{TemplateBinding Background}" CornerRadius="%RADIUS%">
                  <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="body" Property="Background" Value="%HOVER%"/></Trigger>
                  <Trigger Property="IsPressed" Value="True"><Setter TargetName="body" Property="Background" Value="%PRESSED%"/></Trigger>
                  <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.35"/></Trigger>
                </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
            """,
            _=>"""
              <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
                <Border x:Name="body" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="%RADIUS%">
                  <ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="{TemplateBinding VerticalContentAlignment}"/>
                </Border>
                <ControlTemplate.Triggers>
                  <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="body" Property="Background" Value="%HOVER%"/><Setter TargetName="body" Property="BorderBrush" Value="%BORDER%"/></Trigger>
                  <Trigger Property="IsPressed" Value="True"><Setter TargetName="body" Property="Background" Value="%PRESSED%"/></Trigger>
                  <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="body" Property="BorderBrush" Value="%TEXT%"/></Trigger>
                  <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.30"/></Trigger>
                </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
            """
        };
        if(theme.Frame=="bevel")setters=setters.Replace("BorderThickness=\"1\"","BorderThickness=\"2\"");
        string border=theme.Frame=="neon"?accent:A(theme.Light?(byte)80:(byte)96,Hex(theme.Text)[1..]);
        return ("<Style TargetType=\"Button\">"+setters+template+"</Style>")
            .Replace("%TEXT%",text).Replace("%FACE%",theme.Frame=="bevel"?Hex(theme.Surface):raised).Replace("%LINE%",line)
            .Replace("%BORDER%",border).Replace("%ACCENT%",accent).Replace("%RADIUS%",radius.ToString())
            .Replace("%HOVER%",hover).Replace("%PRESSED%",pressed);
    }
    // The field follows the same material: an inset plate on paper and bento, a ruled box in suisse,
    // a sunken slot in rétro, a dark recess in néon and on glass.
    static string FieldStyle(ThemeDefinition theme,string field,string fieldLine){
        string text=Hex(theme.Text),accent=Hex(theme.Accent);int radius=Radius(15);
        string background=theme.Plated?field:"#14FFFFFF",border=theme.Plated?fieldLine:"#38FFFFFF";
        return """
        <Style TargetType="TextBox">
          <Setter Property="Foreground" Value="%TEXT%"/><Setter Property="CaretBrush" Value="%TEXT%"/><Setter Property="SelectionBrush" Value="%SELECTION%"/>
          <Setter Property="Background" Value="%FIELD%"/><Setter Property="BorderBrush" Value="%LINE%"/><Setter Property="BorderThickness" Value="1"/>
          <Setter Property="Padding" Value="13,9"/><Setter Property="FontSize" Value="14"/>
          <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox"><Border CornerRadius="%RADIUS%" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}"><ScrollViewer x:Name="PART_ContentHost" Margin="{TemplateBinding Padding}"/></Border></ControlTemplate></Setter.Value></Setter>
        </Style>
        """.Replace("%TEXT%",text).Replace("%SELECTION%",A(140,Hex(theme.Accent)[1..])).Replace("%FIELD%",background).Replace("%LINE%",border).Replace("%RADIUS%",radius.ToString());
    }
    internal static TextBlock Text(string value,double size=14,Brush? color=null,bool bold=false)=>new(){Text=value,FontSize=size,Foreground=color??White,FontFamily=bold&&size>=15?Heading:Font,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal};
    internal static TextBlock Glyph(string code,double size=18)=>new(){Text=code,FontFamily=Icons,FontSize=size,Foreground=White,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
    internal static Button Button(string label,Action action){var b=new Button{Content=label,HorizontalContentAlignment=HorizontalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center};b.Click+=(_,_)=>action();Motion.BindButton(b);return b;}
    internal static Button IconButton(string glyph,string name,Action action){var b=Button("",action);b.Content=Glyph(glyph);b.Width=38;b.Height=38;b.Padding=new Thickness(0);b.ToolTip=name;System.Windows.Automation.AutomationProperties.SetName(b,name);return b;}
    internal static MenuItem Menu(string text,Action action){var item=new MenuItem{Header=text};item.Click+=(_,_)=>action();return item;}
    internal static Color Color(string hex)=>((SolidColorBrush)Brush(hex)).Color;
    internal static void SetIcon(Image image,ImageSource source){image.Source=source;image.Effect=null;if(!Theme.Current.Light||source is not System.Windows.Media.Imaging.BitmapSource bitmap)return;try{var converted=new System.Windows.Media.Imaging.FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0);int stride=converted.PixelWidth*4;var bytes=new byte[stride*converted.PixelHeight];converted.CopyPixels(bytes,stride,0);double light=0;int visible=0;for(int y=0;y<converted.PixelHeight;y+=6)for(int x=0;x<converted.PixelWidth;x+=6){int index=y*stride+x*4;if(bytes[index+3]<170)continue;light+=(bytes[index]+bytes[index+1]+bytes[index+2])/3d;visible++;}if(visible>0&&light/visible>224){var shadow=new DropShadowEffect{Color=Theme.Current.Text,BlurRadius=3,ShadowDepth=0,Opacity=.8};shadow.Freeze();image.Effect=shadow;}}catch{}}
    internal static void Animate(UIElement element,int direction=1){if(element is FrameworkElement framework)Motion.Reveal(framework);}

}
internal class GlassWindow:Window {
    // Off-screen rendering for the checks: every window opens far outside the virtual desktop,
    // so a theme can be inspected as a PNG without a single panel appearing on the desk.
    internal static Vector CaptureOffset;
    static bool Offscreen=>CaptureOffset.LengthSquared>0;
    internal readonly Grid Surface=new();internal readonly Grid ContentHost=new();readonly Border tintLayer=new();readonly Border rim=new(),accentRule=new(),glowFrame=new();readonly Grid reflections=new(),ambient=new();
    readonly Rectangle ambientA=new(),ambientB=new(),signature=new(),reflectionSheet=new();readonly LinearGradientBrush rimBrush=new();
    (double tint,bool light,bool opaque)? material;(double width,double height,double radius) geometry;readonly TranslateTransform motion=new();
    IntPtr nativeHandle;int motionVersion,themeVersion=-1;readonly TranslateTransform drawerSlide=new();readonly System.Windows.Shapes.Path drawerOutline=new(){IsHitTestVisible=false,StrokeThickness=1};
    internal bool TopAttached;
    internal void AttachToTop(){TopAttached=true;AllowsTransparency=true;Surface.RenderTransform=drawerSlide;Surface.Children.Add(drawerOutline);}
    internal int Radius=32;internal int VisualRadius=>Math.Max(4,(int)Math.Round(Radius*Theme.Current.RadiusScale));internal double TargetLeft,TargetTop;internal bool KeepOpen;internal bool MaterialVisible=true;internal Func<KeyEventArgs,bool>? Shortcut;
    protected override void OnPreviewKeyDown(KeyEventArgs e){if(Shortcut!=null&&Shortcut(e)){e.Handled=true;return;}base.OnPreviewKeyDown(e);}
    internal GlassWindow(string title){Title=title;WindowStyle=WindowStyle.None;AllowsTransparency=Native.Layered&&!Theme.Current.Opaque;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;Topmost=true;Background=Brushes.Transparent;FontFamily=UI.Font;Foreground=UI.White;UseLayoutRounding=true;SnapsToDevicePixels=true;
        Surface.ClipToBounds=true;Content=Surface;
        tintLayer.CornerRadius=new CornerRadius(Radius);Surface.Children.Add(tintLayer);
        // Néon lights its own frame: a blurred inner rim, clipped by the window like the tint.
        glowFrame.CornerRadius=new CornerRadius(Radius);glowFrame.BorderThickness=new Thickness(2);glowFrame.IsHitTestVisible=false;
        glowFrame.Effect=new BlurEffect{Radius=11};glowFrame.Visibility=Visibility.Collapsed;Surface.Children.Add(glowFrame);
        ambient.IsHitTestVisible=false;ambient.Opacity=.8;ambientA.IsHitTestVisible=ambientB.IsHitTestVisible=signature.IsHitTestVisible=false;ambient.Children.Add(ambientA);ambient.Children.Add(ambientB);ambient.Children.Add(signature);Surface.Children.Add(ambient);
        reflections.IsHitTestVisible=false;reflectionSheet.IsHitTestVisible=false;reflections.Children.Add(reflectionSheet);Surface.Children.Add(reflections);
        rim.CornerRadius=new CornerRadius(Radius);rim.BorderBrush=rimBrush;rim.BorderThickness=new Thickness(1);rim.IsHitTestVisible=false;Surface.Children.Add(rim);
        accentRule.HorizontalAlignment=HorizontalAlignment.Stretch;accentRule.VerticalAlignment=VerticalAlignment.Top;accentRule.IsHitTestVisible=false;accentRule.CornerRadius=new CornerRadius(2);
        BuildThemeLayers();
        Surface.Children.Add(ContentHost);Surface.Children.Add(accentRule);
        ContentHost.RenderTransform=motion;ContentHost.CacheMode=new BitmapCache{EnableClearType=true,SnapsToDevicePixels=true};
        SourceInitialized+=(_,_)=>{nativeHandle=new WindowInteropHelper(this).Handle;ApplyGlass();if(TopAttached)Native.ClearRegion(this);};SizeChanged+=(_,_)=>UpdateCorners();LocationChanged+=(_,_)=>{if(nativeHandle!=IntPtr.Zero)Native.SyncBackdrop(this,VisualRadius);};IsVisibleChanged+=(_,_)=>{if(nativeHandle!=IntPtr.Zero)Native.SyncBackdrop(this,VisualRadius);};Closed+=(_,_)=>{if(AllowsTransparency&&nativeHandle!=IntPtr.Zero)Native.BackdropDetach(nativeHandle);};DpiChanged+=(_,_)=>{geometry=default;UpdateCorners();};
    }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e){base.OnPropertyChanged(e);if(e.Property==TopmostProperty&&nativeHandle!=IntPtr.Zero)Native.SyncBackdrop(this,VisualRadius);}
    void UpdateCorners(){var next=(ActualWidth,ActualHeight,(double)VisualRadius);if(geometry==next)return;geometry=next;if(TopAttached){var shape=DrawerShape.Create(Math.Max(1,ActualWidth),Math.Max(1,ActualHeight));Surface.Clip=shape;drawerOutline.Data=shape;drawerOutline.Stroke=UI.Alpha(Theme.Current.Dim,95);rim.Visibility=Visibility.Collapsed;return;}double radius=Math.Min(VisualRadius,Math.Min(ActualWidth,ActualHeight)/2);if(radius<0)return;tintLayer.CornerRadius=rim.CornerRadius=glowFrame.CornerRadius=new CornerRadius(radius);Surface.Clip=new RectangleGeometry(new Rect(0,0,Math.Max(0,ActualWidth),Math.Max(0,ActualHeight)),radius,radius);Native.Round(this,(int)radius);}
    // Everything coloured on the glass is rebuilt from the active theme, so a swap reaches old windows too.
    void BuildThemeLayers(){
        var theme=Theme.Current;themeVersion=Theme.Version;
        ambientA.Fill=new RadialGradientBrush{Center=new Point(.03,.01),GradientOrigin=new Point(.03,.01),RadiusX=.92,RadiusY=1.1,GradientStops=new(){new(theme.Halo(36,0),0),new(Colors.Transparent,1)}};
        ambientB.Fill=new RadialGradientBrush{Center=new Point(.98,.98),GradientOrigin=new Point(.98,.98),RadiusX=.95,RadiusY=1,GradientStops=new(){new(theme.Halo(24,1),0),new(Colors.Transparent,1)}};
        signature.Fill=SignatureBrush(theme);signature.Visibility=signature.Fill==null?Visibility.Collapsed:Visibility.Visible;
        reflectionSheet.Fill=new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(.82,1),GradientStops=new(){
            new(colorWith(Color.FromArgb(27,255,255,255),theme.ReflectionGain),0),new(colorWith(Color.FromArgb(7,255,255,255),theme.ReflectionGain),.35),new(Colors.Transparent,.53),
            new(Color.FromArgb((byte)Math.Clamp(12*theme.ReflectionGain,0,255),theme.Reflection.R,theme.Reflection.G,theme.Reflection.B),.70),new(Colors.Transparent,1)}};
        rimBrush.StartPoint=new Point(0,0);rimBrush.EndPoint=new Point(1,1);rimBrush.GradientStops.Clear();
        rimBrush.GradientStops.Add(theme.Light?new(Color.FromArgb(170,theme.RimB.R,theme.RimB.G,theme.RimB.B),0):new(Color.FromArgb(174,255,255,255),0));
        rimBrush.GradientStops.Add(new(Color.FromArgb((byte)(50*theme.ReflectionGain),theme.RimA.R,theme.RimA.G,theme.RimA.B),.3));
        rimBrush.GradientStops.Add(new(Color.FromArgb(23,255,255,255),.50));
        rimBrush.GradientStops.Add(new(Color.FromArgb(105,theme.RimB.R,theme.RimB.G,theme.RimB.B),1));
        accentRule.Height=theme.Kind switch{SurfaceKind.Poster=>4,SurfaceKind.Retro=>3,SurfaceKind.Bento=>5,SurfaceKind.Swiss=>2,SurfaceKind.Neon=>2,_=>2};
        accentRule.Visibility=theme.Kind is SurfaceKind.Paper or SurfaceKind.Ink or SurfaceKind.Poster or SurfaceKind.Retro or SurfaceKind.Bento or SurfaceKind.Swiss or SurfaceKind.Neon?Visibility.Visible:Visibility.Collapsed;
        accentRule.Background=theme.Kind switch{
            SurfaceKind.Poster=>new LinearGradientBrush(new GradientStopCollection{new(theme.HaloA,0),new(theme.HaloB,.55),new(theme.HaloA,1)},new Point(0,0),new Point(1,0)),
            SurfaceKind.Ink=>new LinearGradientBrush(Color.FromArgb(190,theme.Accent.R,theme.Accent.G,theme.Accent.B),Colors.Transparent,new Point(0,0),new Point(1,0)),
            SurfaceKind.Paper=>new SolidColorBrush(Color.FromArgb(90,theme.Accent.R,theme.Accent.G,theme.Accent.B)),
            SurfaceKind.Retro=>new LinearGradientBrush(Color.FromArgb(255,theme.Accent.R,theme.Accent.G,theme.Accent.B),Color.FromArgb(160,theme.Accent.R,theme.Accent.G,theme.Accent.B),new Point(0,0),new Point(1,0)),
            SurfaceKind.Bento=>new LinearGradientBrush(new GradientStopCollection{new(theme.HaloA,0),new(theme.HaloB,.62),new(theme.HaloA,1)},new Point(0,0),new Point(1,0)),
            SurfaceKind.Swiss=>new SolidColorBrush(theme.Accent),
            SurfaceKind.Neon=>new LinearGradientBrush(new GradientStopCollection{new(theme.HaloA,0),new(theme.HaloB,.5),new(theme.HaloA,1)},new Point(0,0),new Point(1,0)),
            _=>Brushes.Transparent};
        glowFrame.BorderBrush=new SolidColorBrush(Color.FromArgb(200,theme.Accent.R,theme.Accent.G,theme.Accent.B));
        glowFrame.Visibility=theme.Kind==SurfaceKind.Neon?Visibility.Visible:Visibility.Collapsed;
        geometry=default;material=null;UpdateCorners();
    }
    // Each theme carries one signature shape so two themes never read as the same glass recoloured.
    static Brush? SignatureBrush(ThemeDefinition theme)=>theme.Signature switch{
        "band"=>new LinearGradientBrush{StartPoint=new Point(0,0),EndPoint=new Point(1,1),GradientStops=new(){
            new(Colors.Transparent,.30),new(theme.Halo(52,1),.47),new(Color.FromArgb((byte)Math.Clamp(18*theme.HaloGain,0,255),theme.HaloA.R,theme.HaloA.G,theme.HaloA.B),.56),new(Colors.Transparent,.70)}},
        "sun"=>new RadialGradientBrush{Center=new Point(.88,.98),GradientOrigin=new Point(.88,.98),RadiusX=.75,RadiusY=.95,GradientStops=new(){new(theme.Halo(78,1),0),new(theme.Halo(28,0),.55),new(Colors.Transparent,1)}},
        "petals"=>Petals(theme),
        "grid"=>Grid(theme),
        "paper"=>Paper(theme),
        "scan"=>Scan(theme),
        "poster"=>Poster(theme),
        "mist"=>Mist(theme),
        "bento"=>Bento(theme),
        "neon"=>Neon(theme),
        "retro"=>Retro(theme),
        "swiss"=>Swiss(theme),
        "zen"=>Zen(theme),
        _=>null
    };
    // Grain fibres for the paper theme: a tiled sheet, never a stretched image.
    static Brush Paper(ThemeDefinition theme){
        var group=new DrawingGroup();var fibre=new Pen(new SolidColorBrush(Color.FromArgb(22,92,72,50)),.7);
        for(int i=0;i<26;i++){double x=i*37%120+4,y=i*53%120+4;group.Children.Add(new GeometryDrawing(null,fibre,new LineGeometry(new Point(x,y),new Point(x+18+i%5*6,y+2))));}
        var speck=new SolidColorBrush(Color.FromArgb(12,70,55,35));
        for(int i=0;i<44;i++){double x=i*29%120,y=i*41%120;group.Children.Add(new GeometryDrawing(speck,null,new EllipseGeometry(new Point(x,y),.8,.8)));}
        return new DrawingBrush(group){TileMode=TileMode.Tile,Viewbox=new Rect(0,0,120,120),ViewboxUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,120,120),ViewportUnits=BrushMappingMode.Absolute,Stretch=Stretch.Fill};
    }
    // Phosphor scanlines, one pixel of light every four.
    static Brush Scan(ThemeDefinition theme){
        var group=new DrawingGroup();var line=new SolidColorBrush(Color.FromArgb(16,theme.HaloA.R,theme.HaloA.G,theme.HaloA.B));
        group.Children.Add(new GeometryDrawing(line,null,new RectangleGeometry(new Rect(0,0,4,1))));
        return new DrawingBrush(group){TileMode=TileMode.Tile,Viewbox=new Rect(0,0,4,4),ViewboxUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,4,4),ViewportUnits=BrushMappingMode.Absolute,Stretch=Stretch.Fill};
    }
    // A printed poster: an arc, a diagonal band and a halftone field.
    static Brush Poster(ThemeDefinition theme){
        // Absolute coordinates: the motif keeps its size at any window width instead of stretching.
        var group=new DrawingGroup();var pen=new Pen(new SolidColorBrush(Color.FromArgb(18,theme.HaloB.R,theme.HaloB.G,theme.HaloB.B)),2);
        group.Children.Add(new GeometryDrawing(null,pen,new EllipseGeometry(new Point(1050,150),150,150)));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(10,theme.HaloA.R,theme.HaloA.G,theme.HaloA.B)),null,new RectangleGeometry(new Rect(0,520,780,54),2,2)));
        var dot=new SolidColorBrush(Color.FromArgb(22,theme.HaloA.R,theme.HaloA.G,theme.HaloA.B));
        for(int row=0;row<5;row++)for(int column=0;column<10;column++)group.Children.Add(new GeometryDrawing(dot,null,new EllipseGeometry(new Point(80+column*38,620+row*22),2.4,2.4)));
        return new DrawingBrush(group){TileMode=TileMode.None,Viewbox=new Rect(0,0,1400,900),ViewboxUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,1400,900),ViewportUnits=BrushMappingMode.Absolute,Stretch=Stretch.None};
    }
    // Soft daylight: nothing but two blurred blooms and a faint grain.
    static Brush Mist(ThemeDefinition theme){
        var group=new DrawingGroup();
        foreach(var (x,y,r) in new[]{(0.12,0.08,0.55),(0.9,0.86,0.6)}){
            var brush=new RadialGradientBrush(new GradientStopCollection{new(theme.Halo(46,0),0),new(theme.Halo(20,1),.62),new(Colors.Transparent,1)}){Center=new Point(x,y),GradientOrigin=new Point(x,y),RadiusX=r,RadiusY=r};
            group.Children.Add(new GeometryDrawing(brush,null,new RectangleGeometry(new Rect(0,0,1,1))));
        }
        return new DrawingBrush(group){Stretch=Stretch.Fill};
    }
    static Brush Petals(ThemeDefinition theme){
        var group=new DrawingGroup();
        foreach(var (x,y,r) in new[]{(0.16,0.78,0.26),(0.84,0.18,0.22),(0.66,0.92,0.16),(0.34,0.12,0.12)}){
            group.Children.Add(new GeometryDrawing(new RadialGradientBrush{GradientStops=new(){new(theme.Halo(40,0),0),new(Colors.Transparent,1)}},null,new EllipseGeometry(new Point(x,y),r,r*.7)));
        }
        return new DrawingBrush(group);
    }
    static Brush Grid(ThemeDefinition theme){
        var group=new DrawingGroup();var pen=new Pen(new SolidColorBrush(Color.FromArgb((byte)Math.Clamp(14*theme.HaloGain,0,255),255,255,255)),1);
        group.Children.Add(new GeometryDrawing(null,pen,new LineGeometry(new Point(0,0),new Point(28,0))));
        group.Children.Add(new GeometryDrawing(null,pen,new LineGeometry(new Point(0,0),new Point(0,28))));
        return new DrawingBrush(group){TileMode=TileMode.Tile,Viewport=new Rect(0,0,28,28),ViewportUnits=BrushMappingMode.Absolute,Stretch=Stretch.None};
    }
    // Bento: a faint tray grid with one offset solid cell, so the surface reads as compartments.
    static Brush Bento(ThemeDefinition theme){
        var group=new DrawingGroup();var pen=new Pen(new SolidColorBrush(Color.FromArgb(9,theme.Text.R,theme.Text.G,theme.Text.B)),1);
        group.Children.Add(new GeometryDrawing(null,pen,new LineGeometry(new Point(0,0),new Point(240,0))));
        group.Children.Add(new GeometryDrawing(null,pen,new LineGeometry(new Point(0,0),new Point(0,240))));
        var dot=new SolidColorBrush(Color.FromArgb(30,theme.HaloA.R,theme.HaloA.G,theme.HaloA.B));
        group.Children.Add(new GeometryDrawing(dot,null,new RectangleGeometry(new Rect(0,0,2,2))));
        return new DrawingBrush(group){TileMode=TileMode.Tile,Viewbox=new Rect(0,0,240,240),ViewboxUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,240,240),ViewportUnits=BrushMappingMode.Absolute,Stretch=Stretch.None};
    }
    // Néon: two tubes bleeding colour into a dark room, one from each corner.
    static Brush Neon(ThemeDefinition theme){
        var group=new DrawingGroup();
        foreach(var (x,y,r,which) in new[]{(0.02,0.98,0.85,0),(0.98,0.02,0.8,1)}){
            group.Children.Add(new GeometryDrawing(new RadialGradientBrush{GradientStops=new(){new(theme.Halo(58,which),0),new(theme.Halo(20,which),.5),new(Colors.Transparent,1)}},null,new RectangleGeometry(new Rect(0,0,1,1))));
        }
        return new DrawingBrush(group){Stretch=Stretch.Fill};
    }
    // Rétro: the classic two-pixel desktop dither.
    static Brush Retro(ThemeDefinition theme){
        var group=new DrawingGroup();var dot=new SolidColorBrush(Color.FromArgb(18,0,0,0));
        group.Children.Add(new GeometryDrawing(dot,null,new RectangleGeometry(new Rect(0,0,1,1))));
        group.Children.Add(new GeometryDrawing(dot,null,new RectangleGeometry(new Rect(2,2,1,1))));
        return new DrawingBrush(group){TileMode=TileMode.Tile,Viewbox=new Rect(0,0,4,4),ViewboxUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,4,4),ViewportUnits=BrushMappingMode.Absolute,Stretch=Stretch.Fill};
    }
    // Suisse: a single hairline grid, almost invisible, plus one red rule at the top.
    static Brush Swiss(ThemeDefinition theme){
        var group=new DrawingGroup();var pen=new Pen(new SolidColorBrush(Color.FromArgb(9,0,0,0)),1);
        group.Children.Add(new GeometryDrawing(null,pen,new LineGeometry(new Point(0,0),new Point(0,1000))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromArgb(255,theme.Accent.R,theme.Accent.G,theme.Accent.B)),null,new RectangleGeometry(new Rect(0,0,52,3))));
        return new DrawingBrush(group){TileMode=TileMode.None,Viewbox=new Rect(0,0,1000,1000),ViewboxUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,1000,1000),ViewportUnits=BrushMappingMode.Absolute,Stretch=Stretch.None};
    }
    // Zen: raked sand, horizontal grooves with a slow drift.
    static Brush Zen(ThemeDefinition theme){
        var group=new DrawingGroup();var pen=new Pen(new SolidColorBrush(Color.FromArgb(14,92,84,64)),1);
        for(int i=0;i<9;i++)group.Children.Add(new GeometryDrawing(null,pen,new LineGeometry(new Point(0,4+i*9),new Point(1000,4+i*9))));
        return new DrawingBrush(group){TileMode=TileMode.Tile,Viewbox=new Rect(0,0,1000,90),ViewboxUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,1000,90),ViewportUnits=BrushMappingMode.Absolute,Stretch=Stretch.Fill};
    }
    static Color colorWith(Color color,double gain)=>Color.FromArgb((byte)Math.Clamp(color.A*gain,0,255),color.R,color.G,color.B);
    internal void RefreshTheme(){themeVersion=-1;ApplyGlass();}
    // A theme swap breathes instead of snapping: the surface dips and comes back in the new colours.
    internal void FlashTheme(){
        if(!Motion.On||!IsVisible)return;
        ContentHost.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(.72,1,Motion.Span(230)){EasingFunction=Motion.Ease});
        var glow=new DoubleAnimation(.55,1,Motion.Span(320)){EasingFunction=Motion.Ease};
        ambient.BeginAnimation(UIElement.OpacityProperty,glow);
    }
    internal void ApplyGlass(){
        if(themeVersion!=Theme.Version)BuildThemeLayers();
        tintLayer.Visibility=ambient.Visibility=reflections.Visibility=rim.Visibility=MaterialVisible?Visibility.Visible:Visibility.Collapsed;
        var theme=Theme.Current;
        if(TopAttached)rim.Visibility=Visibility.Collapsed;
        var surface=Store.State.LightGlass&&!theme.Opaque?Color.FromRgb(0x2B,0x3D,0x52):theme.Surface;
        byte alpha=theme.Opaque?(byte)255:(byte)Math.Clamp((.58+Store.State.Tint*.4)*255,0,235);
        tintLayer.Background=new SolidColorBrush(Color.FromArgb(alpha,surface.R,surface.G,surface.B));
        reflections.Opacity=Math.Clamp(Store.State.Reflections*theme.ReflectionGain,0,1);rim.Opacity=.55+Store.State.Reflections*.45;
        var next=(Store.State.Tint,Theme.Current.Light||Store.State.LightGlass,Theme.Current.Opaque);if(material!=next){Native.Glass(this,next.Item1,next.Item2,VisualRadius);material=next;}UpdateCorners();}
    void CancelMotion(){motionVersion++;double currentHeight=Height;BeginAnimation(HeightProperty,null);if(double.IsFinite(currentHeight))Height=currentHeight;drawerSlide.BeginAnimation(TranslateTransform.YProperty,null);drawerSlide.Y=0;Surface.BeginAnimation(UIElement.OpacityProperty,null);Surface.Opacity=1;double x=Left,y=Top;BeginAnimation(LeftProperty,null);BeginAnimation(TopProperty,null);if(double.IsFinite(x))Left=x;if(double.IsFinite(y))Top=y;motion.BeginAnimation(TranslateTransform.XProperty,null);ContentHost.BeginAnimation(UIElement.OpacityProperty,null);motion.X=0;ContentHost.Opacity=1;ContentHost.Effect=null;}
    static DoubleAnimation Tween(double from,double to,double milliseconds)=>new(from,to,Motion.Span(milliseconds*Store.State.Motion)){EasingFunction=Motion.Ease,FillBehavior=FillBehavior.Stop};
    internal void TransitionTo(Rect rect,int index,bool animate){if(TopAttached&&IsVisible&&animate&&Motion.On&&Math.Abs(Height-rect.Height)>.5){double from=Height;CancelMotion();Left=TargetLeft=rect.X;Top=TargetTop=rect.Y;Width=rect.Width;Height=rect.Height;BeginAnimation(HeightProperty,Tween(from,rect.Height,250),HandoffBehavior.SnapshotAndReplace);ApplyGlass();return;}if(Offscreen)rect.Offset(CaptureOffset.X,CaptureOffset.Y);if(!IsVisible||Math.Abs(Width-rect.Width)>.5||Math.Abs(Height-rect.Height)>.5){MoveInto(rect.X,rect.Y,rect.Width,rect.Height,index,animate);return;}if(Math.Abs(TargetLeft-rect.X)<.5&&Math.Abs(TargetTop-rect.Y)<.5){ApplyGlass();return;}double x=Left,y=Top;CancelMotion();TargetLeft=rect.X;TargetTop=rect.Y;Left=rect.X;Top=rect.Y;int version=motionVersion;
        if(animate&&Motion.On){void Axis(DependencyProperty property,double from,double to){if(Math.Abs(from-to)<.5)return;var slide=Tween(from,to,175);slide.Completed+=(_,_)=>{if(version==motionVersion)BeginAnimation(property,null);};BeginAnimation(property,slide,HandoffBehavior.SnapshotAndReplace);}Axis(LeftProperty,x,rect.X);Axis(TopProperty,y,rect.Y);}ApplyGlass();}
    internal void HideImmediately(){CancelMotion();if(IsVisible)Hide();}
    internal void MoveInto(double x,double y,double width,double height,int index,bool animate){if(Offscreen){x+=CaptureOffset.X;y+=CaptureOffset.Y;}bool opening=!IsVisible;bool unchanged=Math.Abs(Left-x)<.5&&Math.Abs(Top-y)<.5&&Math.Abs(Width-width)<.5&&Math.Abs(Height-height)<.5;if(!opening&&unchanged){ApplyGlass();return;}CancelMotion();TargetLeft=x;TargetTop=y;Width=width;Height=height;Top=y;Left=x;// A window already closing (app shutdown, space collapsing) must not be shown again.
        if(opening){try{Show();}catch(InvalidOperationException){return;}}ApplyGlass();
        if(opening&&animate&&Motion.On&&TopAttached){drawerSlide.BeginAnimation(TranslateTransform.YProperty,Tween(-height,0,300),HandoffBehavior.SnapshotAndReplace);Surface.BeginAnimation(UIElement.OpacityProperty,Tween(.25,1,190),HandoffBehavior.SnapshotAndReplace);return;}if(opening&&animate&&Motion.On){int version=motionVersion;var slide=Tween(14,0,160);slide.Completed+=(_,_)=>{if(version!=motionVersion)return;motion.BeginAnimation(TranslateTransform.XProperty,null);ContentHost.BeginAnimation(UIElement.OpacityProperty,null);};motion.BeginAnimation(TranslateTransform.XProperty,slide,HandoffBehavior.SnapshotAndReplace);ContentHost.BeginAnimation(UIElement.OpacityProperty,Tween(.65,1,160),HandoffBehavior.SnapshotAndReplace);Motion.BlurReveal(ContentHost);}
    }
    internal void SlideOut(int index,Action completed){if(!IsVisible){completed();return;}if(TopAttached){double from=drawerSlide.Y;CancelMotion();if(!Motion.On){Hide();completed();return;}int expected=motionVersion;var animation=Tween(from,-ActualHeight,190);animation.Completed+=(_,_)=>{if(expected==motionVersion){HideImmediately();completed();}};drawerSlide.BeginAnimation(TranslateTransform.YProperty,animation,HandoffBehavior.SnapshotAndReplace);return;}double current=motion.X,opacity=ContentHost.Opacity;CancelMotion();if(!Motion.On){Hide();completed();return;}int version=motionVersion;var slide=Tween(current,12,100);slide.Completed+=(_,_)=>{if(version!=motionVersion)return;HideImmediately();completed();};motion.BeginAnimation(TranslateTransform.XProperty,slide,HandoffBehavior.SnapshotAndReplace);ContentHost.BeginAnimation(UIElement.OpacityProperty,Tween(opacity,.3,100),HandoffBehavior.SnapshotAndReplace);}
}
