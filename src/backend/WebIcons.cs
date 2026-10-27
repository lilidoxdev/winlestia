// language: C# 12, file: WebIcons.cs, target: Windows 11 / .NET 8, bounded direct favicon discovery and transparent PNG cache
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing=System.Drawing;

namespace PanneauxGlass;
internal sealed record WebIconResult(string Name,string Path,bool Fallback);
internal static class WebIcons {
    static readonly HttpClient client=new(new HttpClientHandler{AutomaticDecompression=DecompressionMethods.All}){Timeout=TimeSpan.FromSeconds(5)};
    const int ImageLimit=2*1024*1024;
    internal static Uri Parse(string input){string value=input.Trim();if(!value.Contains("://"))value="https://"+value;if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme is not ("https" or "http")||string.IsNullOrWhiteSpace(uri.Host)||!string.IsNullOrEmpty(uri.UserInfo))throw new FormatException("Entre une adresse de site, par exemple gmail.com ou https://github.com.");return uri;}
    internal static ImageSource Image(string path){var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.UriSource=new Uri(path,UriKind.Absolute);bitmap.DecodePixelWidth=96;bitmap.EndInit();bitmap.Freeze();return bitmap;}
    internal static async Task<WebIconResult> Resolve(Uri uri,CancellationToken cancellation){
        string directory=Path.Combine(Store.Root,"web-icons");Directory.CreateDirectory(directory);string key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.GetLeftPart(UriPartial.Authority)))).Substring(0,24);string path=Path.Combine(directory,key+".png");
        string host=uri.IdnHost.ToLowerInvariant(),name=host.StartsWith("www.")?host[4..]:host;string? known=null;
        if(host is "gmail.com" or "mail.google.com" or "www.gmail.com"){name="Gmail";known="https://ssl.gstatic.com/ui/v1/icons/mail/rfr/gmail.ico";}
        else if(host is "github.com" or "www.github.com"){name="GitHub";known="https://github.githubassets.com/favicons/favicon.png";}
        string bundled=Path.Combine(Store.Root,"assets","web",name=="Gmail"?"gmail.ico":name=="GitHub"?"github.png":"missing");
        if(known!=null&&File.Exists(bundled)){Normalize(File.ReadAllBytes(bundled),path);return new(name,path,false);}
        if(File.Exists(path)){try{_ =Image(path);bool fallback=File.Exists(path+".fallback")&&File.GetLastWriteTimeUtc(path+".fallback")>=File.GetLastWriteTimeUtc(path);if(!fallback||File.GetLastWriteTimeUtc(path)>DateTime.UtcNow.AddHours(-1))return new(name,path,fallback);}catch{}}
        var candidates=new List<Uri>();if(known!=null)candidates.Add(new Uri(known));
        async Task<bool> TryImage(Uri source){try{byte[] bytes=await Download(source,ImageLimit,cancellation);if(bytes.Length<24)return false;Normalize(bytes,path);return true;}catch(OperationCanceledException)when(cancellation.IsCancellationRequested){throw;}catch{return false;}}
        foreach(var candidate in candidates)if(await TryImage(candidate))return new(name,path,false);
        using var budget=CancellationTokenSource.CreateLinkedTokenSource(cancellation);budget.CancelAfter(TimeSpan.FromSeconds(12));
        try{
            string html=Encoding.UTF8.GetString(await Download(uri,512*1024,budget.Token));
            if(known==null){var title=Regex.Match(html,@"<title[^>]*>(.*?)</title>",RegexOptions.IgnoreCase|RegexOptions.Singleline,TimeSpan.FromMilliseconds(100));if(title.Success){string decoded=WebUtility.HtmlDecode(Regex.Replace(title.Groups[1].Value,@"\s+"," ")).Trim();if(decoded.Length is >0 and <=90)name=decoded;}}
            var links=Regex.Matches(html,@"<link\b[^>]{0,4096}>",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(100));
            foreach(Match link in links){var attrs=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);foreach(Match attr in Regex.Matches(link.Value,"([\\w-]+)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))",RegexOptions.None,TimeSpan.FromMilliseconds(100)))attrs[attr.Groups[1].Value]=WebUtility.HtmlDecode(attr.Groups[2].Success?attr.Groups[2].Value:attr.Groups[3].Success?attr.Groups[3].Value:attr.Groups[4].Value);
                if(attrs.TryGetValue("rel",out string? rel)&&rel.Split(' ',StringSplitOptions.RemoveEmptyEntries).Any(x=>x.Equals("icon",StringComparison.OrdinalIgnoreCase)||x.StartsWith("apple-touch-icon",StringComparison.OrdinalIgnoreCase))&&attrs.TryGetValue("href",out string? href)&&Uri.TryCreate(uri,href,out var imageUri)&&imageUri.Scheme is "https" or "http")candidates.Add(imageUri);
            }
        }catch(OperationCanceledException)when(cancellation.IsCancellationRequested){throw;}catch{}
        candidates.Add(new Uri(uri.GetLeftPart(UriPartial.Authority)+"/favicon.ico"));
        foreach(var candidate in candidates.Distinct().Take(7)){cancellation.ThrowIfCancellationRequested();if(budget.IsCancellationRequested)break;try{byte[] bytes=await Download(candidate,ImageLimit,budget.Token);Normalize(bytes,path);return new(name,path,false);}catch(OperationCanceledException)when(cancellation.IsCancellationRequested){throw;}catch{}}
        Default(path);File.WriteAllText(path+".fallback","Designed local fallback");return new(name,path,true);
    }
    static async Task<byte[]> Download(Uri uri,int limit,CancellationToken token){using var request=new HttpRequestMessage(HttpMethod.Get,uri);request.Headers.UserAgent.ParseAdd("PanneauxGlass/5.1");using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);response.EnsureSuccessStatusCode();if(response.Content.Headers.ContentLength>limit)throw new InvalidDataException("Ressource trop volumineuse.");await using var stream=await response.Content.ReadAsStreamAsync(token);using var memory=new MemoryStream();var buffer=new byte[8192];int read;while((read=await stream.ReadAsync(buffer,token))>0){if(memory.Length+read>limit)throw new InvalidDataException("Ressource trop volumineuse.");memory.Write(buffer,0,read);}return memory.ToArray();}
    internal static void Normalize(byte[] data,string path){using var stream=new MemoryStream(data);var decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnLoad);var frame=decoder.Frames.Where(x=>x.PixelWidth<=2048&&x.PixelHeight<=2048).OrderByDescending(x=>x.PixelWidth).FirstOrDefault()??throw new InvalidDataException("Icône invalide.");BitmapSource source=frame;if(frame.PixelWidth>256||frame.PixelHeight>256)source=new TransformedBitmap(frame,new ScaleTransform(Math.Min(256d/frame.PixelWidth,256d/frame.PixelHeight),Math.Min(256d/frame.PixelWidth,256d/frame.PixelHeight)));var rgba=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);int w=rgba.PixelWidth,h=rgba.PixelHeight,stride=w*4;byte[] pixels=new byte[stride*h];rgba.CopyPixels(pixels,stride,0);
        // Remove only a uniform, border-connected matte; enclosed white logo detail stays intact.
        int[] corners={0,(w-1)*4,(h-1)*stride,(h-1)*stride+(w-1)*4};byte b=pixels[0],g=pixels[1],r=pixels[2];bool matte=corners.All(i=>pixels[i+3]>245&&Math.Abs(pixels[i]-b)<12&&Math.Abs(pixels[i+1]-g)<12&&Math.Abs(pixels[i+2]-r)<12);
        if(matte){var queue=new Queue<int>();var visited=new bool[w*h];foreach(int p in new[]{0,w-1,(h-1)*w,h*w-1})queue.Enqueue(p);while(queue.Count>0){int p=queue.Dequeue();if(visited[p])continue;visited[p]=true;int i=p*4;if(Math.Abs(pixels[i]-b)>28||Math.Abs(pixels[i+1]-g)>28||Math.Abs(pixels[i+2]-r)>28)continue;pixels[i+3]=0;int x=p%w,y=p/w;if(x>0)queue.Enqueue(p-1);if(x<w-1)queue.Enqueue(p+1);if(y>0)queue.Enqueue(p-w);if(y<h-1)queue.Enqueue(p+w);}}
        bool monochrome=true;long luminance=0,count=0;for(int i=0;i<pixels.Length;i+=4){if(pixels[i+3]<32)continue;if(Math.Max(pixels[i],Math.Max(pixels[i+1],pixels[i+2]))-Math.Min(pixels[i],Math.Min(pixels[i+1],pixels[i+2]))>24)monochrome=false;luminance+=(pixels[i]+pixels[i+1]+pixels[i+2])/3;count++;}
        if(count==0)throw new InvalidDataException("Le logo est entièrement transparent.");
        // Preserve the exact alpha silhouette, using the light logo variant on dark glass.
        if(monochrome&&count>0&&luminance/count<110)for(int i=0;i<pixels.Length;i+=4)if(pixels[i+3]>0){pixels[i]=255;pixels[i+1]=247;pixels[i+2]=242;}
        var bitmap=BitmapSource.Create(w,h,96,96,PixelFormats.Bgra32,null,pixels,stride);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);png.Save(output);
    }
    internal static void Default(string path){using var bitmap=new Drawing.Bitmap(128,128,Drawing.Imaging.PixelFormat.Format32bppArgb);using var graphics=Drawing.Graphics.FromImage(bitmap);graphics.SmoothingMode=Drawing.Drawing2D.SmoothingMode.AntiAlias;graphics.Clear(Drawing.Color.Transparent);using var brush=new Drawing.Drawing2D.LinearGradientBrush(new Drawing.Rectangle(14,14,100,100),Drawing.Color.FromArgb(192,237,255),Drawing.Color.FromArgb(111,147,238),45);using var pen=new Drawing.Pen(brush,6);graphics.DrawEllipse(pen,17,17,94,94);graphics.DrawEllipse(pen,42,17,44,94);graphics.DrawArc(pen,17,31,94,31,0,180);graphics.DrawArc(pen,17,65,94,31,180,180);graphics.DrawLine(pen,18,64,110,64);bitmap.Save(path,Drawing.Imaging.ImageFormat.Png);}
}
