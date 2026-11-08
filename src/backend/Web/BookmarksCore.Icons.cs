// C# 12 / .NET 8 / Windows 11. Favoris des navigateurs : cache disque des favicons
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
namespace PanneauxGlass;

internal static partial class BookmarksCore
{
    // ---- Favicons (cache disque 32 px, aucune requête réseau) --------------------------------------------------
    internal static string IconsDir => Path.Combine(WorkRoot, "icons");
    static readonly HashSet<string> NoIcon = new(); // hôtes sans icône trouvée : on ne recopie pas la base à chaque scan

    internal static string HostOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
            return "";
        return u.Host.ToLowerInvariant();
    }

    internal static void Collect(IEnumerable<BNode> nodes, Dictionary<string, string> hosts)
    {
        foreach (var n in nodes)
        {
            if (n.Folder)
            {
                Collect(n.Children, hosts);
                continue;
            }
            string host = HostOf(n.Url);
            if (host.Length > 0 && !hosts.ContainsKey(host))
                hosts[host] = n.Url;
        }
    }

    static void Assign(IEnumerable<BNode> nodes)
    {
        foreach (var n in nodes)
        {
            if (n.Folder)
            {
                Assign(n.Children);
                continue;
            }
            string host = HostOf(n.Url);
            if (host.Length == 0)
                continue;
            string file = Path.Combine(IconsDir, Hash(host) + ".png");
            if (File.Exists(file))
                n.Icon = new Uri(file).AbsoluteUri;
        }
    }

    public static int BuildIcons(BrowserInfo b, List<BNode> items)
    {
        var hosts = new Dictionary<string, string>();
        Collect(items, hosts);
        Directory.CreateDirectory(IconsDir);
        var missing = hosts.Where(kv => !File.Exists(Path.Combine(IconsDir, Hash(kv.Key) + ".png")) && !NoIcon.Contains(kv.Key)).ToList();
        int created = 0;
        if (missing.Count > 0)
        {
            string work = Path.Combine(WorkRoot, "tmp", Guid.NewGuid().ToString("N"));
            try
            {
                string source = b.Chromium ? Path.Combine(b.ProfileDir, "Favicons") : Path.Combine(b.ProfileDir, "favicons.sqlite");
                string? copy = CopyDatabase(source, work);
                if (copy != null)
                {
                    using var connection = Open(copy);
                    foreach (var (host, url) in missing)
                    {
                        byte[]? data = Query(connection, b.Chromium, url, host);
                        byte[]? png = data == null ? null : ToPng32(data);
                        if (png == null)
                        {
                            NoIcon.Add(host);
                            continue;
                        }
                        try
                        {
                            File.WriteAllBytes(Path.Combine(IconsDir, Hash(host) + ".png"), png);
                            created++;
                        }
                        catch (Exception)
                        {
                            // Favicon illisible ou non enregistrable : icône de repli, sans journal par site.
                        }
                    }
                }
            }
            catch (Exception ex) { Log("Favoris icônes : " + ex.GetType().Name); }
            finally { SqliteConnection.ClearAllPools(); Safe.Try(() => Directory.Delete(work, true)); }
        }
        Assign(items);
        return created;
    }

    static void Log(string text)
    {
        try
        {
            Store.Log(text);
        }
        catch (Exception)
        {
            // Le journal ne peut pas se journaliser lui-même.
        }
    }

    internal static byte[]? Query(SqliteConnection connection, bool chromium, string url, string host)
    {
        string select = chromium
            ? "SELECT b.image_data FROM icon_mapping m JOIN favicon_bitmaps b ON b.icon_id=m.icon_id WHERE m.page_url{0} AND " +
                "b.image_data IS NOT NULL ORDER BY ABS(b.width-32) LIMIT 1"
            : "SELECT i.data FROM moz_pages_w_icons p JOIN moz_icons_to_pages ip ON ip.page_id=p.id JOIN moz_icons i ON i.id=ip.icon_id " +
                "WHERE p.page_url{0} AND i.data IS NOT NULL AND i.width<1024 ORDER BY ABS(i.width-32) LIMIT 1";
        foreach (var (clause, value) in new[] { ("=$u", url), (" LIKE $u", "https://" + host + "/%"), (" LIKE $u", "http://" + host + "/%")
            })
        {
            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = string.Format(select, clause);
                command.Parameters.AddWithValue("$u", value);
                if (command.ExecuteScalar() is byte[] data && data.Length > 0)
                    return data;
            }
            catch { return null; }
        }
        return null;
    }

    internal static byte[]? ToPng32(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data);
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream,
                System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.OrderBy(f => Math.Abs(f.PixelWidth - 32)).FirstOrDefault();
            if (frame == null || frame.PixelWidth < 8 || frame.PixelHeight < 8 || frame.PixelWidth > 2048)
                return null;
            double scale = 32.0 / Math.Max(frame.PixelWidth, frame.PixelHeight);
            System.Windows.Media.Imaging.BitmapSource source = scale == 1 ? frame
                : new System.Windows.Media.Imaging.TransformedBitmap(frame, new System.Windows.Media.ScaleTransform(scale, scale));
            source = new System.Windows.Media.Imaging.FormatConvertedBitmap(source, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            source.Freeze();
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }
        catch { return null; }
    }
}
