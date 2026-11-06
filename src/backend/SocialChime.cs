// C# 12 / .NET 8 / Windows 11. GPL-3.0-or-later. Signature sonore Winlestia synthétisée en mémoire (aucun fichier).

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace PanneauxGlass;

internal static class SocialChime {

    const int Rate=44100;
    static int playing;static long lastTicks;
    static readonly Lazy<float[]> Signature=new(Compose);

    // Arpège montant Sol5 → Ré6 → Sol6 (quinte + octave, ouvert et lumineux) avec un Si6 en scintillement.
    // Timbre « verre » : FM 2:1 dont l'indice retombe vite (attaque cristalline, queue pure), un léger désaccord
    // gauche/droite pour la largeur, chaque note posée un peu plus à droite, puis un écho ping-pong discret.
    // Stéréo entrelacée, crête normalisée à 0,9 ; le volume est appliqué à la lecture.
    static float[] Compose(){
        const double length=1.05;int frames=(int)(Rate*length);var left=new double[frames];var right=new double[frames];
        (double start,double freq,double gain,double decay,double pan)[] notes={
            (0.000, 783.99,0.55,9.0,-0.35),
            (0.072,1174.66,0.60,8.0, 0.00),
            (0.144,1567.98,0.85,4.6, 0.25),
            (0.150,1975.53,0.22,6.5, 0.45),
        };
        foreach(var n in notes){
            int offset=(int)(n.start*Rate);double l=Math.Sqrt((1-n.pan)/2),r=Math.Sqrt((1+n.pan)/2);
            for(int i=0;offset+i<frames;i++){
                double t=i/(double)Rate,env=Math.Min(1,t/0.004)*Math.Exp(-n.decay*t);if(env<1e-4&&t>0.05)break;
                double index=1.8*Math.Exp(-16*t);
                double voiceL=Math.Sin(2*Math.PI*n.freq*0.9985*t+index*Math.Sin(2*Math.PI*2*n.freq*t));
                double voiceR=Math.Sin(2*Math.PI*n.freq*1.0015*t+index*Math.Sin(2*Math.PI*2*n.freq*t));
                double body=0.18*Math.Sin(Math.PI*n.freq*t)*Math.Exp(-9*t);
                left[offset+i]+=n.gain*env*(voiceL+body)*l;right[offset+i]+=n.gain*env*(voiceR+body)*r;
            }
        }
        int delay=(int)(0.118*Rate);
        for(int i=frames-1;i>=delay;i--){left[i]+=0.20*right[i-delay];right[i]+=0.20*left[i-delay];}
        double peak=1e-9;for(int i=0;i<frames;i++)peak=Math.Max(peak,Math.Max(Math.Abs(left[i]),Math.Abs(right[i])));
        // Fondu de sortie sur les 120 dernières ms : aucune coupure audible.
        int fade=(int)(0.12*Rate);var mix=new float[frames*2];
        for(int i=0;i<frames;i++){
            double tail=i>frames-fade?(frames-i)/(double)fade:1,scale=0.9/peak*tail;
            mix[i*2]=(float)(left[i]*scale);mix[i*2+1]=(float)(right[i]*scale);
        }
        return mix;
    }

    static byte[] Render(double volume){
        var mix=Signature.Value;var bytes=new byte[mix.Length*2];double amp=Math.Clamp(volume,0,1);amp*=amp;
        for(int i=0;i<mix.Length;i++){
            short sample=(short)Math.Clamp(Math.Round(mix[i]*amp*short.MaxValue),short.MinValue,short.MaxValue);
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
                using var source=new RawSourceWaveStream(stream,new WaveFormat(Rate,16,2));
                using var output=new WaveOutEvent{DesiredLatency=100};
                using var done=new ManualResetEventSlim(false);
                output.PlaybackStopped+=(_,_)=>done.Set();
                output.Init(source);output.Play();
                done.Wait(2500);
            }catch(Exception ex){Store.Log("Social son: "+ex.Message);}
            finally{Interlocked.Exchange(ref playing,0);}
        });
    }
}
