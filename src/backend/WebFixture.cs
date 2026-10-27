// language: C# 12, file: WebFixture.cs, target: Windows 11 / .NET 8, isolated loopback checks for favicon discovery and offline fallback
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PanneauxGlass;
internal static class WebFixture {
    internal static async Task<(bool discovered,bool offline)> Run(){
        var server=new TcpListener(IPAddress.Loopback,0);server.Start();int port=((IPEndPoint)server.LocalEndpoint).Port;using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        string icon=Path.Combine(Store.Root,"web-icons","fixture.png");WebIcons.Default(icon);byte[] png=File.ReadAllBytes(icon);
        var replies=Task.Run(async()=>{for(int i=0;i<2;i++){using var socket=await server.AcceptTcpClientAsync(deadline.Token);using var stream=socket.GetStream();using var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);string first=await reader.ReadLineAsync(deadline.Token)??"";while(!string.IsNullOrEmpty(await reader.ReadLineAsync(deadline.Token))){}bool image=first.Contains("/logo.png");byte[] body=image?png:Encoding.UTF8.GetBytes("<html><head><title>Fixture Panneaux</title><link rel='icon' href='/logo.png' type='image/png'></head></html>");byte[] header=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nConnection: close\r\nContent-Type: "+(image?"image/png":"text/html")+"\r\nContent-Length: "+body.Length+"\r\n\r\n");await stream.WriteAsync(header,deadline.Token);await stream.WriteAsync(body,deadline.Token);}});
        bool discovered=false;try{var result=await WebIcons.Resolve(new Uri("http://127.0.0.1:"+port+"/"),deadline.Token);discovered=!result.Fallback&&result.Name=="Fixture Panneaux"&&File.Exists(result.Path);await replies;}finally{server.Stop();}
        var spare=new TcpListener(IPAddress.Loopback,0);spare.Start();int unused=((IPEndPoint)spare.LocalEndpoint).Port;spare.Stop();var offline=await WebIcons.Resolve(new Uri("http://127.0.0.1:"+unused+"/"),deadline.Token);return(discovered,offline.Fallback&&File.Exists(offline.Path));
    }
}
