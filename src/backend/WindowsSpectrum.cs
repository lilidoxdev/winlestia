// C# 12 / WindowsSpectrum.cs / Windows 11. FFT of the selected process tree, never global output.
using System;using System.Threading;using System.Runtime.InteropServices;using NAudio.Dsp;
namespace PanneauxGlass;
internal sealed class WindowsSpectrum:IDisposable{
 [DllImport("ProcessAudio.dll",CallingConvention=CallingConvention.Cdecl)]static extern IntPtr AudioCreate(uint pid);
 [DllImport("ProcessAudio.dll",CallingConvention=CallingConvention.Cdecl)]static extern void AudioDestroy(IntPtr handle);
 [DllImport("ProcessAudio.dll",CallingConvention=CallingConvention.Cdecl)]static extern int AudioRead(IntPtr handle,[Out]float[] data,int count,out uint serial);
 readonly object gate=new();readonly AutoResetEvent wake=new(false);readonly Thread thread;volatile int requested;volatile bool disposed;double[] values=new double[48];
 internal WindowsSpectrum(){thread=new Thread(Loop){IsBackground=true,Name="Winlestia process FFT"};thread.Start();}
 internal double[] Values{get{lock(gate)return (double[])values.Clone();}}
 internal void Start()=>SetTarget(Environment.ProcessId);
 internal void SetTarget(int pid){if(requested==pid)return;requested=pid;wake.Set();}
 internal void Stop(){SetTarget(0);lock(gate)values=new double[48];}
 void Loop(){IntPtr capture=IntPtr.Zero;int current=0;var pcm=new float[2048];var fft=new Complex[2048];uint previous=0;try{while(!disposed){
  if(current!=requested){if(capture!=IntPtr.Zero)AudioDestroy(capture);current=requested;capture=current>0?AudioCreate((uint)current):IntPtr.Zero;previous=0;}
  var next=new double[48];if(capture!=IntPtr.Zero&&AudioRead(capture,pcm,pcm.Length,out uint serial)==pcm.Length&&serial!=previous){previous=serial;for(int i=0;i<2048;i++){fft[i].X=(float)(pcm[i]*FastFourierTransform.HannWindow(i,2048));fft[i].Y=0;}FastFourierTransform.FFT(true,11,fft);
   for(int bar=0;bar<48;bar++){int lo=Math.Max(1,(int)(40*Math.Pow(400,bar/48d)*2048/44100)),hi=Math.Min(1024,(int)(40*Math.Pow(400,(bar+1)/48d)*2048/44100)+1);double peak=0;for(int bin=lo;bin<hi;bin++)peak=Math.Max(peak,Math.Sqrt(fft[bin].X*fft[bin].X+fft[bin].Y*fft[bin].Y));next[bar]=Math.Clamp((20*Math.Log10(Math.Max(1e-8,peak))+65)/55,0,1);}}
  lock(gate)for(int i=0;i<48;i++)values[i]+=(next[i]-values[i])*(next[i]>values[i]?.65:.18);
  wake.WaitOne(current>0?25:Timeout.Infinite);
 }}catch(Exception ex){Store.Log("Process FFT: "+ex.Message);}finally{if(capture!=IntPtr.Zero)AudioDestroy(capture);}}
 public void Dispose(){disposed=true;wake.Set();if(thread.Join(100))wake.Dispose();}
}
