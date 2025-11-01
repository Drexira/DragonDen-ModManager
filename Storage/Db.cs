using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        guid TEXT,
        source_url TEXT,
        disabled INTEGER DEFAULT 0,
        disabled_at INTEGER
    )");
        c.Execute(@"CREATE TABLE IF NOT EXISTS files(
        path TEXT PRIMARY KEY,
        mod_id TEXT,
        sha256 TEXT,
        target TEXT,
        FOREIGN KEY(mod_id) REFERENCES mods(mod_id) ON DELETE CASCADE
    )");
        c.Execute(@"CREATE TABLE IF NOT EXISTS dirs(
        path TEXT,
        mod_id TEXT,
        target TEXT,
        PRIMARY KEY(path, mod_id, target),
        FOREIGN KEY(mod_id) REFERENCES mods(mod_id) ON DELETE CASCADE
    )");

        EnsureColumn(c, "mods", "guid", "TEXT");
        EnsureColumn(c, "mods", "source_url", "TEXT");
        EnsureColumn(c, "mods", "disabled", "INTEGER DEFAULT 0");
        EnsureColumn(c, "mods", "disabled_at", "INTEGER");

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
            catch (Exception ex)
            {
                Logger.Error($"[Db] Failed to add 'target' to 'files': {ex}");
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
            catch (Exception ex)
            {
                Logger.Error($"[Db] Failed to add '{col}' column to '{table}' table: {ex}");
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
        Installer.Target target, string source, string sourceUrl, string guid, List<string>? dirs)
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

        using (var delF = c.CreateCommand())
        {
            delF.CommandText = "DELETE FROM files WHERE mod_id=$m";
            delF.Parameters.AddWithValue("$m", modId);
            delF.ExecuteNonQuery();
        }

        using (var delD = c.CreateCommand())
        {
            delD.CommandText = "DELETE FROM dirs WHERE mod_id=$m";
            delD.Parameters.AddWithValue("$m", modId);
            delD.ExecuteNonQuery();
        }

        foreach (var rel in files ?? new List<string>())
        {
            var full = Path.Combine(root, rel);
            string digest;
            try
            {
                digest = File.Exists(full) ? Sha256(full) : "";
            }
            catch
            {
                digest = "";
            }

            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO files(path,mod_id,sha256,target) VALUES($p,$m,$h,$t)";
            cmd.Parameters.AddWithValue("$p", rel.Replace('\\', '/'));
            cmd.Parameters.AddWithValue("$m", modId);
            cmd.Parameters.AddWithValue("$h", digest);
            cmd.Parameters.AddWithValue("$t", target == Installer.Target.Client ? "client" : "server");
            cmd.ExecuteNonQuery();
        }

        foreach (var d in dirs ?? new List<string>())
        {
            using var cmd = c.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO dirs(path,mod_id,target) VALUES($p,$m,$t)";
            cmd.Parameters.AddWithValue("$p", d.Replace('\\', '/'));
            cmd.Parameters.AddWithValue("$m", modId);
            cmd.Parameters.AddWithValue("$t", target == Installer.Target.Client ? "client" : "server");
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
        App.NotifyInstallsChanged();
    }

    public void RecordInstallWithSource(string modId, string name, string version, List<string> files,
        string root, Installer.Target target, string sourceUrl, string guid, List<string>? dirs = null)
    {
        RecordInstallDetailed(modId, name, version, files, root, target, "installed", sourceUrl, guid, dirs ?? new List<string>());
    }

    public void RecordInstall(string modId, string name, string version, List<string> files, string root, Installer.Target target)
    {
        RecordInstallWithSource(modId, name, version, files, root, target, "", "", new List<string>());
    }

    public (int missing, int changed) VerifyForRoot(string root)
    {
        using var c = Conn();
        int missing = 0, changed = 0;
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT f.path, f.sha256
FROM files f
JOIN mods m ON m.mod_id = f.mod_id
WHERE COALESCE(m.disabled,0)=0
";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var rel = r.IsDBNull(0) ? "" : r.GetString(0);
            var hash = r.IsDBNull(1) ? "" : r.GetString(1);
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

    public List<(string mod_id, string name, string version, long fileCount, string guid, string source_url, long installed_at, int disabled, long disabled_at)>
        ListMods()
    {
        using var c = Conn();
        var list = new List<(string, string, string, long, string, string, long, int, long)>();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
    SELECT m.mod_id,
           m.name,
           m.version,
           COUNT(f.path),
           COALESCE(m.guid,''),
           COALESCE(m.source_url,''),
           COALESCE(m.installed_at,0),
           COALESCE(m.disabled,0) AS disabled,
           COALESCE(m.disabled_at,0) AS disabled_at
    FROM mods m
    LEFT JOIN files f ON m.mod_id=f.mod_id
    GROUP BY m.mod_id,m.name,m.version,m.guid,m.source_url,m.installed_at,m.disabled,m.disabled_at
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
                r.IsDBNull(6) ? 0L : r.GetInt64(6),
                r.IsDBNull(7) ? 0 : r.GetInt32(7),
                r.IsDBNull(8) ? 0L : r.GetInt64(8)
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

        var dbFolder = Path.GetFileNameWithoutExtension(Paths.ModsDbPath) ?? "default";
        var disabledRootBase = Path.Combine(Paths.DataDir, "Disabled Mods", dbFolder);
        var sptRoot = App.Config.Paths.SptRoot;

        static bool IsProtectedDir(string baseRoot, string dirFull)
        {
            try
            {
                var rel = Path.GetRelativePath(baseRoot, dirFull).Replace('\\', '/').TrimEnd('/');
                rel = rel.TrimStart('/');
                if (string.Equals(rel, "BepInEx/plugins", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(rel, "BepInEx/patchers", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(rel, "SPT/user/mods", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(rel, "user/mods", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch
            {
                // good girl action
            }

            return false;
        }

        static bool TryDeleteDirRobust(string path, int maxTries = 6, int delayMs = 40)
        {
            for (var i = 0; i < maxTries; i++)
            {
                try
                {
                    if (!Directory.Exists(path)) return true;
                    try
                    {
                        var attr = File.GetAttributes(path);
                        var cleared = attr & ~FileAttributes.ReadOnly & ~FileAttributes.System & ~FileAttributes.Hidden;
                        if (cleared != attr) File.SetAttributes(path, cleared);
                    }
                    catch
                    {
                        // good girl action
                    }

                    Directory.Delete(path, false);
                    return true;
                }
                catch (IOException)
                {
                    System.Threading.Thread.Sleep(delayMs);
                }
                catch (UnauthorizedAccessException)
                {
                    System.Threading.Thread.Sleep(delayMs);
                }
            }

            return !Directory.Exists(path);
        }

        static void TryDeleteEmptyParentsQuiet(string start, string stopAt)
        {
            try
            {
                static string Normalize(string? p)
                {
                    if (string.IsNullOrWhiteSpace(p)) return "";
                    try
                    {
                        return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    }
                    catch
                    {
                        return p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    }
                }

                var stop = Normalize(stopAt);
                var cur = Normalize(start);
                while (!string.IsNullOrWhiteSpace(cur) && !string.Equals(cur, stop, StringComparison.OrdinalIgnoreCase))
                {
                    if (!Directory.Exists(cur)) break;
                    if (IsProtectedDir(stop, cur)) break;
                    if (Directory.EnumerateFileSystemEntries(cur).Any()) break;

                    if (!TryDeleteDirRobust(cur))
                    {
                        if (Directory.Exists(cur))
                            Logger.Error($"[Db] Failed to delete empty dir '{cur}' after retries");
                        break;
                    }

                    var next = Path.GetDirectoryName(cur);
                    if (string.IsNullOrWhiteSpace(next)) break;
                    cur = Normalize(next);
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[Db] Failed to delete empty parent directories: {ex}");
            }
        }

        foreach (var modId in modIds)
        {
            using var cmdF = c.CreateCommand();
            cmdF.CommandText = "SELECT path FROM files WHERE mod_id=$m";
            cmdF.Parameters.AddWithValue("$m", modId);
            using var rF = cmdF.ExecuteReader();
            var fileRels = new List<string>();
            while (rF.Read())
            {
                var path = rF.IsDBNull(0) ? "" : rF.GetString(0);
                if (!string.IsNullOrWhiteSpace(path)) fileRels.Add(path.Replace('\\', '/'));
            }

            using var cmdD = c.CreateCommand();
            cmdD.CommandText = "SELECT path FROM dirs WHERE mod_id=$m";
            cmdD.Parameters.AddWithValue("$m", modId);
            using var rD = cmdD.ExecuteReader();
            var dirRels = new List<string>();
            while (rD.Read())
            {
                var path = rD.IsDBNull(0) ? "" : rD.GetString(0);
                if (!string.IsNullOrWhiteSpace(path)) dirRels.Add(path.Replace('\\', '/'));
            }

            foreach (var rel in fileRels)
            {
                var full = Path.Combine(sptRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    if (File.Exists(full))
                    {
                        try
                        {
                            var attr = File.GetAttributes(full);
                            var cleared = attr & ~FileAttributes.ReadOnly & ~FileAttributes.System & ~FileAttributes.Hidden;
                            if (cleared != attr) File.SetAttributes(full, cleared);
                        }
                        catch
                        {
                             // good girl action
                        }

                        File.Delete(full);
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error($"[Db] Failed to delete file '{full}': {ex}");
                }

                try
                {
                    var dir = Path.GetDirectoryName(full);
                    if (!string.IsNullOrWhiteSpace(dir))
                        TryDeleteEmptyParentsQuiet(dir, sptRoot);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[Db] Live cleanup failed for '{full}': {ex}");
                }
            }

            var unifiedDisabled = Path.Combine(disabledRootBase, modId);
            var legacyClient = Path.Combine(disabledRootBase, "client", modId);
            var legacyServer = Path.Combine(disabledRootBase, "server", modId);

            try
            {
                if (Directory.Exists(unifiedDisabled)) Directory.Delete(unifiedDisabled, true);
            }
            catch
            {
                // good girl action
            }

            try
            {
                if (Directory.Exists(legacyClient)) Directory.Delete(legacyClient, true);
            }
            catch
            {
                // good girl action
            }

            try
            {
                if (Directory.Exists(legacyServer)) Directory.Delete(legacyServer, true);
            }
            catch
            {
                // good girl action
            }

            TryDeleteEmptyParentsQuiet(Path.Combine(disabledRootBase, "client"), disabledRootBase);
            TryDeleteEmptyParentsQuiet(Path.Combine(disabledRootBase, "server"), disabledRootBase);
            TryDeleteEmptyParentsQuiet(disabledRootBase, Path.Combine(Paths.DataDir, "Disabled Mods"));

            foreach (var drel in dirRels.OrderByDescending(s => s.Length).ToList())
            {
                var dfull = Path.Combine(sptRoot, drel.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    if (Directory.Exists(dfull) && !IsProtectedDir(sptRoot, dfull) && !Directory.EnumerateFileSystemEntries(dfull).Any())
                        TryDeleteDirRobust(dfull);
                }
                catch (Exception ex)
                {
                    Logger.Error($"[Db] Failed to delete empty dir '{dfull}': {ex}");
                }

                try
                {
                    var parent = Path.GetDirectoryName(dfull);
                    if (!string.IsNullOrWhiteSpace(parent))
                        TryDeleteEmptyParentsQuiet(parent, sptRoot);
                }
                catch
                {
                    // good girl action
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

    public List<string> ListDirsForModId(string modId)
    {
        using var c = Conn();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT path FROM dirs WHERE mod_id=$m ORDER BY path";
        cmd.Parameters.AddWithValue("$m", modId);
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read())
        {
            var p = r.IsDBNull(0) ? "" : r.GetString(0);
            if (!string.IsNullOrWhiteSpace(p)) list.Add(p.Replace('\\', '/'));
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
    
    private static string BuildAckKey(string? guid, string? name)
    {
        var g = (guid ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(g)) return g;
        var n = (name ?? "").Trim();
        return n.ToUpperInvariant();
    }

    public bool HasModPageAcknowledged(string? guid, string? name)
    {
        return HasInstalledBefore.HasModInstalledBefore(guid, name);
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
            catch (Exception ex)
            {
                Logger.Error($"[Db] Failed to rollback transaction in RemoveInstall: {ex}");
            }

            throw;
        }
    }

    public int PruneRemovedMods()
    {
        using var c = Conn();

        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT m.mod_id,
       COALESCE(f.path,'') AS path,
       COALESCE(f.target,'client') AS target,
       COALESCE(m.disabled,0) AS disabled
FROM mods m
LEFT JOIN files f ON f.mod_id = m.mod_id
ORDER BY m.mod_id";
        using var r = cmd.ExecuteReader();

        var byMod = new Dictionary<string, List<(string path, string target)>>(StringComparer.Ordinal);
        var disabledByMod = new Dictionary<string, bool>(StringComparer.Ordinal);

        while (r.Read())
        {
            var id = r.GetString(0);
            var path = r.IsDBNull(1) ? "" : r.GetString(1);
            var target = r.IsDBNull(2) ? "client" : r.GetString(2);
            var disabled = !r.IsDBNull(3) && r.GetInt32(3) == 1;

            if (!byMod.TryGetValue(id, out var list))
            {
                list = new List<(string, string)>();
                byMod[id] = list;
            }

            list.Add((path, target));
            disabledByMod[id] = disabled;
        }

        var dbFolder = Path.GetFileNameWithoutExtension(Paths.ModsDbPath) ?? "default";
        var disabledRootBase = Path.Combine(Paths.DataDir, "Disabled Mods", dbFolder);
        var sptRoot = App.Config.Paths.SptRoot;

        static string NormalizeTarget(string t) => string.Equals(t, "server", StringComparison.OrdinalIgnoreCase) ? "server" : "client";

        static string JoinUnder(string root, string rel)
        {
            var r = (rel ?? "").Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
            return Path.Combine(root ?? "", r);
        }

        var removed = 0;

        foreach (var kv in byMod)
        {
            var modId = kv.Key;
            var files = kv.Value;
            var isDisabled = disabledByMod.TryGetValue(modId, out var d) && d;

            var anyOnDisk = false;

            foreach (var (relRaw, tgtRaw) in files)
            {
                if (string.IsNullOrWhiteSpace(relRaw)) continue;

                var full = JoinUnder(sptRoot, relRaw);
                if (File.Exists(full))
                {
                    anyOnDisk = true;
                    break;
                }

                if (isDisabled)
                {
                    var target = NormalizeTarget(tgtRaw);
                    var disabledFull = JoinUnder(Path.Combine(disabledRootBase, target, modId), relRaw);
                    if (File.Exists(disabledFull))
                    {
                        anyOnDisk = true;
                        break;
                    }
                }
            }

            if (!anyOnDisk && !isDisabled)
            {
                c.Execute("DELETE FROM mods WHERE mod_id=$m", ("$m", modId));
                removed++;
            }
        }

        return removed;
    }

    public bool IsDisabledByName(string name)
    {
        using var c = Conn();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM mods WHERE name=$n COLLATE NOCASE AND COALESCE(disabled,0)=1 LIMIT 1";
        cmd.Parameters.AddWithValue("$n", name ?? "");
        return cmd.ExecuteScalar() != null;
    }

    public bool IsDisabledById(string modId)
    {
        using var c = Conn();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM mods WHERE mod_id=$m AND COALESCE(disabled,0)=1 LIMIT 1";
        cmd.Parameters.AddWithValue("$m", modId ?? "");
        return cmd.ExecuteScalar() != null;
    }

    public void SetDisabled(string modId, bool disabled)
    {
        using var c = Conn();
        c.Execute(
            "UPDATE mods SET disabled=$d, disabled_at=$t WHERE mod_id=$m",
            ("$d", disabled ? 1 : 0),
            ("$t", disabled ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : (long?)null),
            ("$m", modId)
        );
        App.NotifyInstallsChanged();
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