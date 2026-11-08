// Injection de texte dans l'application au premier plan, extraite de DictationService.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace PanneauxGlass;

// Saisie dans l'application active : presse-papiers + Ctrl+V (texte exact, restauré), SendInput Unicode en repli.
// Cause mesurée du 1er caractère perdu / texte altéré avec SendInput dans le Bloc-notes Win11 : lot unique trop gros
// pour le RichEdit + modificateur (Alt de la touche de dictée) encore pris en compte au début du lot.
internal static class TextInjector
{
    // Laisse l'application cible lire le presse-papiers avant de le restaurer.
    const int PasteWaitMs = 1000;
    [StructLayout(LayoutKind.Sequential)]
    struct MouseInput
    {
        public int Dx, Dy; public uint Data, Flags, Time; public UIntPtr Extra;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct KeyInput
    {
        public ushort Vk, Scan; public uint Flags, Time; public UIntPtr Extra;
    }
    [StructLayout(LayoutKind.Explicit)]
    struct Input
    {
        [FieldOffset(0)] public uint Type; [FieldOffset(8)] public MouseInput Mouse; [FieldOffset(8)] public KeyInput Key;
    }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

    const uint KeyUp = 2, Unicode = 4; const ushort VkReturn = 0x0D, VkControl = 0x11, VkV = 0x56;
    static readonly int[] Modifiers = { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5 };

    static Input Key(ushort vk, ushort scan, uint flags) => new() { Type = 1, Key = new KeyInput { Vk = vk, Scan = scan, Flags = flags } };

    // Attend (max 2 s) qu'aucun Shift/Ctrl/Alt(AltGr)/Win ne soit enfoncé, puis laisse ~40 ms au système.
    static void WaitModifiersReleased()
    {
        for (int i = 0; i < 100; i++)
        {
            bool down = false;
            foreach (int vk in Modifiers)
                if ((GetAsyncKeyState(vk) & 0x8000) != 0)
                {
                    down = true;
                    break;
                }
            if (!down)
                break;
            Thread.Sleep(20);
        }
        Thread.Sleep(40);
    }

    internal static bool Type(string text)
    {
        if (text.Length == 0)
            return true;
        WaitModifiersReleased();
        if (Paste(text))
            return true;
        return TypeKeys(text);
    }

    // Repli : 1 caractère UTF-16 (down+up) par appel, léger rythme ; les surrogates partent en paire consécutive.
    static bool TypeKeys(string text)
    {
        if (Marshal.SizeOf<Input>() != 40)
            return false;
        bool any = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
                continue;
            Input[] batch;
            if (c == '\n')
                batch = new[] { Key(VkReturn, 0, 0), Key(VkReturn, 0, KeyUp) };
            else if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                char d = text[++i];
                batch = new[] { Key(0, c, Unicode), Key(0, d, Unicode), Key(0, c, Unicode | KeyUp), Key(0, d, Unicode | KeyUp) };
            }
            else
                batch = new[] { Key(0, c, Unicode), Key(0, c, Unicode | KeyUp) };
            if (SendInput((uint)batch.Length, batch, Marshal.SizeOf<Input>()) != batch.Length)
                return any;
            any = true;
            Thread.Sleep(4);
        }
        return any;
    }

    static bool Paste(string text)
    {
        bool ok = false;
        var thread = new Thread(() =>
        {
            System.Windows.Forms.IDataObject? previous = null;
            System.Windows.Forms.DataObject? backup = null;
            try
            {
                try
                {
                    previous = System.Windows.Forms.Clipboard.GetDataObject();
                    if (previous != null)
                    {
                        backup = new System.Windows.Forms.DataObject();
                        foreach (string format in previous.GetFormats(false))
                        {
                            var data = previous.GetData(format, false);
                            if (data != null)
                                backup.SetData(format, false, data);
                        }
                    }
                }
                catch { backup = null; }
                if (!SetText(text))
                    return;
                var paste = new[] { Key(VkControl, 0, 0), Key(VkV, 0, 0), Key(VkV, 0, KeyUp), Key(VkControl, 0, KeyUp) };
                ok = SendInput((uint)paste.Length, paste, Marshal.SizeOf<Input>()) == paste.Length;
                Thread.Sleep(PasteWaitMs);
                try
                {
                    if (backup != null)
                        System.Windows.Forms.Clipboard.SetDataObject(backup, true, 5, 20);
                    else
                        System.Windows.Forms.Clipboard.Clear();
                }
                catch (Exception ex)
                {
                    Store.Log("DictationService.Paste : " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            catch (Exception ex) { Store.Log("Dictée: presse-papiers: " + ex.Message); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join(5000);
        return ok;
    }

    static bool SetText(string text)
    {
        for (int i = 0; i < 5; i++)
        {
            try
            {
                System.Windows.Forms.Clipboard.SetDataObject(text, true, 5, 20);
                return true;
            }
            catch { Thread.Sleep(30); }
        }
        return false;
    }
}
