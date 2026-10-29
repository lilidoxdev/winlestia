// C# 12 / WgcCapture.cs / Windows 10 1903+. One-shot Windows.Graphics.Capture of the live-wallpaper host window, for videos Media Foundation cannot decode (HEVC without the Store extension).
// CLI: Panneaux.Glass.exe --capture-frames <outDir> [count=8] [maxSize=320] [intervalMs=400] [budgetMs=4300]. Everything (session, frame pool, D3D device) is closed before returning.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
namespace PanneauxGlass;
internal static class WgcCapture {
    [ComImport,Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IGraphicsCaptureItemInterop{IntPtr CreateForWindow(IntPtr window,ref Guid iid);IntPtr CreateForMonitor(IntPtr monitor,ref Guid iid);}
    [DllImport("combase.dll")]static extern int RoGetActivationFactory(IntPtr classId,ref Guid iid,out IntPtr factory);
    [DllImport("combase.dll",CharSet=CharSet.Unicode)]static extern int WindowsCreateString(string source,int length,out IntPtr hstring);
    [DllImport("combase.dll")]static extern int WindowsDeleteString(IntPtr hstring);
    [DllImport("d3d11.dll")]static extern int D3D11CreateDevice(IntPtr adapter,int driverType,IntPtr software,uint flags,IntPtr levels,uint count,uint sdk,out IntPtr device,out int level,out IntPtr context);
    [DllImport("d3d11.dll")]static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgi,out IntPtr graphicsDevice);
    static readonly Guid ItemIid=new("79C3F95B-31F7-4EC2-A464-632EF5D30760"),InteropIid=new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"),DxgiDeviceIid=new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");

    internal static int Cli(string[] args){   // optional args 6..9: monitor x y w h (host chosen and cropped to that monitor)
        try{
            string folder=args[1];int Num(int i,int d,int lo,int hi)=>args.Length>i&&int.TryParse(args[i],out int v)?Math.Clamp(v,lo,hi):d;
            int count=Num(2,8,1,16),size=Num(3,320,64,1024),interval=Num(4,400,100,2000),budget=Num(5,4300,1000,5000);
            Native.Rect? monitor=args.Length>9&&int.TryParse(args[6],out int mx)&&int.TryParse(args[7],out int my)&&int.TryParse(args[8],out int mw)&&int.TryParse(args[9],out int mh)&&mw>0?new Native.Rect{Left=mx,Top=my,Right=mx+mw,Bottom=my+mh}:null;
            return Task.Run(()=>Run(folder,count,size,interval,budget,monitor)).GetAwaiter().GetResult()>0?0:1;
        }catch(Exception ex){Store.Log("Capture WGC: "+ex.Message);return 2;}
    }

    static GraphicsCaptureItem? ItemFor(IntPtr hwnd){
        IntPtr hs=IntPtr.Zero,factory=IntPtr.Zero,item=IntPtr.Zero;
        try{
            string cls="Windows.Graphics.Capture.GraphicsCaptureItem";if(WindowsCreateString(cls,cls.Length,out hs)!=0)return null;
            var iid=InteropIid;if(RoGetActivationFactory(hs,ref iid,out factory)!=0)return null;
            var interop=(IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factory);var itemIid=ItemIid;
            item=interop.CreateForWindow(hwnd,ref itemIid);return GraphicsCaptureItem.FromAbi(item);
        }catch{return null;}
        finally{if(item!=IntPtr.Zero)Marshal.Release(item);if(factory!=IntPtr.Zero)Marshal.Release(factory);if(hs!=IntPtr.Zero)WindowsDeleteString(hs);}
    }

    static async Task<int> Run(string folder,int count,int size,int interval,int budget,Native.Rect? monitor){
        if(!GraphicsCaptureSession.IsSupported())return 0;
        var clock=Stopwatch.StartNew();Directory.CreateDirectory(folder);
        // Candidates: the live host (child of Progman/WorkerW), then its parents (a child HWND is not always capturable).
        IntPtr host=WindowsWallpaper.FindHost(monitor);if(host==IntPtr.Zero)return 0;
        var candidates=new System.Collections.Generic.List<IntPtr>{host};for(IntPtr p=WallpaperNative.GetParent(host);p!=IntPtr.Zero&&candidates.Count<3;p=WallpaperNative.GetParent(p))candidates.Add(p);
        IntPtr d3d=IntPtr.Zero,context=IntPtr.Zero,dxgi=IntPtr.Zero,graphics=IntPtr.Zero;IDirect3DDevice? device=null;Direct3D11CaptureFramePool? pool=null;GraphicsCaptureSession? session=null;int written=0;
        try{
            if(D3D11CreateDevice(IntPtr.Zero,1,IntPtr.Zero,0x20,IntPtr.Zero,0,7,out d3d,out _,out context)!=0)return 0;
            var dxgiIid=DxgiDeviceIid;if(Marshal.QueryInterface(d3d,ref dxgiIid,out dxgi)!=0||CreateDirect3D11DeviceFromDXGIDevice(dxgi,out graphics)!=0)return 0;
            device=WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(graphics);
            foreach(var window in candidates){
                var item=ItemFor(window);if(item==null||item.Size.Width<16||item.Size.Height<16)continue;
                pool=Direct3D11CaptureFramePool.CreateFreeThreaded(device,DirectXPixelFormat.B8G8R8A8UIntNormalized,2,item.Size);
                session=pool.CreateCaptureSession(item);
                try{session.IsCursorCaptureEnabled=false;}catch{}
                try{typeof(GraphicsCaptureSession).GetProperty("IsBorderRequired")?.SetValue(session,false);}catch{}  // Windows 11 only, may need the borderless capability: failure just means the system border stays during the ~3 s grab
                session.StartCapture();
                long lastSaved=-interval;int tries=0;var own=Stopwatch.StartNew();
                while(written<count&&clock.ElapsedMilliseconds<budget&&(written>0||own.ElapsedMilliseconds<1200)){   // each candidate window gets about 1.2 s while it yields nothing, so the parent fallback stays reachable
                    await Task.Delay(60);
                    using var frame=pool.TryGetNextFrame();if(frame==null)continue;
                    if(clock.ElapsedMilliseconds-lastSaved<interval)continue;
                    if(await SaveFrame(frame.Surface,Path.Combine(folder,$"f{written}.jpg"),size,CropFor(window,monitor))){written++;lastSaved=clock.ElapsedMilliseconds;tries=0;}
                    else if(++tries>=4&&written==0)break;   // black from the start: try the next window
                }
                session.Dispose();session=null;pool.Dispose();pool=null;
                if(written>0)break;
            }
        }finally{
            session?.Dispose();pool?.Dispose();(device as IDisposable)?.Dispose();
            foreach(var p in new[]{graphics,dxgi,context,d3d})if(p!=IntPtr.Zero)Marshal.Release(p);
        }
        return written;
    }

    static BitmapBounds? CropFor(IntPtr window,Native.Rect? monitor){
        if(monitor is not{} m||!WallpaperNative.GetWindowRect(window,out var r))return null;
        int w=r.Right-r.Left,h=r.Bottom-r.Top;if(m.Right-m.Left>=w-2&&m.Bottom-m.Top>=h-2)return null;
        int x=Math.Max(0,m.Left-r.Left),y=Math.Max(0,m.Top-r.Top),cw=Math.Min(w-x,m.Right-m.Left),ch=Math.Min(h-y,m.Bottom-m.Top);
        return cw<16||ch<16?null:new BitmapBounds{X=(uint)x,Y=(uint)y,Width=(uint)cw,Height=(uint)ch};
    }
    static async Task<bool> SaveFrame(IDirect3DSurface surface,string path,int size,BitmapBounds? crop){
        using var bitmap=await SoftwareBitmap.CreateCopyFromSurfaceAsync(surface);
        using var bgra=SoftwareBitmap.Convert(bitmap,BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore);
        var bytes=new byte[bgra.PixelWidth*bgra.PixelHeight*4];bgra.CopyToBuffer(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(bytes));
        long sum=0;int max=0,n=0;int step=Math.Max(1,bytes.Length/4/600)*4;
        for(int i=0;i+2<bytes.Length;i+=step){int v=(bytes[i]+bytes[i+1]+bytes[i+2])/3;sum+=v;if(v>max)max=v;n++;}
        if(n==0||(sum/n<4&&max<16))return false;   // black / empty frame
        uint cw=crop?.Width??(uint)bgra.PixelWidth,ch=crop?.Height??(uint)bgra.PixelHeight;double scale=Math.Min(1.0,(double)size/Math.Max(cw,ch));
        using var stream=new InMemoryRandomAccessStream();
        var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId,stream);encoder.SetSoftwareBitmap(bgra);
        if(crop is{} b)encoder.BitmapTransform.Bounds=b;encoder.BitmapTransform.ScaledWidth=(uint)Math.Max(2,cw*scale);encoder.BitmapTransform.ScaledHeight=(uint)Math.Max(2,ch*scale);encoder.BitmapTransform.InterpolationMode=BitmapInterpolationMode.Fant;
        await encoder.FlushAsync();stream.Seek(0);var data=new byte[stream.Size];await stream.ReadAsync(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(data),(uint)data.Length,InputStreamOptions.None);
        await File.WriteAllBytesAsync(path,data);return true;
    }
}
