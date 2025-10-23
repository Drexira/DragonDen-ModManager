using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using DragonDen.ModManager.Services;
using Microsoft.Data.Sqlite;

namespace DragonDen.ModManager.Storage;

public sealed class Db
{
    private readonly string dbPath;

    public Db(string dbPath)
    {
        this.dbPath = dbPath;
    }

    private SqliteConnection Conn()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        var c = new SqliteConnection("Data Source=" + dbPath);
        c.Open();
        using var pragma = c.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON";
        pragma.ExecuteNonQuery();
        return c;
    }

    public void Init()
    {
        using var c = Conn();
        c.Execute(@"CREATE TABLE IF NOT EXISTS mods(
            mod_id TEXT PRIMARY KEY,
            name TEXT,
            version TEXT,
            installed_at INTEGER,
            source TEXT,
            guid TEXT,             -- NEW
            source_url TEXT        -- NEW
        )");
        c.Execute(@"CREATE TABLE IF NOT EXISTS files(
            path TEXT PRIMARY KEY,
            mod_id TEXT,
            sha256 TEXT,
            target TEXT,
            FOREIGN KEY(mod_id) REFERENCES mods(mod_id) ON DELETE CASCADE
        )");

        EnsureColumn(c, "mods", "guid", "TEXT");
        EnsureColumn(c, "mods", "source_url", "TEXT");

        var hasTarget = false;
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "PRAGMA table_info(files)";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                if (string.Equals(r.GetString(1), "target", StringComparison.OrdinalIgnoreCase))
                    hasTarget = true;
        }

        if (!hasTarget)
            try
            {
                c.Execute("ALTER TABLE files ADD COLUMN target TEXT DEFAULT 'client'");
            }
            catch
            {
                // good girl action
            }
    }

    private static void EnsureColumn(SqliteConnection c, string table, string col, string type)
    {
        var has = false;
        using var cmd = c.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table})";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (string.Equals(r.GetString(1), col, StringComparison.OrdinalIgnoreCase))
                has = true;
        if (!has)
            try
            {
                c.Execute($"ALTER TABLE {table} ADD COLUMN {col} {type}");
            }
            catch
            {
                // good girl action
            }
    }

    public HashSet<string> GetInstalledPathsForTarget(Installer.Target target)
    {
        using var c = Conn();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT f.path
FROM files f
JOIN mods m ON m.mod_id = f.mod_id
WHERE COALESCE(f.target,'client') = $t
  AND COALESCE(m.source,'installed') <> 'scan'";
        cmd.Parameters.AddWithValue("$t", target == Installer.Target.Client ? "client" : "server");

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var p = r.IsDBNull(0) ? "" : r.GetString(0);
            if (!string.IsNullOrWhiteSpace(p))
                set.Add(p.Replace('\\', '/'));
        }

        return set;
    }

    public void RecordInstallDetailed(string modId, string name, string version, List<string> files, string root, 
        Installer.Target target, string source, string sourceUrl,string guid)
    {
        using var c = Conn();

        using (var check = c.CreateCommand())
        {
            check.CommandText = "SELECT COALESCE(source,'') FROM mods WHERE mod_id=$id LIMIT 1";
            check.Parameters.AddWithValue("$id", modId);
            var cur = check.ExecuteScalar() as string;

            if (!string.IsNullOrWhiteSpace(cur) && !string.Equals(cur, "scan", StringComparison.OrdinalIgnoreCase)
                                                && string.Equals(source ?? "", "scan", StringComparison.OrdinalIgnoreCase))
                return;
        }

        c.Execute(
            "INSERT OR REPLACE INTO mods(mod_id,name,version,installed_at,source,guid,source_url) " +
            "VALUES($id,$n,$v,$t,$s,$g,$u)",
            ("$id", modId),
            ("$n", name),
            ("$v", version),
            ("$t", DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
            ("$s", string.IsNullOrWhiteSpace(source) ? "installed" : source),
            ("$g", guid ?? ""),
            ("$u", sourceUrl ?? "")
        );

        using var tx = c.BeginTransaction();

        using (var del = c.CreateCommand())
        {
            del.CommandText = "DELETE FROM files WHERE mod_id=$m";
            del.Parameters.AddWithValue("$m", modId);
            del.ExecuteNonQuery();
        }

        foreach (var rel in files)
        {
            var full = Path.Combine(root, rel);
            var digest = Sha256(full);
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO files(path,mod_id,sha256,target) VALUES($p,$m,$h,$t)";
            cmd.Parameters.AddWithValue("$p", rel);
            cmd.Parameters.AddWithValue("$m", modId);
            cmd.Parameters.AddWithValue("$h", digest);
            cmd.Parameters.AddWithValue("$t", target == Installer.Target.Client ? "client" : "server");
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
        App.NotifyInstallsChanged();
    }

    public void RecordInstallWithSource(string modId, string name, string version, List<string> files,
        string root, Installer.Target target, string sourceUrl, string guid)
    {
        RecordInstallDetailed(modId, name, version, files, root, target, "installed", sourceUrl, guid);
    }

    public void RecordInstall(string modId, string name, string version, List<string> files, string root, Installer.Target target)
    {
        RecordInstallWithSource(modId, name, version, files, root, target, "", "");
    }

    public (int missing, int changed) VerifyForRoot(string root)
    {
        using var c = Conn();
        int missing = 0, changed = 0;
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT path, sha256 FROM files";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var rel = r.GetString(0);
            var hash = r.GetString(1);
            var f = Path.Combine(root, rel);
            if (!File.Exists(f))
            {
                missing++;
                continue;
            }

            var cur = Sha256(f);
            if (!string.Equals(cur, hash, StringComparison.OrdinalIgnoreCase)) changed++;
        }

        return (missing, changed);
    }

    public (int missing, int changed) VerifyAll()
    {
        var m1 = (0, 0);
        var m2 = (0, 0);
        if (!string.IsNullOrWhiteSpace(Spt.ClientModsPath)) m1 = VerifyForRoot(Spt.ClientModsPath);
        if (!string.IsNullOrWhiteSpace(Spt.ServerModsPath)) m2 = VerifyForRoot(Spt.ServerModsPath);
        return (m1.Item1 + m2.Item1, m1.Item2 + m2.Item2);
    }

    public List<(string mod_id, string name, string version, long fileCount, string guid, string source_url, long installed_at)> ListMods()
    {
        using var c = Conn();
        var list = new List<(string, string, string, long, string, string, long)>();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
        SELECT m.mod_id,
               m.name,
               m.version,
               COUNT(f.path),
               COALESCE(m.guid,''),
               COALESCE(m.source_url,''),
               COALESCE(m.installed_at,0)
        FROM mods m
        LEFT JOIN files f ON m.mod_id=f.mod_id
        GROUP BY m.mod_id,m.name,m.version,m.guid,m.source_url,m.installed_at
        ORDER BY m.name";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((
                r.GetString(0),
                r.GetString(1),
                r.GetString(2),
                r.GetInt64(3),
                r.GetString(4),
                r.GetString(5),
                r.IsDBNull(6) ? 0L : r.GetInt64(6)
            ));
        return list;
    }

    public bool TryGetInstalled(string name, out string version)
    {
        using var c = Conn();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT version FROM mods WHERE name=$n COLLATE NOCASE LIMIT 1";
        cmd.Parameters.AddWithValue("$n", name ?? "");
        var v = cmd.ExecuteScalar() as string;
        version = v ?? "";
        return v != null;
    }

    public bool HasRealInstall(string name)
    {
        using var c = Conn();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT 1
FROM mods
WHERE name=$n COLLATE NOCASE
  AND (COALESCE(guid,'') <> '' OR COALESCE(source,'') <> 'scan')
LIMIT 1";
        cmd.Parameters.AddWithValue("$n", name ?? "");
        var v = cmd.ExecuteScalar();
        return v != null;
    }

    public void Uninstall(string name)
    {
        using var c = Conn();

        using var getMods = c.CreateCommand();
        getMods.CommandText = "SELECT mod_id FROM mods WHERE name=$n";
        getMods.Parameters.AddWithValue("$n", name);

        var ids = new List<string>();
        using (var rr = getMods.ExecuteReader())
        {
            while (rr.Read()) ids.Add(rr.GetString(0));
        }

        foreach (var id in ids)
            UninstallByModId(id);
    }

    public void UninstallByModId(string modId)
    {
        UninstallByModIds(new List<string> { modId });
    }

    public void UninstallByModIds(List<string> modIds)
    {
        using var c = Conn();

        foreach (var modId in modIds)
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT path, target FROM files WHERE mod_id=$m";
            cmd.Parameters.AddWithValue("$m", modId);
            using var r = cmd.ExecuteReader();
            var items = new List<(string path, string target)>();
            while (r.Read())
            {
                var path = r.IsDBNull(0) ? "" : r.GetString(0);
                var target = r.IsDBNull(1) ? "client" : r.GetString(1);
                items.Add((path, target));
            }

            foreach (var it in items)
            {
                var root = it.target == "server" ? Spt.ServerModsPath : Spt.ClientModsPath;
                if (!string.IsNullOrWhiteSpace(root))
                {
                    var full = Path.Combine(root, it.path);
                    try
                    {
                        if (File.Exists(full)) File.Delete(full);
                    }
                    catch
                    {
                        // good girl action
                    }

                    try
                    {
                        var dir = Path.GetDirectoryName(full);
                        var stop = string.Equals(root.TrimEnd(Path.DirectorySeparatorChar), dir?.TrimEnd(Path.DirectorySeparatorChar),
                            StringComparison.OrdinalIgnoreCase);
                        while (!stop && !string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
                        {
                            Directory.Delete(dir);
                            dir = Path.GetDirectoryName(dir);
                            stop = string.Equals(root.TrimEnd(Path.DirectorySeparatorChar), dir?.TrimEnd(Path.DirectorySeparatorChar),
                                StringComparison.OrdinalIgnoreCase);
                        }
                    }
                    catch
                    {
                        // good girl action
                    }
                }
            }

            c.Execute("DELETE FROM mods WHERE mod_id=$m", ("$m", modId));
        }
    }

    public List<(string path, string target)> ListFilesForModId(string modId)
    {
        using var c = Conn();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT path, target FROM files WHERE mod_id=$m ORDER BY path";
        cmd.Parameters.AddWithValue("$m", modId);
        using var r = cmd.ExecuteReader();
        var list = new List<(string path, string target)>();
        while (r.Read())
        {
            var path = r.IsDBNull(0) ? "" : r.GetString(0);
            var target = r.IsDBNull(1) ? "client" : r.GetString(1);
            list.Add((path, target));
        }

        return list;
    }

    public List<(string root, string rel)> ListInstallFiles(string modId)
    {
        const string sql = @"
SELECT f.root AS root, f.rel AS rel
FROM install_files f
WHERE f.mod_id = @modId
";
        using var cmd = Conn().CreateCommand();
        cmd.CommandText = sql;

        var p = cmd.CreateParameter();
        p.ParameterName = "@modId";
        p.Value = modId;
        cmd.Parameters.Add(p);

        var res = new List<(string root, string rel)>();
        using var rdr = cmd.ExecuteReader();
        while (rdr.Read())
        {
            var root = rdr["root"]?.ToString() ?? "";
            var rel = rdr["rel"]?.ToString() ?? "";
            if (!string.IsNullOrWhiteSpace(root) && !string.IsNullOrWhiteSpace(rel))
                res.Add((root, rel.Replace('\\', '/')));
        }

        return res;
    }

    public void RemoveInstall(string modId)
    {
        using var tx = Conn().BeginTransaction();
        try
        {
            using (var cmd1 = Conn().CreateCommand())
            {
                cmd1.Transaction = tx;
                cmd1.CommandText = "DELETE FROM install_files WHERE mod_id = @modId";
                var p = cmd1.CreateParameter();
                p.ParameterName = "@modId";
                p.Value = modId;
                cmd1.Parameters.Add(p);
                cmd1.ExecuteNonQuery();
            }

            using (var cmd2 = Conn().CreateCommand())
            {
                cmd2.Transaction = tx;
                cmd2.CommandText = "DELETE FROM installs WHERE mod_id = @modId";
                var p = cmd2.CreateParameter();
                p.ParameterName = "@modId";
                p.Value = modId;
                cmd2.Parameters.Add(p);
                cmd2.ExecuteNonQuery();
            }

            tx.Commit();
        }
        catch
        {
            try
            {
                tx.Rollback();
            }
            catch
            {
                // good girl action
            }

            throw;
        }
    }

    private static int TryInt(object? o)
    {
        return o is null ? 0 : int.TryParse(o.ToString(), out var n) ? n : 0;
    }

    private static string Sha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }
}

internal static class SqliteExt
{
    public static void Execute(this SqliteConnection c, string sql, params (string, object?)[] p)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var kv in p) cmd.Parameters.AddWithValue(kv.Item1, kv.Item2 ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }
}