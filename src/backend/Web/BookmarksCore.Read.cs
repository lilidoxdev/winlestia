// C# 12 / .NET 8 / Windows 11. Favoris des navigateurs : lecture Chromium et Firefox
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
    // ---- Lecture ---------------------------------------------------------------------------------------------
    public static List<BNode> Read(BrowserInfo b) => b.Chromium ? ReadChromium(b) : ReadFirefox(b);

    static string NodeId(string browser, JsonNode node)
    {
        string guid = node["guid"]?.GetValue<string>() ?? "";
        if (guid.Length > 0)
            return browser + ":" + guid;
        return browser + ":u" + Hash((node["url"]?.GetValue<string>() ?? "") + "|" + (node["name"]?.GetValue<string>() ?? ""))[..12];
    }

    static List<BNode> ReadChromium(BrowserInfo b)
    {
        var root = JsonNode.Parse(ReadShared(b.BookmarksFile))?["roots"];
        var result = new List<BNode>();
        if (root == null)
            return result;
        int budget = MaxNodes;
        List<BNode> Children(JsonNode? folder, int depth)
        {
            var list = new List<BNode>();
            if (folder?["children"] is not JsonArray array || depth > MaxDepth)
                return list;
            foreach (var child in array)
            {
                if (child == null || budget-- <= 0)
                    continue;
                string type = child["type"]?.GetValue<string>() ?? "";
                var node = new BNode { Id = NodeId(b.Id, child), Title = child["name"]?.GetValue<string>() ?? "" };
                if (type == "folder")
                {
                    node.Folder = true;
                    node.Children = Children(child, depth + 1);
                }
                else if (type == "url")
                {
                    node.Url = child["url"]?.GetValue<string>() ?? "";
                    if (node.Url.Length == 0)
                        continue;
                }
                else
                    continue;
                list.Add(node);
            }
            return list;
        }
        result.AddRange(Children(root["bookmark_bar"], 0));
        var other = Children(root["other"], 0);
        if (other.Count > 0)
            result.Add(new BNode { Id = b.Id + ":other", Title = "Autres favoris", Folder = true, Children = other });
        var mobile = Children(root["synced"], 0);
        if (mobile.Count > 0)
            result.Add(new BNode { Id = b.Id + ":synced", Title = "Favoris mobiles", Folder = true, Children = mobile });
        return result;
    }

    static List<BNode> ReadFirefox(BrowserInfo b)
    {
        string work = Path.Combine(WorkRoot, "tmp", Guid.NewGuid().ToString("N"));
        try
        {
            string? copy = CopyDatabase(b.BookmarksFile, work);
            if (copy == null)
                throw new IOException("places.sqlite illisible");
            var rows = new Dictionary<int, (int type, int parent, string title, string guid, string url)>();
            var order = new List<int>();
            using (var connection = Open(copy))
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT b.id,b.type,b.parent,COALESCE(b.title,''),b.guid,COALESCE(p.url,'') FROM moz_bookmarks b " +
                    "LEFT JOIN moz_places p ON p.id=b.fk ORDER BY b.parent,b.position";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    int id = reader.GetInt32(0);
                    rows[id] = (reader.GetInt32(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4), reader.GetString(5));
                    order.Add(id);
                }
            }
            int budget = MaxNodes;
            List<BNode> Children(int parent, int depth)
            {
                var list = new List<BNode>();
                if (depth > MaxDepth)
                    return list;
                foreach (int id in order)
                {
                    var r = rows[id];
                    if (r.parent != parent || budget-- <= 0)
                        continue;
                    if (r.type == 2)
                    {
                        list.Add(new BNode { Id = b.Id + ":" + r.guid, Title = r.title, Folder = true, Children = Children(id, depth + 1)
                            });
                    }
                    else if (r.type == 1 && r.url.Length > 0 && !r.url.StartsWith("place:", StringComparison.OrdinalIgnoreCase))
                        list.Add(new BNode { Id = b.Id + ":" + r.guid, Title = r.title, Url = r.url });
                }
                return list;
            }
            int Find(string guid) => rows.Where(kv => kv.Value.guid == guid).Select(kv => kv.Key).DefaultIfEmpty(-1).First();
            var result = new List<BNode>();
            int toolbar = Find("toolbar_____"), menu = Find("menu________"), unfiled = Find("unfiled_____");
            if (toolbar >= 0)
                result.AddRange(Children(toolbar, 0));
            var other = new List<BNode>();
            if (menu >= 0)
                other.AddRange(Children(menu, 0));
            if (unfiled >= 0)
                other.AddRange(Children(unfiled, 0));
            if (other.Count > 0)
                result.Add(new BNode { Id = b.Id + ":other", Title = "Autres favoris", Folder = true, Children = other });
            return result;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Safe.Try(() => Directory.Delete(work, true));
        }
    }
}
