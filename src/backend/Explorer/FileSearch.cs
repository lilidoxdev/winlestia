// C# 12 / FileSearch.cs / Windows Search index queries for the command palette (">file "). Late-bound ADODB, no NuGet. Never logs the typed text or a path.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PanneauxGlass.Explorer;

internal sealed record FileHit(string Name, string Path);

internal sealed record FileSnapshot(long Generation, string Status, IReadOnlyList<FileHit> Items);

// Pure part: input normalisation, LIKE escaping, SQL text. Covered by tests/FileSearch.
internal static class FileQuery
{
    internal const int MinChars = 2,
        MaxChars = 64,
        MaxRows = 20;
    internal const string ConnectionString = "Provider=Search.CollatorDSO;Extended Properties='Application=Windows'";

    // Trims, drops control characters, collapses blanks. False when shorter than MinChars or longer than MaxChars.
    internal static bool TryNormalize(string? text, out string term)
    {
        term = "";
        if (text == null)
            return false;
        var sb = new StringBuilder();
        bool space = false;
        foreach (char c in text)
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c))
            {
                space = sb.Length > 0;
                continue;
            }
            if (space)
            {
                sb.Append(' ');
                space = false;
            }
            sb.Append(c);
            if (sb.Length > MaxChars)
                return false;
        }
        term = sb.ToString();
        return term.Length >= MinChars;
    }

    // The term goes into CONTAINS(col,'"term*"'). LIKE with a leading % scans the whole scope (4 s without a hit), CONTAINS answers from the index in tens of ms,
    // at the price of matching word beginnings. Characters with a meaning for the query ( " * % _ [ ] ) become blanks; a single quote is doubled afterwards.
    internal static string NeutraliseContains(string term)
    {
        var sb = new StringBuilder(term.Length);
        foreach (char c in term)
            sb.Append(c is '"' or '*' or '%' or '_' or '[' or ']' ? ' ' : c);
        return sb.ToString();
    }

    internal static string EscapeLiteral(string text) => text.Replace("'", "''");

    static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe",
        ".bat",
        ".cmd",
        ".com",
        ".ps1",
        ".psm1",
        ".vbs",
        ".vbe",
        ".js",
        ".jse",
        ".wsf",
        ".wsh",
        ".msi",
        ".msp",
        ".scr",
        ".lnk",
        ".url",
        ".hta",
        ".pif",
        ".reg",
        ".cpl",
        ".jar",
        ".appref-ms",
    };

    // True when opening the file could run code: such a result is only revealed in Explorer, never launched.
    internal static bool IsExecutable(string path) => ExecutableExtensions.Contains(Path.GetExtension(path ?? ""));

    // The scope is the user's profile folder only. False for an empty or malformed term, or an unusable profile path.
    internal static bool TryBuild(string? text, string? profile, out string sql)
    {
        sql = "";
        if (!TryNormalize(text, out var typed) || !TryNormalize(NeutraliseContains(typed), out var term))
            return false;
        if (string.IsNullOrWhiteSpace(profile) || profile.IndexOf((char)0) >= 0 || !Path.IsPathFullyQualified(profile))
            return false;
        sql =
            "SELECT TOP "
            + MaxRows
            + " System.ItemPathDisplay, System.ItemNameDisplay FROM SystemIndex WHERE SCOPE='file:"
            + EscapeLiteral(profile.TrimEnd('\\', '/'))
            + "' AND CONTAINS(System.ItemNameDisplay,'\""
            + EscapeLiteral(term)
            + "*\"')";
        return true;
    }
}

internal sealed class FileSearch : IDisposable
{
    internal const string Ok = "ok",
        Unavailable = "unavailable",
        Timeout = "timeout";
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(2);
    readonly object gate = new();
    CancellationTokenSource? current;
    long latest;
    FileSnapshot? snapshot;
    bool disposed;

