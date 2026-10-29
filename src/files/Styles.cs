// Templates of the details list (rows, scrollbar, list shell). Brushes reach the templates through LiveBrushes (a Binding), never through DynamicResource or a Setter value: WPF freezes those, and a frozen brush cannot be repainted in place.
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using PanneauxGlass;

namespace WinlestiaFiles;

// Plain CLR properties returning the shared live brushes (PanneauxGlass.UI.T): bound from XAML, never frozen.
public sealed class LiveBrushes {
    public static readonly LiveBrushes Instance = new();
    public Brush Hover => UI.Tint;
    public Brush Selected => UI.Selected;
    public Brush Accent => UI.T(M3Role.Primary);
    public Brush DropTarget => UI.T(M3Role.PrimaryContainer);
    public Brush Label => UI.Label;
    public Brush Thumb => UI.T(M3Role.OnSurface, 70);
    public Brush ThumbHot => UI.T(M3Role.OnSurface, 130);
}

internal static class Styles {
    internal const int IconWidth = 36, ModifiedWidth = 140, TypeWidth = 110, SizeWidth = 84, ScrollWidth = 10, RowHeight = 32;

    const string Xaml = """
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" xmlns:local="clr-namespace:WinlestiaFiles;assembly=Winlestia.Files">
  <Style TargetType="ScrollBar">
    <Setter Property="Width" Value="@SCROLL@"/>
    <Setter Property="MinWidth" Value="@SCROLL@"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ScrollBar">
          <Grid Background="Transparent">
            <Track x:Name="PART_Track" IsDirectionReversed="True">
              <Track.DecreaseRepeatButton><RepeatButton Command="{x:Static ScrollBar.PageUpCommand}" Opacity="0" Focusable="False"/></Track.DecreaseRepeatButton>
              <Track.IncreaseRepeatButton><RepeatButton Command="{x:Static ScrollBar.PageDownCommand}" Opacity="0" Focusable="False"/></Track.IncreaseRepeatButton>
              <Track.Thumb>
                <Thumb>
                  <Thumb.Template>
                    <ControlTemplate TargetType="Thumb">
                      <Border x:Name="T" Margin="2,0" CornerRadius="3" Background="{Binding Thumb, Source={x:Static local:LiveBrushes.Instance}}"/>
                      <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="T" Property="Background" Value="{Binding ThumbHot, Source={x:Static local:LiveBrushes.Instance}}"/></Trigger>
                        <Trigger Property="IsDragging" Value="True"><Setter TargetName="T" Property="Background" Value="{Binding ThumbHot, Source={x:Static local:LiveBrushes.Instance}}"/></Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
            </Track>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsEnabled" Value="False"><Setter TargetName="PART_Track" Property="Visibility" Value="Hidden"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key="ListStyle" TargetType="ListBox">
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ListBox">
          <ScrollViewer Focusable="False" Padding="{TemplateBinding Padding}"><ItemsPresenter/></ScrollViewer>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key="RowStyle" TargetType="ListBoxItem">
    <Setter Property="Height" Value="@ROW@"/>
    <Setter Property="Padding" Value="0"/>
    <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
    <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ListBoxItem">
          <Border x:Name="Bd" CornerRadius="10" Background="Transparent"><ContentPresenter VerticalAlignment="Center"/></Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Bd" Property="Background" Value="{Binding Hover, Source={x:Static local:LiveBrushes.Instance}}"/></Trigger>
            <Trigger Property="IsSelected" Value="True"><Setter TargetName="Bd" Property="Background" Value="{Binding Selected, Source={x:Static local:LiveBrushes.Instance}}"/></Trigger>
            <DataTrigger Binding="{Binding DropHot}" Value="True"><Setter TargetName="Bd" Property="Background" Value="{Binding DropTarget, Source={x:Static local:LiveBrushes.Instance}}"/></DataTrigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <DataTemplate x:Key="RowTemplate">
    <Grid Opacity="{Binding CutOpacity}">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="@ICON@"/><ColumnDefinition Width="*"/><ColumnDefinition Width="@MOD@"/><ColumnDefinition Width="@TYPE@"/><ColumnDefinition Width="@SIZE@"/>
      </Grid.ColumnDefinitions>
      <TextBlock x:Name="Ic" Text="{Binding Icon, Mode=OneTime}" FontFamily="{DynamicResource IconFont}" FontSize="20" Foreground="{Binding Label, Source={x:Static local:LiveBrushes.Instance}}" HorizontalAlignment="Center" VerticalAlignment="Center"/>
      <local:ShellImage Cell="22" Width="22" Height="22" HorizontalAlignment="Center" VerticalAlignment="Center"/>
      <TextBlock Grid.Column="1" Text="{Binding Name, Mode=OneTime}" TextTrimming="CharacterEllipsis" VerticalAlignment="Center" Margin="4,0,8,0"/>
      <TextBlock Grid.Column="2" Text="{Binding ModifiedText, Mode=OneTime}" Foreground="{Binding Label, Source={x:Static local:LiveBrushes.Instance}}" VerticalAlignment="Center" Margin="8,0,0,0"/>
      <TextBlock Grid.Column="3" Text="{Binding TypeLabel, Mode=OneTime}" TextTrimming="CharacterEllipsis" Foreground="{Binding Label, Source={x:Static local:LiveBrushes.Instance}}" VerticalAlignment="Center" Margin="8,0,4,0"/>
      <TextBlock Grid.Column="4" Text="{Binding SizeText, Mode=OneTime}" TextAlignment="Right" Foreground="{Binding Label, Source={x:Static local:LiveBrushes.Instance}}" VerticalAlignment="Center" Margin="0,0,12,0"/>
    </Grid>
    <DataTemplate.Triggers>
      <DataTrigger Binding="{Binding IsDir, Mode=OneTime}" Value="True"><Setter TargetName="Ic" Property="Foreground" Value="{Binding Accent, Source={x:Static local:LiveBrushes.Instance}}"/></DataTrigger>
    </DataTemplate.Triggers>
  </DataTemplate>
  <ItemsPanelTemplate x:Key="TilePanel"><local:TilePanel/></ItemsPanelTemplate>

  <Style x:Key="TileStyle" TargetType="ListBoxItem">
    <Setter Property="Width" Value="128"/>
    <Setter Property="Height" Value="152"/>
    <Setter Property="Margin" Value="2"/>
    <Setter Property="Padding" Value="0"/>
    <Setter Property="FocusVisualStyle" Value="{x:Null}"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="ListBoxItem">
          <Border x:Name="Bd" CornerRadius="12" Background="Transparent"><ContentPresenter/></Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Bd" Property="Background" Value="{Binding Hover, Source={x:Static local:LiveBrushes.Instance}}"/></Trigger>
            <Trigger Property="IsSelected" Value="True"><Setter TargetName="Bd" Property="Background" Value="{Binding Selected, Source={x:Static local:LiveBrushes.Instance}}"/></Trigger>
            <DataTrigger Binding="{Binding DropHot}" Value="True"><Setter TargetName="Bd" Property="Background" Value="{Binding DropTarget, Source={x:Static local:LiveBrushes.Instance}}"/></DataTrigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <DataTemplate x:Key="TileTemplate">
    <Grid Opacity="{Binding CutOpacity}">
      <Grid.RowDefinitions><RowDefinition Height="112"/><RowDefinition Height="*"/></Grid.RowDefinitions>
      <TextBlock x:Name="Ic" Text="{Binding Icon, Mode=OneTime}" FontFamily="{DynamicResource IconFont}" FontSize="52" Foreground="{Binding Label, Source={x:Static local:LiveBrushes.Instance}}" HorizontalAlignment="Center" VerticalAlignment="Center"/>
      <local:ShellImage Thumb="True" Cell="104" Width="104" Height="104" HorizontalAlignment="Center" VerticalAlignment="Center" Margin="0,6,0,0"/>
      <TextBlock Grid.Row="1" Text="{Binding Name, Mode=OneTime}" TextWrapping="Wrap" TextTrimming="CharacterEllipsis" TextAlignment="Center" MaxHeight="36" Margin="6,2,6,0" VerticalAlignment="Top"/>
    </Grid>
    <DataTemplate.Triggers>
      <DataTrigger Binding="{Binding IsDir, Mode=OneTime}" Value="True"><Setter TargetName="Ic" Property="Foreground" Value="{Binding Accent, Source={x:Static local:LiveBrushes.Instance}}"/></DataTrigger>
    </DataTemplate.Triggers>
  </DataTemplate>
</ResourceDictionary>
""";

    internal static ResourceDictionary Load() {
        var context = new ParserContext();
        context.XmlnsDictionary.Add("", "http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        context.XmlnsDictionary.Add("x", "http://schemas.microsoft.com/winfx/2006/xaml");
        context.XmlnsDictionary.Add("local", "clr-namespace:WinlestiaFiles;assembly=Winlestia.Files");
        return (ResourceDictionary)XamlReader.Parse(Xaml
            .Replace("@SCROLL@", ScrollWidth.ToString()).Replace("@ROW@", RowHeight.ToString()).Replace("@ICON@", IconWidth.ToString())
            .Replace("@MOD@", ModifiedWidth.ToString()).Replace("@TYPE@", TypeWidth.ToString()).Replace("@SIZE@", SizeWidth.ToString()), context);
    }
}
