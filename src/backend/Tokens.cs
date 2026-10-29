// language: C# 12, file: Tokens.cs, target: Windows 11 / .NET 8 WPF, shared Material-3 tokens for the Atelier windows
// One source of truth with the QML shell: the palette is rebuilt here from the same inputs
// (themeMode / customPalette / wallpaper) with the same algorithm as src/frontend/custom_palette.py
// and chromatic_palette.py, so a WPF tool window and the dashboard read the same roles.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PanneauxGlass;

internal sealed class M3Palette {
    public bool Dark=true;
    public Color Surface,SurfaceLow,SurfaceContainer,SurfaceHigh,SurfaceHighest,OnSurface,OnSurfaceVariant,
        Primary,OnPrimary,PrimaryContainer,OnPrimaryContainer,Secondary,SecondaryContainer,OnSecondaryContainer,
        Outline,OutlineVariant,Error,OnError;
    static Color H(string hex)=>(Color)ColorConverter.ConvertFromString(hex);
    // Fallbacks of Colours.qml when Bridge.adaptivePalette is empty (dark / light shell mode).
    internal static M3Palette DarkDefault()=>new(){Dark=true,Surface=H("#101518"),SurfaceLow=H("#151c21"),SurfaceContainer=H("#182025"),SurfaceHigh=H("#242d33"),SurfaceHighest=H("#303b42"),
        OnSurface=H("#e5ebef"),OnSurfaceVariant=H("#bdc7cf"),Primary=H("#afd4f0"),OnPrimary=H("#12354a"),PrimaryContainer=H("#304b60"),OnPrimaryContainer=H("#d1e7fa"),
        Secondary=H("#c3c8d0"),SecondaryContainer=H("#404851"),OnSecondaryContainer=H("#e3e6ed"),Outline=H("#8897a2"),OutlineVariant=H("#3c4a54"),Error=H("#ffb4ab"),OnError=H("#690005")};
    internal static M3Palette LightDefault()=>new(){Dark=false,Surface=H("#fff8f6"),SurfaceLow=H("#fff0eb"),SurfaceContainer=H("#fae8e3"),SurfaceHigh=H("#f5e0d9"),SurfaceHighest=H("#eed9d2"),
        OnSurface=H("#261916"),OnSurfaceVariant=H("#54433e"),Primary=H("#895143"),OnPrimary=H("#ffffff"),PrimaryContainer=H("#ffdace"),OnPrimaryContainer=H("#351006"),
        Secondary=H("#775b52"),SecondaryContainer=H("#f5dcd3"),OnSecondaryContainer=H("#2b1811"),Outline=H("#85726a"),OutlineVariant=H("#d6c3bc"),Error=H("#b3261e"),OnError=H("#ffffff")};
    // custom_palette.py::_build
    internal static M3Palette Custom(IReadOnlyDictionary<string,string> colors){
        Color Get(string key,string fallback){try{if(colors.TryGetValue(key,out var v)&&v.Length==7&&v[0]=='#')return H(v);}catch{}return H(fallback);}
        var bg=Get("background","#fff8f6");var text=Get("text","#261916");var primary=Get("primary","#895143");var secondary=Get("secondary","#775b52");
        bool dark=Lum(bg)<.35;
        return new(){Dark=dark,Surface=bg,SurfaceLow=Theme.Mix(bg,text,.03),SurfaceContainer=Theme.Mix(bg,text,.06),SurfaceHigh=Theme.Mix(bg,text,.10),SurfaceHighest=Theme.Mix(bg,text,.14),
            OnSurface=text,OnSurfaceVariant=Theme.Mix(bg,text,.76),Primary=primary,OnPrimary=OnColor(primary),PrimaryContainer=Theme.Mix(bg,primary,.2),OnPrimaryContainer=text,
            Secondary=secondary,SecondaryContainer=Theme.Mix(bg,secondary,.2),OnSecondaryContainer=text,Outline=Theme.Mix(bg,text,.54),OutlineVariant=Theme.Mix(bg,text,.2),
            Error=dark?H("#ffb4ab"):H("#b3261e"),OnError=dark?H("#690005"):H("#ffffff")};
    }
    // Used for the legacy themes (and any token read on them): same roles, derived from surface/ink/accent.
    internal static M3Palette FromTheme(ThemeDefinition t){
        var s=t.Surface;var x=t.Text;var a=t.Accent;
        return new(){Dark=!t.Light,Surface=s,SurfaceLow=Theme.Mix(s,x,.03),SurfaceContainer=Theme.Mix(s,x,.06),SurfaceHigh=Theme.Mix(s,x,.10),SurfaceHighest=Theme.Mix(s,x,.14),
            OnSurface=x,OnSurfaceVariant=t.Dim,Primary=a,OnPrimary=OnColor(a),PrimaryContainer=Theme.Mix(s,a,.22),OnPrimaryContainer=x,
            Secondary=a,SecondaryContainer=Theme.Mix(s,a,.14),OnSecondaryContainer=x,Outline=Theme.Mix(s,x,.54),OutlineVariant=Theme.Mix(s,x,.2),
            Error=t.Light?H("#b3261e"):H("#ffb4ab"),OnError=t.Light?H("#ffffff"):H("#690005")};
    }
    internal static double Lum(Color c){
        static double L(byte v){double d=v/255.0;return d<=.04045?d/12.92:Math.Pow((d+.055)/1.055,2.4);}
        return .2126*L(c.R)+.7152*L(c.G)+.0722*L(c.B);
    }
    static Color OnColor(Color c)=>Lum(c)<.179?Colors.White:H("#101518");
    // chromatic_palette.py::generate_palette: hue-weighted wallpaper source, HSV surfaces, always dark.
    internal static M3Palette FromWallpaper(string path,string background){
        double chromatic=0;int pixels=1;var groups=new double[36,4];
        BitmapSource? image=null;
        try{if(!string.IsNullOrEmpty(path)&&File.Exists(path)){var bmp=new BitmapImage();bmp.BeginInit();bmp.CacheOption=BitmapCacheOption.OnLoad;bmp.DecodePixelWidth=160;bmp.CreateOptions=BitmapCreateOptions.IgnoreColorProfile;bmp.UriSource=new Uri(path);bmp.EndInit();bmp.Freeze();image=new FormatConvertedBitmap(bmp,PixelFormats.Bgr24,null,0);}}catch{image=null;}
        if(image!=null){
            int w=image.PixelWidth,h=image.PixelHeight,stride=w*3;var bytes=new byte[stride*h];image.CopyPixels(bytes,stride,0);pixels=Math.Max(1,w*h);
            for(int i=0;i<w*h;i++){
                int red=bytes[i*3+2],green=bytes[i*3+1],blue=bytes[i*3];double r=red/255.0,g=green/255.0,b=blue/255.0;
                Hsv(r,g,b,out var hue,out var sat,out var val);double chroma=Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b));
                if(sat<.16||chroma<.035||val<.08)continue;
                double weight=sat*sat*Math.Pow(chroma,.7)*Math.Sqrt(val);chromatic+=weight;
                double position=hue*36;int index=(int)position;double fraction=position-index;
                for(int k=0;k<2;k++){int bucket=(index+k)%36;double f=k==0?1-fraction:fraction;double wt=weight*f;groups[bucket,0]+=wt;groups[bucket,1]+=red*wt;groups[bucket,2]+=green*wt;groups[bucket,3]+=blue*wt;}
            }
        }
        bool mono=chromatic/pixels<.003;
        var scores=new double[36];
        for(int i=0;i<36;i++)for(int j=-1;j<=1;j++)scores[i]+=groups[(i+j+36)%36,0]*(j==0?1:.65);
        Color source=H("#777777");
        if(!mono){
            int best=Enumerable.Range(0,36).OrderByDescending(i=>scores[i]).First();
            if(scores[best]>0){double total=0,sr=0,sg=0,sb=0;for(int j=-1;j<=1;j++){int n=(best+j+36)%36;total+=groups[n,0];sr+=groups[n,1];sg+=groups[n,2];sb+=groups[n,3];}
                if(total>0)source=Color.FromRgb((byte)Math.Round(sr/total),(byte)Math.Round(sg/total),(byte)Math.Round(sb/total));else mono=true;}
            else mono=true;
        }
        Hsv(source.R/255.0,source.G/255.0,source.B/255.0,out var hh,out var ss,out _);
        Color C(double sat,double val)=>FromHsv(hh,sat,val);
        double acc=Math.Min(.76,Math.Max(.55,ss));Color accent=mono?H("#d0d0d0"):C(acc,.98);var ink=H("#130d0e");
        while(Contrast(accent,H("#130d0e"))<4.8&&acc>.15){acc-=.025;accent=C(acc,.98);}
        double m(double s)=>mono?0:s;
        return new(){Dark=true,Surface=C(m(.28),.063),SurfaceLow=C(m(.24),.095),SurfaceContainer=C(m(.23),.13),SurfaceHigh=C(m(.22),.17),SurfaceHighest=C(m(.20),.22),
            OnSurface=H("#f7f3f4"),OnSurfaceVariant=H("#d3c9cc"),Primary=accent,OnPrimary=ink,PrimaryContainer=C(m(.55),.25),OnPrimaryContainer=C(m(.1),.98),
            Secondary=accent,SecondaryContainer=C(m(.38),.21),OnSecondaryContainer=C(m(.08),.95),Outline=Theme.Mix(C(m(.2),.30),H("#d3c9cc"),.5),OutlineVariant=C(m(.2),.30),
            Error=H("#ffb4ab"),OnError=H("#690005")};
    }
    static double Contrast(Color a,Color b){double x=Lum(a)*100+5,y=Lum(b)*100+5;return Math.Max(x,y)/Math.Min(x,y);}
    // colorsys equivalents
    static void Hsv(double r,double g,double b,out double h,out double s,out double v){
        double max=Math.Max(r,Math.Max(g,b)),min=Math.Min(r,Math.Min(g,b));v=max;if(max==min){h=0;s=0;return;}
        double d=max-min;s=d/max;double rc=(max-r)/d,gc=(max-g)/d,bc=(max-b)/d;
        h=r==max?bc-gc:g==max?2+rc-bc:4+gc-rc;h=(h/6.0)%1.0;if(h<0)h+=1;
    }
    static Color FromHsv(double h,double s,double v){
        if(s==0)return Color.FromRgb((byte)Math.Round(v*255),(byte)Math.Round(v*255),(byte)Math.Round(v*255));
        int i=(int)(h*6.0);double f=h*6.0-i;double p=v*(1-s),q=v*(1-s*f),t=v*(1-s*(1-f));i%=6;
        (double r,double g,double b)=i switch{0=>(v,t,p),1=>(q,v,p),2=>(p,v,t),3=>(p,q,v),4=>(t,p,v),_=>(v,p,q)};
        return Color.FromRgb((byte)Math.Round(r*255),(byte)Math.Round(g*255),(byte)Math.Round(b*255));
    }
}