    static string Profile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // Starts a query on a worker thread. A newer generation cancels the older one; an older generation is ignored.
    // `done` is called from the worker only when this query is still the latest one and its result was stored.
    internal void Start(string? term, long generation, Action done)
    {
        var cts = new CancellationTokenSource();
        lock (gate)
        {
            if (disposed || generation < latest)
            {
                cts.Dispose();
                return;
            }
            latest = generation;
            current?.Cancel();
            current = cts;
            snapshot = null;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                FileSnapshot result;
                try
                {
                    result = await QueryAsync(term, generation, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                lock (gate)
                {
                    if (disposed || cts.IsCancellationRequested || generation != latest)
                        return;
                    snapshot = result;
                }
                done();
            }
            finally
            {
                // Disposed only here, once the query is over; `current` never points to a disposed source, so Cancel() cannot hit it.
                lock (gate)
                {
                    if (ReferenceEquals(current, cts))
                        current = null;
                }
                cts.Dispose();
            }
        });
    }

    // One-off query (command line): nothing is kept.
    internal static Task<FileSnapshot> RunOnceAsync(string? term, CancellationToken token) =>
        QueryAsync(term, 0, token);

    static async Task<FileSnapshot> QueryAsync(string? term, long generation, CancellationToken token)
    {
        if (!FileQuery.TryBuild(term, Profile, out var sql))
            return new(generation, Ok, Array.Empty<FileHit>());
        try
        {
            return await Task.Run(() => Execute(sql, generation), token).WaitAsync(Limit, token);
        }
        catch (TimeoutException)
        {
            return new(generation, Timeout, Array.Empty<FileHit>());
        }
    }

    static FileSnapshot Execute(string sql, long generation)
    {
        object? connection = null,
            rows = null;
        try
        {
            var connectionType = Type.GetTypeFromProgID("ADODB.Connection");
            var rowsType = Type.GetTypeFromProgID("ADODB.Recordset");
            if (connectionType == null || rowsType == null)
                return new(generation, Unavailable, Array.Empty<FileHit>());
            dynamic c = Activator.CreateInstance(connectionType)!;
            connection = c;
            c.ConnectionTimeout = 2;
            c.CommandTimeout = 2;
            c.Open(FileQuery.ConnectionString);
            dynamic r = Activator.CreateInstance(rowsType)!;
            rows = r;
            r.Open(sql, c);
            var items = new List<FileHit>();
            while (!(bool)r.EOF && items.Count < FileQuery.MaxRows)
            {
                string path = Convert.ToString((object?)r.Fields.Item(0).Value) ?? "";
                string name = Convert.ToString((object?)r.Fields.Item(1).Value) ?? "";
                if (path.Length > 0)
                    items.Add(new(name.Length > 0 ? name : Path.GetFileName(path), path));
                r.MoveNext();
            }
            r.Close();
            c.Close();
            return new(generation, Ok, items);
        }
        catch
        {
            return new(generation, Unavailable, Array.Empty<FileHit>());
        }
        finally
        {
            if (rows != null && Marshal.IsComObject(rows))
                Marshal.FinalReleaseComObject(rows);
            if (connection != null && Marshal.IsComObject(connection))
                Marshal.FinalReleaseComObject(connection);
        }
    }

    // What the palette displays. The frontend only gets an index back for opening, never a path to trust.
    internal object? Payload()
    {
        lock (gate)
        {
            if (snapshot == null)
                return null;
            return new
            {
                gen = snapshot.Generation,
                status = snapshot.Status,
                items = snapshot
                    .Items.Select(
                        (h, i) =>
                            new
                            {
                                index = i,
                                name = h.Name,
                                path = h.Path,
                            }
                    )
                    .ToArray(),
            };
        }
    }

    // A path is only released for a result of the last stored query.
    internal bool TryResolve(long generation, int index, out string path)
    {
        lock (gate)
        {
            path = "";
            if (snapshot == null || snapshot.Generation != generation || index < 0 || index >= snapshot.Items.Count)
                return false;
            path = snapshot.Items[index].Path;
            return true;
        }
    }

    internal void Cancel()
    {
        lock (gate)
        {
            current?.Cancel();
            current = null;
            snapshot = null;
            latest = 0;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            current?.Cancel();
            current = null;
            snapshot = null;
        }
    }
}
