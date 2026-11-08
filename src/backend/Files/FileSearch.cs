// C# 12 / FileSearch.cs / Requêtes sur l'index Windows Search pour la palette de commandes (« >file »). ADODB en liaison tardive, sans
// NuGet. Ne journalise jamais le texte saisi ni un chemin.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PanneauxGlass;

internal sealed record FileHit(string Name, string Path);
internal sealed record FileSnapshot(long Generation, string Status, IReadOnlyList<FileHit> Items);

// Partie pure : normalisation de l'entrée, échappement LIKE, texte SQL. Couverte par tests/FileSearch.
internal static class FileQuery
{
    internal const int MinChars = 2, MaxChars = 64, MaxRows = 20;
    internal const string ConnectionString = "Provider=Search.CollatorDSO;Extended Properties='Application=Windows'";

    // Rogne, retire les caractères de contrôle, réduit les blancs. Faux si plus court que MinChars ou plus long que MaxChars.
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

    // Le terme va dans CONTAINS(col,'"terme*"'). LIKE avec un % initial parcourt toute la portée (4 s sans résultat), CONTAINS répond
    // depuis l'index en quelques dizaines de ms,
    // au prix de ne trouver que les débuts de mots. Les caractères porteurs de sens pour la requête ( " * % _ [ ] ) deviennent des
    // blancs ; une apostrophe est doublée ensuite.
    internal static string NeutraliseContains(string term)
    {
        var sb = new StringBuilder(term.Length);
        foreach (char c in term)
            sb.Append(c is '"' or '*' or '%' or '_' or '[' or ']' ? ' ' : c);
        return sb.ToString();
    }

    internal static string EscapeLiteral(string text) => text.Replace("'", "''");

    static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase) {
        ".exe", ".bat", ".cmd", ".com", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".msi", ".msp", ".scr", ".lnk",
            ".url", ".hta", ".pif", ".reg", ".cpl", ".jar", ".appref-ms" };

    // Vrai quand l'ouverture du fichier pourrait exécuter du code : un tel résultat n'est que révélé dans l'Explorateur, jamais
    // lancé.
    internal static bool IsExecutable(string path) => ExecutableExtensions.Contains(Path.GetExtension(path ?? ""));

    // La portée est uniquement le dossier du profil de l'utilisateur. Faux pour un terme vide ou mal formé, ou un chemin de profil
    // inutilisable.
    internal static bool TryBuild(string? text, string? profile, out string sql)
    {
        sql = "";
        if (!TryNormalize(text, out var typed) || !TryNormalize(NeutraliseContains(typed), out var term))
            return false;
        if (string.IsNullOrWhiteSpace(profile) || profile.IndexOf((char)0) >= 0 || !Path.IsPathFullyQualified(profile))
            return false;
        sql = "SELECT TOP " + MaxRows + " System.ItemPathDisplay, System.ItemNameDisplay FROM SystemIndex WHERE SCOPE='file:"
            + EscapeLiteral(profile.TrimEnd('\\', '/')) + "' AND CONTAINS(System.ItemNameDisplay,'\"" + EscapeLiteral(term) + "*\"')";
        return true;
    }
}

internal sealed class FileSearch : IDisposable
{
    internal const string Ok = "ok", Unavailable = "unavailable", Timeout = "timeout";
    internal static readonly TimeSpan Limit = TimeSpan.FromSeconds(2);
    readonly object gate = new();
    CancellationTokenSource? current;
    long latest;
    FileSnapshot? snapshot;
    bool disposed;
    readonly Func<string?, long, CancellationToken, Task<FileSnapshot>> query;

    internal FileSearch() => query = QueryAsync;

    // Couture d'essai : la requête (index Windows Search par défaut) est remplaçable, les tests ne touchent aucun index.
    internal FileSearch(Func<string?, long, CancellationToken, Task<FileSnapshot>> query) => this.query = query;

    static string Profile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // Lance une requête sur un thread de travail. Une génération plus récente annule la plus ancienne ; une génération plus ancienne
    // est ignorée. `done` n'est appelé depuis le worker que si cette requête est toujours la plus récente et que son résultat a été
    // stocké.
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
                    result = await query(term, generation, cts.Token);
                }
                catch (OperationCanceledException) { return; }
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
                // Libérée ici seulement, une fois la requête terminée ; `current` ne pointe jamais vers une source libérée, donc
                // Cancel() ne peut pas la toucher.
                lock (gate)
                {
                    if (ReferenceEquals(current, cts))
                        current = null;
                }
                cts.Dispose();
            }
        });
    }

    // Requête ponctuelle (ligne de commande) : rien n'est conservé.
    internal static Task<FileSnapshot> RunOnceAsync(string? term, CancellationToken token) => QueryAsync(term, 0, token);

    static async Task<FileSnapshot> QueryAsync(string? term, long generation, CancellationToken token)
    {
        if (!FileQuery.TryBuild(term, Profile, out var sql))
            return new(generation, Ok, Array.Empty<FileHit>());
        try
        {
            return await Task.Run(() => Execute(sql, generation), token).WaitAsync(Limit, token);
        }
        catch (TimeoutException) { return new(generation, Timeout, Array.Empty<FileHit>()); }
    }

    static FileSnapshot Execute(string sql, long generation)
    {
        object? connection = null, rows = null;
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
        catch { return new(generation, Unavailable, Array.Empty<FileHit>()); }
        finally
        {
            if (rows != null && Marshal.IsComObject(rows))
                Marshal.FinalReleaseComObject(rows);
            if (connection != null && Marshal.IsComObject(connection))
                Marshal.FinalReleaseComObject(connection);
        }
    }

    // Ce que la palette affiche. Le frontend ne reçoit qu'un index pour l'ouverture, jamais un chemin à croire.
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
                items = snapshot.Items.Select((h, i) => new { index = i, name = h.Name, path = h.Path }).ToArray()
            };
        }
    }

    // Un chemin n'est libéré que pour un résultat de la dernière requête stockée.
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
