// C# 12 / .NET 8 / Windows 11. GPL-3.0-or-later. Court carillon à deux notes synthétisé en mémoire (aucun fichier).

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace PanneauxGlass;

internal static class SocialChime {

    const int Rate=44100;
    static int playing;static long lastTicks;

    // Deux sinusoïdes (La5 puis Mi6), attaque courte et décroissance douce, ~250 ms, mono 16 bits.
    static byte[] Render(double volume){
        int half=Rate/8,total=half*2;var bytes=new byte[total*2];double amp=0.55*Math.Clamp(volume,0,1);
        for(int i=0;i<total;i++){
            bool second=i>=half;int k=second?i-half:i;double t=k/(double)Rate,freq=second?1318.51:880.0;
            double attack=Math.Min(1,t/0.006),decay=Math.Exp(-t*18),tone=Math.Sin(2*Math.PI*freq*t)+0.25*Math.Sin(4*Math.PI*freq*t);
            short sample=(short)Math.Clamp(tone*attack*decay*amp*short.MaxValue/1.25,short.MinValue,short.MaxValue);
            bytes[i*2]=(byte)(sample&0xFF);bytes[i*2+1]=(byte)((sample>>8)&0xFF);
        }
        return bytes;
    }

    // Hors thread UI. Un seul son à la fois et jamais plus d'un toutes les 350 ms : pas de pile de sorties audio.
    internal static void Play(int volumePercent){
        if(volumePercent<=0)return;
        long now=Environment.TickCount64;if(now-Interlocked.Read(ref lastTicks)<350)return;
        if(Interlocked.Exchange(ref playing,1)==1)return;
        Interlocked.Exchange(ref lastTicks,now);
        _=Task.Run(()=>{
            try{
                using var stream=new MemoryStream(Render(volumePercent/100.0));
                using var source=new RawSourceWaveStream(stream,new WaveFormat(Rate,16,1));
                using var output=new WaveOutEvent{DesiredLatency=100};
                using var done=new ManualResetEventSlim(false);
                output.PlaybackStopped+=(_,_)=>done.Set();
                output.Init(source);output.Play();
                done.Wait(2000);
            }catch(Exception ex){Store.Log("Social son: "+ex.Message);}
            finally{Interlocked.Exchange(ref playing,0);}
        });
    }
}