internal enum M3Role { Surface,SurfaceLow,SurfaceContainer,SurfaceHigh,SurfaceHighest,OnSurface,OnSurfaceVariant,Primary,OnPrimary,PrimaryContainer,OnPrimaryContainer,SecondaryContainer,OnSecondaryContainer,Outline,OutlineVariant,Error,OnError }

// Design tokens shared with AppearanceConfig.qml (rounding, spacing, padding, type, motion).
internal static class M3 {
    internal const double Small=12,Normal=17,Large=25;                   // rounding
    internal const double Sp1=7,Sp2=10,Sp3=12,Sp4=15,Sp5=20;             // spacing
    internal const double Pad1=5,Pad2=7,Pad3=10,Pad4=12,Pad5=15;         // padding
    internal static double Pt(double points)=>points*96.0/72.0;          // QML pt -> WPF device-independent px
    internal static readonly double FontSmall=Pt(11),FontBody=Pt(12),FontLabel=Pt(13),FontTitle=Pt(15),FontHeadline=Pt(18),FontDisplay=Pt(28);
    internal const int AnimFast=200,AnimSlow=400;
    internal const double MenuItemHeight=36;
    // Maps the legacy radii (10..16 in the old XAML) onto the three QML rounding steps.
    internal static int Round(int basis)=>basis<=13?(int)Small:basis<=19?(int)Normal:(int)Large;
    static FontFamily? material;
    internal static FontFamily? Symbols{get{_=Body;return material;}}
    static FontFamily? body;
    internal static FontFamily Body=>body??=ResolveBody();
    // Same switch as QtShellBridge.LightRendering (settings.reducedEffects): off = no animation at all.
    internal static bool ReducedEffects=>Store.State.ReducedEffects??GC.GetGCMemoryInfo().TotalAvailableMemoryBytes<=8L*1024*1024*1024;
    static string? FontDirectory(){
        var root=Store.Root;
        foreach(var candidate in new[]{Path.Combine(root,"assets","fonts"),Path.Combine(root,"..","Caelestia-Windows","assets","fonts"),Path.Combine(root,"..","frontend","assets","fonts"),Path.Combine(root,"..","..","frontend","assets","fonts"),Path.Combine(root,"..","..","src","frontend","assets","fonts"),Path.Combine(root,"..","..","..","src","frontend","assets","fonts")}){
            try{if(File.Exists(Path.Combine(candidate,"Rubik.ttf")))return Path.GetFullPath(candidate);}catch{}
        }
        return null;
    }
    static FontFamily ResolveBody(){
        const string fallback="Segoe UI Variable Text, Segoe UI";
        try{
            var installed=Fonts.SystemFontFamilies.Any(f=>f.Source.StartsWith("Rubik",StringComparison.OrdinalIgnoreCase));
            if(installed)return new FontFamily("Rubik, "+fallback);
            var dir=FontDirectory();
            if(dir!=null){var uri=new Uri(dir+Path.DirectorySeparatorChar);
                if(File.Exists(Path.Combine(dir,"MaterialSymbolsRounded.ttf")))material=new FontFamily(uri,"./#Material Symbols Rounded");
                return new FontFamily(uri,"./#Rubik, "+fallback);}
        }catch{}
        return new FontFamily(fallback);
    }
    // Wallpaper-driven (auto) palette, rebuilt off the UI thread only when the picture changes.
    static (string path,long modified)? wallpaperKey;static M3Palette? auto;static int generation;
    internal static M3Palette? AutoPalette=>auto;
    internal static void Wallpaper(WallpaperMonitor[] monitors,Action apply){
        if(!Store.State.AutoTheme||monitors.Length==0)return;
        var monitor=monitors.FirstOrDefault(m=>m.x==0&&m.y==0)??monitors[0];var key=(monitor.path??"",monitor.modified);
        if(wallpaperKey==key)return;wallpaperKey=key;int mine=++generation;
        Task.Run(()=>{
            M3Palette palette;try{palette=M3Palette.FromWallpaper(key.Item1,monitor.background??"#182025");}catch{if(mine==generation)wallpaperKey=null;return;}
            var app=Application.Current;if(app==null)return;
            app.Dispatcher.BeginInvoke(()=>{if(mine!=generation)return;auto=palette;apply();});
        });
    }
}
