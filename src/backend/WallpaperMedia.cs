// C# 12 / WallpaperMedia.cs / Windows. One-shot helpers for the Automatique theme: video frame sampling (Media Foundation via Windows.Media.Editing) and live-host capture (PrintWindow).
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
namespace PanneauxGlass;
internal static class WallpaperMedia {
    // CLI: Panneaux.Glass.exe --extract-frames <video> <outDir> [count=8] [maxSize=320]. Writes f0..f{n-1}.jpg, exit 0 when at least one frame was written.
    internal static int ExtractFramesCli(string[] args){
        try{
            string video=args[1],folder=args[2];int count=args.Length>3&&int.TryParse(args[3],out int c)?Math.Clamp(c,1,16):8,size=args.Length>4&&int.TryParse(args[4],out int z)?Math.Clamp(z,64,1024):320;
            return Task.Run(()=>ExtractFrames(video,folder,count,size)).GetAwaiter().GetResult()>0?0:1;
        }catch(Exception ex){Store.Log("Images du fond: "+ex.Message);return 2;}
    }
    internal static async Task<int> ExtractFrames(string video,string folder,int count,int size){
        Directory.CreateDirectory(folder);
        var file=await Windows.Storage.StorageFile.GetFileFromPathAsync(video);
        // One independent clip + composition per frame so the thumbnails decode in parallel (a single composition serialises them).
        var results=await Task.WhenAll(Enumerable.Range(0,count).Select(i=>Task.Run(async()=>{
            try{
                var clip=await Windows.Media.Editing.MediaClip.CreateFromFileAsync(file);
                var composition=new Windows.Media.Editing.MediaComposition();composition.Clips.Add(clip);
                var props=clip.GetVideoEncodingProperties();double scale=Math.Min(1.0,(double)size/Math.Max(1u,Math.Max(props.Width,props.Height)));
                int width=Math.Max(2,(int)(props.Width*scale)),height=Math.Max(2,(int)(props.Height*scale));
                var at=TimeSpan.FromTicks((long)(clip.OriginalDuration.Ticks*((i+0.5)/count)));
                using var stream=await composition.GetThumbnailAsync(at,width,height,Windows.Media.Editing.VideoFramePrecision.NearestKeyFrame);
                using var input=new MemoryStream();await System.IO.WindowsRuntimeStreamExtensions.AsStreamForRead(stream).CopyToAsync(input);input.Position=0;
                var frame=BitmapDecoder.Create(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad).Frames[0];
                var encoder=new JpegBitmapEncoder{QualityLevel=85};encoder.Frames.Add(BitmapFrame.Create(frame));
                using var output=File.Create(Path.Combine(folder,$"f{i}.jpg"));encoder.Save(output);return 1;
            }catch(Exception ex){Store.Log($"Image {i} du fond: {ex.Message}");return 0;}
        })));
        return results.Sum();
    }

    // One-shot capture of the live-wallpaper host (child of Progman / WorkerW) with PrintWindow(PW_RENDERFULLCONTENT). Returns a temp PNG path, or null when the host is absent or the frame is black
    // (GPU-composited hosts often print black): the frontend then falls back to the Windows wallpaper file. Windows.Graphics.Capture was not used: it needs a D3D11 device and a GraphicsCaptureItem interop for little gain here.
    internal static string? CaptureHost(Native.Rect? monitor=null){
        IntPtr host=WindowsWallpaper.FindHost(monitor);if(host==IntPtr.Zero||!WallpaperNative.GetWindowRect(host,out var r))return null;
        int w=r.Right-r.Left,h=r.Bottom-r.Top;if(w<16||h<16)return null;
        // PrintWindow and every bitmap/HDC live inside one task that disposes them itself: on timeout we return null and never free what a hung PrintWindow may still be writing to.
        var work=Task.Run(()=>{
        using var full=new Bitmap(w,h,PixelFormat.Format32bppRgb);
        using(var g=Graphics.FromImage(full)){IntPtr dc=g.GetHdc();try{if(!PrintWindow(host,dc,2))return null;}finally{g.ReleaseHdc(dc);}}
        var crop=new Rectangle(0,0,w,h);if(monitor is{} m&&(m.Right-m.Left<w-2||m.Bottom-m.Top<h-2)){crop=Rectangle.Intersect(crop,new Rectangle(m.Left-r.Left,m.Top-r.Top,m.Right-m.Left,m.Bottom-m.Top));if(crop.Width<16||crop.Height<16)crop=new Rectangle(0,0,w,h);}
        int tw=Math.Min(640,crop.Width),th=Math.Max(1,crop.Height*tw/crop.Width);using var small=new Bitmap(tw,th,PixelFormat.Format32bppRgb);using(var g=Graphics.FromImage(small)){g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;g.DrawImage(full,new Rectangle(0,0,tw,th),crop,GraphicsUnit.Pixel);}
        if(IsBlack(small))return null;
        string folder=Path.Combine(Path.GetTempPath(),"Winlestia-wallpaper");Directory.CreateDirectory(folder);
        foreach(var old in Directory.GetFiles(folder,"cap-*.png").OrderByDescending(File.GetLastWriteTimeUtc).Skip(4))try{File.Delete(old);}catch{}
        string path=Path.Combine(folder,$"cap-{DateTime.UtcNow.Ticks}.png");small.Save(path,ImageFormat.Png);return path;
        });
        try{return work.Wait(3000)?work.Result:null;}catch{return null;}   // a hung host must not stall the command
    }
    static bool IsBlack(Bitmap b){
        long sum=0;int max=0,n=0;
        for(int y=0;y<b.Height;y+=Math.Max(1,b.Height/20))for(int x=0;x<b.Width;x+=Math.Max(1,b.Width/20)){var p=b.GetPixel(x,y);int v=(p.R+p.G+p.B)/3;sum+=v;max=Math.Max(max,v);n++;}
        return n==0||(sum/n<4&&max<16);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool PrintWindow(IntPtr hwnd,IntPtr hdc,uint flags);
}
