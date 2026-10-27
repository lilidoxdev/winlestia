// language: C# 12, file: FocusAlarm.cs, target: Windows 11 / .NET 8 WPF, synthesized focus chime
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;

namespace PanneauxGlass;
// A rising three-note bell, written once as a 16-bit PCM wave and played by MediaPlayer.
// No audio file to ship, no notification sound borrowed from Windows.
internal static class FocusAlarm {
    [DllImport("user32.dll")]static extern bool MessageBeep(uint kind);
    static readonly object gate=new();static MediaPlayer? player;
    internal static void Play(){
        lock(gate){
            try{
                string path=EnsureFile();
                player?.Close();
                player=new MediaPlayer{Volume=.92};
                player.MediaFailed+=(_,_)=>{player?.Close();MessageBeep(0x40);};
                player.Open(new Uri(path));
                player.Play();
            }catch(Exception ex){Store.Log("Alarme focus: "+ex.Message);MessageBeep(0x40);}
        }
    }
    internal static string EnsureFile(){
        string path=Path.Combine(Store.Root,"focus-alarm.wav");
        if(!File.Exists(path)||new FileInfo(path).Length<40000)File.WriteAllBytes(path,Build());
        return path;
    }
    static byte[] Build(){
        const int rate=44100;const double total=2.0;
        int frames=(int)(rate*total);var mix=new double[frames];
        double[] notes={1046.50,1318.51,1567.98};
        for(int strike=0;strike<notes.Length;strike++){
            int offset=(int)(strike*.5*rate);double frequency=notes[strike];
            for(int i=0;offset+i<frames;i++){
                double t=i/(double)rate;if(t>1.35)break;
                double attack=Math.Min(1,t/.006),decay=Math.Exp(-4.4*t);
                mix[offset+i]+=.30*attack*decay*(
                    Math.Sin(2*Math.PI*frequency*t)+
                    .33*Math.Sin(4*Math.PI*frequency*t)+
                    .12*Math.Sin(6*Math.PI*frequency*t));
            }
        }
        int echo=(int)(.13*rate);
        for(int i=echo;i<frames;i++)mix[i]+=mix[i-echo]*.24;
        using var stream=new MemoryStream(44+frames*2);
        using(var writer=new BinaryWriter(stream,System.Text.Encoding.ASCII,true)){
            writer.Write("RIFF".ToCharArray());writer.Write(36+frames*2);writer.Write("WAVE".ToCharArray());
            writer.Write("fmt ".ToCharArray());writer.Write(16);writer.Write((short)1);writer.Write((short)1);
            writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);
            writer.Write("data".ToCharArray());writer.Write(frames*2);
            for(int i=0;i<frames;i++){
                double value=Math.Clamp(mix[i],-1,1);
                writer.Write((short)Math.Round(value*32767));
            }
        }
        return stream.ToArray();
    }
}
