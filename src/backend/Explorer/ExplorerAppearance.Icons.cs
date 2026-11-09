// ExplorerAppearance : génération des icônes de dossier natives.

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Security.Cryptography;
using System.Windows.Threading;
using D = System.Drawing;
using G = System.Drawing.Drawing2D;

namespace PanneauxGlass;

internal sealed partial class ExplorerAppearance
{
    internal static void DrawIcon(string path, string hex, string kind)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var images = new List<byte[]>();
        int[] sizes = { 256, 128, 64, 48, 32, 16 };
        foreach (int size in sizes)
        {
            using var bitmap = new D.Bitmap(size, size);
            using var g = D.Graphics.FromImage(bitmap);
            g.SmoothingMode = G.SmoothingMode.AntiAlias;
            g.ScaleTransform(size / 256f, size / 256f);
            var c = D.ColorTranslator.FromHtml(hex);
            D.Color Mix(int white)
                => D.Color.FromArgb((c.R * (255 - white) + 255 * white) / 255,
                                    (c.G * (255 - white) + 255 * white) / 255,
                                    (c.B * (255 - white) + 255 * white) / 255);
            using var shadow = new D.SolidBrush(D.Color.FromArgb(30, 0, 0, 0));
            g.FillRectangle(shadow, 28, 73, 204, 151);
            using var back = new G.GraphicsPath();
            back.AddLines(new[]
            {
                new D.PointF(23, 81),
                new D.PointF(23, 58),
                new D.PointF(33, 46),
                new D.PointF(99, 46),
                new D.PointF(121, 66),
                new D.PointF(222, 66),
                new D.PointF(234, 80),
                new D.PointF(234, 199),
                new D.PointF(222, 212),
                new D.PointF(34, 212)
            });
            back.CloseFigure();
            using var backFill = new D.SolidBrush(Mix(75));
            g.FillPath(backFill, back);
            using var paper = new D.SolidBrush(D.Color.FromArgb(220, 255, 255, 255));
            g.FillRectangle(paper, 44, 77, 169, 34);
            using var front = new G.GraphicsPath();
            front.AddArc(18, 89, 28, 28, 180, 90);
            front.AddArc(210, 89, 28, 28, 270, 90);
            front.AddArc(210, 191, 28, 28, 0, 90);
            front.AddArc(18, 191, 28, 28, 90, 90);
            front.CloseFigure();
            using var fill = new G.LinearGradientBrush(new D.Rectangle(18, 89, 220, 130), Mix(38), c, 90);
            g.FillPath(fill, front);
            using var edge = new D.Pen(D.Color.FromArgb(85, 255, 255, 255), 2);
            g.DrawPath(edge, front);
            double luminance = .2126 * c.R + .7152 * c.G + .0722 * c.B;
            using var pen =
                new D.Pen(luminance > 170 ? D.Color.FromArgb(170, 25, 30, 35) : D.Color.FromArgb(230, 255, 255, 255),
                          7)
                {
                    StartCap = G.LineCap.Round,
                    EndCap = G.LineCap.Round,
                    LineJoin = G.LineJoin.Round
                };
            switch (kind)
            {
                case "download":
                    g.DrawLine(pen, 128, 125, 128, 175);
                    g.DrawLines(pen, new[] { new D.Point(109, 157), new D.Point(128, 176), new D.Point(147, 157) });
                    g.DrawLine(pen, 107, 190, 149, 190);
                    break;
                case "document":
                    g.DrawRectangle(pen, 108, 123, 42, 63);
                    g.DrawLine(pen, 118, 143, 140, 143);
                    g.DrawLine(pen, 118, 158, 140, 158);
                    break;
                case "picture":
                    g.DrawRectangle(pen, 96, 128, 66, 48);
                    g.DrawLines(pen,
                                new[] { new D.Point(101, 169), new D.Point(120, 149), new D.Point(134, 163), new D.Point(145, 151),
                                    new D.Point(157, 169) });
                    break;
                case "music":
                    g.DrawLines(pen, new[] { new D.Point(116, 174), new D.Point(116, 130), new D.Point(147, 123), new D.Point(147, 165) });
                    g.DrawEllipse(pen, 101, 171, 15, 9);
                    g.DrawEllipse(pen, 132, 163, 15, 9);
                    break;
                case "video":
                    g.DrawPolygon(pen, new[] { new D.Point(112, 126), new D.Point(151, 153), new D.Point(112, 180) });
                    break;
                case "desktop":
                    g.DrawRectangle(pen, 96, 127, 65, 43);
                    g.DrawLine(pen, 128, 173, 128, 188);
                    g.DrawLine(pen, 111, 190, 145, 190);
                    break;
                default:
                    g.DrawLine(pen, 110, 152, 146, 152);
                    g.DrawLine(pen, 128, 134, 128, 170);
                    break;
            }
            using var memory = new MemoryStream();
            if (size == 256)
                bitmap.Save(memory, D.Imaging.ImageFormat.Png);
            else
            {
                using var dib = new BinaryWriter(memory, System.Text.Encoding.UTF8, true);
                int maskStride = ((size + 31) / 32) * 4;
                dib.Write(40);
                dib.Write(size);
                dib.Write(size * 2);
                dib.Write((ushort)1);
                dib.Write((ushort)32);
                dib.Write(0);
                dib.Write(size * size * 4 + maskStride * size);
                for (int i = 0; i < 4; i++)
                    dib.Write(0);
                for (int y = size - 1; y >= 0; y--)
                    for (int x = 0; x < size; x++)
                    {
                        var pixel = bitmap.GetPixel(x, y);
                        dib.Write(pixel.B);
                        dib.Write(pixel.G);
                        dib.Write(pixel.R);
                        dib.Write(pixel.A);
                    }
                for (int y = size - 1; y >= 0; y--)
                {
                    var mask = new byte[maskStride];
                    for (int x = 0; x < size; x++)
                        if (bitmap.GetPixel(x, y).A == 0)
                            mask[x / 8] |= (byte)(128 >> (x % 8));
                    dib.Write(mask);
                }
            }
            images.Add(memory.ToArray());
        }
        using var output = File.Create(path);
        using var writer = new BinaryWriter(output);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)sizes.Length);
        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            writer.Write((byte)(sizes[i] % 256));
            writer.Write((byte)(sizes[i] % 256));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(images[i].Length);
            writer.Write(offset);
            offset += images[i].Length;
        }
        foreach (var image in images)
            writer.Write(image);
    }
}
