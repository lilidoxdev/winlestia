// C# 12 / .NET 8 / Windows 11. Favoris des navigateurs : suppression dans le navigateur (Chromium)
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
    // ---- Suppression dans le navigateur (Chromium seulement) -------------------------------------------------
    // Renvoie null si réussi, sinon un code : unsupported, running, sync, notfound, error.
    static int backupCounter;
    public static string? DeleteChromium(BrowserInfo b, string id, bool confirmSync, string backupsDir, string? bookmarksOverride = null,
        bool skipChecks = false)
    {
        if (!b.Chromium)
            return "unsupported";
        string file = bookmarksOverride ?? b.BookmarksFile;
        try
        {
            if (!skipChecks)
            {
                if (IsRunning(b))
                    return "running";
                if (!confirmSync && SyncActive(b))
                    return "sync";
            }
            if (!id.StartsWith(b.Id + ":", StringComparison.Ordinal) || id.EndsWith(":other") || id.EndsWith(":synced"))
                return "notfound";
            var root = JsonNode.Parse(ReadShared(file));
            if (root?["roots"] is not JsonObject roots)
                return "error";
            int matches = 0;
            bool hasGuid = false;
            void Count(JsonNode? folder)
            {
                if (folder?["children"] is not JsonArray array)
                    return;
                foreach (var child in array)
                {
                    if (child == null)
                        continue;
                    if (NodeId(b.Id, child) == id)
                    {
                        matches++;
                        if (child["guid"] is JsonValue g && g.TryGetValue<string>(out var gs) && gs.Length > 0)
                            hasGuid = true;
                    }
                    Count(child);
                }
            }
            foreach (var kv in roots)
                Count(kv.Value);
            if (matches == 0)
                return "notfound";
            if (matches > 1 || !hasGuid)
                return "ambiguous";
            Directory.CreateDirectory(backupsDir);
            string backup = Path.Combine(backupsDir, b.Id + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmssfff") + "-"
                + Interlocked.Increment(ref backupCounter) + ".json");
            File.Copy(file, backup, false);
            try
            {
                var old = new DirectoryInfo(backupsDir).GetFiles(b.Id + "-*.json").OrderByDescending(f
                    => f.LastWriteTimeUtc).ThenByDescending(f => f.Name, StringComparer.Ordinal).Skip(20);
                foreach (var f in old)
                {
                    Safe.Try(() => f.Delete());
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Store.Log("BookmarksCore.DeleteChromium : " + ex.GetType().Name + ": " + ex.Message);
            }
            bool removed = false;
            bool Remove(JsonNode? folder)
            {
                if (folder?["children"] is not JsonArray array)
                    return false;
                for (int i = 0; i < array.Count; i++)
                {
                    var child = array[i];
                    if (child == null)
                        continue;
                    if (NodeId(b.Id, child) == id)
                    {
                        array.RemoveAt(i);
                        return true;
                    }
                    if (Remove(child))
                        return true;
                }
                return false;
            }
            foreach (var kv in roots)
                if (Remove(kv.Value))
                {
                    removed = true;
                    break;
                }
            if (!removed)
                return "notfound";
            if (root is JsonObject obj)
                obj.Remove("checksum");
            string temp = file + ".winlestia.tmp";
            File.WriteAllText(temp, root.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }), new UTF8Encoding(false));
            try
            {
                File.Replace(temp, file, null);
            }
            catch { Safe.Try(() => File.Delete(temp)); throw; }
            return null;
        }
        catch (Exception ex) { Log("Favoris suppression : " + ex.GetType().Name); return "error"; }
    }

    internal static bool SyncActive(BrowserInfo b)
    {
        try
        {
            string prefs = Path.Combine(b.ProfileDir, "Preferences");
            if (!File.Exists(prefs))
                return false;
            var root = JsonNode.Parse(ReadShared(prefs));
            if (root?["account_info"] is JsonArray accounts && accounts.Count > 0)
                return true;
            var sync = root?["sync"];
            if (sync is JsonObject s)
                foreach (var key in new[] { "has_setup_completed", "requested", "keep_everything_synced" })
                    if (s[key] is JsonValue v && v.TryGetValue<bool>(out var on) && on)
                        return true;
        }
        catch { return true; } // illisible : par prudence, on demande confirmation
        return false;
    }
}
