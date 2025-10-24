using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace DragonDen.ModManager.Services;

public sealed class CacheDb
{
    private readonly string path;

    public CacheDb(string dbPath)
    {
        path = dbPath;
    }

    private SqliteConnection Conn()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var c = new SqliteConnection("Data Source=" + path);
        c.Open();
        using (var pragma = c.CreateCommand())
        {
            pragma.CommandText = @"
PRAGMA journal_mode=WAL;
PRAGMA synchronous=NORMAL;
PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }

        return c;
    }

    public void Init()
    {
        using var c = Conn();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
    CREATE TABLE IF NOT EXISTS meta(
      key TEXT PRIMARY KEY,
      value TEXT
    );
    CREATE TABLE IF NOT EXISTS categories(
      id INTEGER PRIMARY KEY,
      title TEXT,
      slug TEXT,
      color_class TEXT
    );
    CREATE TABLE IF NOT EXISTS owners(
      id INTEGER PRIMARY KEY,
      name TEXT
    );
    CREATE TABLE IF NOT EXISTS mods(
      id INTEGER PRIMARY KEY,
      guid TEXT,                      -- NEW
      name TEXT,
      slug TEXT,
      teaser TEXT,
      thumbnail TEXT,
      downloads INTEGER,
      detail_url TEXT,
      featured INTEGER,
      contains_ads INTEGER,
      contains_ai_content INTEGER,
      category_id INTEGER,
      category_slug TEXT,
      category_name TEXT,
      updated_at TEXT,
      published_at TEXT
    );
    CREATE TABLE IF NOT EXISTS mod_owners(
      mod_id INTEGER,
      owner_id INTEGER,
      PRIMARY KEY(mod_id, owner_id)
    );
    CREATE TABLE IF NOT EXISTS versions(
      id INTEGER PRIMARY KEY,
      mod_id INTEGER,
      version TEXT,
      link TEXT,
      spt_version_constraint TEXT,
      downloads INTEGER,
      published_at TEXT,
      spt_norm TEXT
    );
    CREATE TABLE IF NOT EXISTS mod_sources(
      mod_id INTEGER,
      url TEXT,
      label TEXT,
      PRIMARY KEY(mod_id, url)
    );
    CREATE INDEX IF NOT EXISTS idx_mods_name ON mods(name);
    CREATE INDEX IF NOT EXISTS idx_mods_slug ON mods(slug);
    CREATE INDEX IF NOT EXISTS idx_mods_guid ON mods(guid);     -- NEW
    CREATE INDEX IF NOT EXISTS idx_mods_updated ON mods(updated_at);
    CREATE INDEX IF NOT EXISTS idx_versions_mod ON versions(mod_id);
    CREATE INDEX IF NOT EXISTS idx_versions_sptnorm ON versions(spt_norm);
    ";
        cmd.ExecuteNonQuery();

        EnsureColumn(c, "mods", "guid", "TEXT");
        EnsureColumn(c, "categories", "color_class", "TEXT");
        EnsureColumn(c, "versions", "spt_norm", "TEXT");
        try
        {
            using var idx = c.CreateCommand();
            idx.CommandText = "CREATE INDEX IF NOT EXISTS idx_mods_guid ON mods(guid)";
            idx.ExecuteNonQuery();
        }
        catch
        {
            // good girl action
        }

        using (var fix = c.CreateCommand())
        {
            fix.CommandText = @"
    UPDATE mods
    SET category_name = COALESCE(NULLIF(category_name,''), NULLIF(category_slug,''), category_slug)
    WHERE COALESCE(category_name,'') = '' AND COALESCE(category_slug,'') <> ''";
            try
            {
                fix.ExecuteNonQuery();
            }
            catch
            {
                // good girl action
            }
        }
    }

    private static void EnsureColumn(SqliteConnection c, string table, string column, string type)
    {
        var has = false;
        using var check = c.CreateCommand();
        check.CommandText = $"PRAGMA table_info({table})";
        using var r = check.ExecuteReader();
        while (r.Read())
            if (!r.IsDBNull(1) && string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                has = true;
        if (!has)
            try
            {
                using var alter = c.CreateCommand();
                alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}";
                alter.ExecuteNonQuery();
            }
            catch
            {
                // good girl action
            }
    }

    public async Task<(List<ForgeClient.ModSummary> items, int total)> QueryModsAsync(string? text, string? author, string? categorySlug,
        string? sptConstraint, string sort, int page, int pageSize, CancellationToken ct = default, bool hideFeatured = false,
        bool hideAds = false, bool hideAi = false)
    {
        text ??= "";
        author ??= "";
        categorySlug ??= "";
        sptConstraint ??= "";
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize <= 0 ? 12 : pageSize, 1, 100);

        using var c = Conn();

        var where = new List<string> { "1=1" };
        var prm = new Dictionary<string, object?>();

        if (!string.IsNullOrWhiteSpace(text))
        {
            where.Add("(m.name LIKE $q ESCAPE '\\' OR m.slug LIKE $q ESCAPE '\\' OR m.guid = $g)");
            prm["$q"] = "%" + text.Replace("%", "\\%").Replace("_", "\\_") + "%";
            prm["$g"] = text;
        }

        if (!string.IsNullOrWhiteSpace(author))
        {
            where.Add(@"EXISTS(SELECT 1 FROM mod_owners mo
                               JOIN owners o ON o.id=mo.owner_id
                               WHERE mo.mod_id=m.id AND o.name LIKE $auth ESCAPE '\')");
            prm["$auth"] = "%" + author.Replace("%", "\\%").Replace("_", "\\_") + "%";
        }

        if (!string.IsNullOrWhiteSpace(categorySlug))
        {
            where.Add("COALESCE(m.category_slug,'') = $cat");
            prm["$cat"] = categorySlug;
        }

        if (hideFeatured) where.Add("COALESCE(m.featured,0) = 0");
        if (hideAds) where.Add("COALESCE(m.contains_ads,0) = 0");
        if (hideAi) where.Add("COALESCE(m.contains_ai_content,0) = 0");

        if (!string.IsNullOrWhiteSpace(sptConstraint))
        {
            if (Regex.IsMatch(sptConstraint, @"^\d+\.\d+\.\d+$"))
            {
                where.Add(@"EXISTS(SELECT 1 FROM versions v
                                   WHERE v.mod_id=m.id AND (
                                       COALESCE(v.spt_norm,'') = $spt
                                       OR COALESCE(v.spt_version_constraint,'') LIKE $sptLike ESCAPE '\'
                                   ))");
                prm["$spt"] = sptConstraint;
                prm["$sptLike"] = "%" + sptConstraint.Replace("%", "\\%").Replace("_", "\\_") + "%";
            }
            else if (Regex.IsMatch(sptConstraint, @"^\d+\.\d+$"))
            {
                where.Add(@"EXISTS(SELECT 1 FROM versions v
                                   WHERE v.mod_id=m.id AND (
                                       COALESCE(v.spt_norm,'') LIKE $majDot ESCAPE '\'
                                       OR COALESCE(v.spt_version_constraint,'') LIKE $maj ESCAPE '\'
                                   ))");
                prm["$majDot"] = sptConstraint.Replace("%", "\\%").Replace("_", "\\_") + ".%";
                prm["$maj"] = "%" + sptConstraint.Replace("%", "\\%").Replace("_", "\\_") + "%";
            }
        }

        var order = sort switch
        {
            "downloads" => "m.downloads DESC, COALESCE(m.updated_at,m.published_at,'') DESC",
            "newest" => "COALESCE(m.published_at,m.updated_at,'') DESC, m.downloads DESC",
            _ => "COALESCE(m.updated_at,m.published_at,'') DESC, m.downloads DESC"
        };

        var total = 0;
        using (var count = c.CreateCommand())
        {
            count.CommandText = $"SELECT COUNT(*) FROM mods m WHERE {string.Join(" AND ", where)}";
            foreach (var kv in prm) count.Parameters.AddWithValue(kv.Key, kv.Value ?? DBNull.Value);
            var obj = await count.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (obj is long l) total = (int)Math.Min(int.MaxValue, l);
        }

        var items = new List<ForgeClient.ModSummary>();
        var idList = new List<int>();

        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = $@"
    SELECT m.id,
           COALESCE(m.guid,'')            AS guid,
           COALESCE(m.name,'')            AS name,
           COALESCE(m.slug,'')            AS slug,
           COALESCE(m.teaser,'')          AS teaser,
           COALESCE(m.thumbnail,'')       AS thumbnail,
           COALESCE(m.downloads,0)        AS downloads,
           COALESCE(m.detail_url,'')      AS detail_url,
           COALESCE(m.featured,0)         AS featured,
           COALESCE(m.contains_ads,0)     AS contains_ads,
           COALESCE(m.contains_ai_content,0) AS contains_ai_content,
           COALESCE(m.category_slug,'')   AS category_slug,
           COALESCE(m.category_name,'')   AS category_name,
           COALESCE(m.updated_at,'')      AS updated_at,
           COALESCE(m.published_at,'')    AS published_at
    FROM mods m
    WHERE {string.Join(" AND ", where)}
    ORDER BY {order}
    LIMIT $lim OFFSET $off";
            foreach (var kv in prm) cmd.Parameters.AddWithValue(kv.Key, kv.Value ?? DBNull.Value);
            cmd.Parameters.AddWithValue("$lim", pageSize);
            cmd.Parameters.AddWithValue("$off", (page - 1) * pageSize);

            using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                DateTimeOffset? upd = null, pub = null;
                var updStr = r.IsDBNull(13) ? "" : r.GetString(13);
                var pubStr = r.IsDBNull(14) ? "" : r.GetString(14);
                if (DateTimeOffset.TryParse(updStr, out var u)) upd = u;
                if (DateTimeOffset.TryParse(pubStr, out var p)) pub = p;

                var mod = new ForgeClient.ModSummary
                {
                    id = r.GetInt32(0),
                    guid = r.IsDBNull(1) ? null : r.GetString(1),
                    name = r.IsDBNull(2) ? "" : r.GetString(2),
                    slug = r.IsDBNull(3) ? null : r.GetString(3),
                    teaser = r.IsDBNull(4) ? null : r.GetString(4),
                    thumbnail = ForgeClient.ResolveImageUrl(r.IsDBNull(5) ? null : r.GetString(5)),
                    downloads = r.IsDBNull(6) ? 0 : r.GetInt64(6),
                    detail_url = r.IsDBNull(7) ? null : r.GetString(7),
                    featured = !r.IsDBNull(8) && r.GetInt32(8) != 0,
                    contains_ads = !r.IsDBNull(9) && r.GetInt32(9) != 0,
                    contains_ai_content = !r.IsDBNull(10) && r.GetInt32(10) != 0,
                    category = new ForgeClient.CategoryInfo
                    {
                        slug = r.IsDBNull(11) ? "" : r.GetString(11),
                        name = r.IsDBNull(12) ? "" : r.GetString(12)
                    },
                    updated_at = upd,
                    published_at = pub,
                    versions = null
                };

                items.Add(mod);
                idList.Add(mod.id);
            }
        }

        if (idList.Count > 0)
        {
            var ownersByMod = new Dictionary<int, List<ForgeClient.Person>>();

            var inList = string.Join(",", idList.Distinct());

            using var ownCmd = c.CreateCommand();
            ownCmd.CommandText = $@"
    SELECT mo.mod_id, o.id, COALESCE(o.name,'') AS name
    FROM mod_owners mo
    JOIN owners o ON o.id = mo.owner_id
    WHERE mo.mod_id IN ({inList})
    ORDER BY o.name COLLATE NOCASE";

            using var or = ownCmd.ExecuteReader();
            while (or.Read())
            {
                var modId = or.IsDBNull(0) ? 0 : or.GetInt32(0);
                var person = new ForgeClient.Person
                {
                    id = or.IsDBNull(1) ? 0 : or.GetInt32(1),
                    name = or.IsDBNull(2) ? "" : or.GetString(2)
                };

                if (!ownersByMod.TryGetValue(modId, out var list))
                {
                    list = new List<ForgeClient.Person>();
                    ownersByMod[modId] = list;
                }

                if (!string.IsNullOrWhiteSpace(person.name))
                    list.Add(person);
            }

            foreach (var m in items)
                if (ownersByMod.TryGetValue(m.id, out var list) && list.Count > 0)
                {
                    m.owner = list[0];
                    m.authors = list.Count > 1 ? list.Skip(1).ToArray() : Array.Empty<ForgeClient.Person>();
                }
                else
                {
                    m.owner = null;
                    m.authors = Array.Empty<ForgeClient.Person>();
                }
        }

        return (items, total);
    }

    public async Task<ForgeClient.ModVersion?> GetLatestVersionForModGuidAsync(string guid)
    {
        if (string.IsNullOrWhiteSpace(guid)) return null;

        using var c = Conn();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT id FROM mods WHERE guid = $g COLLATE NOCASE LIMIT 1";
        cmd.Parameters.AddWithValue("$g", guid);
        var idObj = cmd.ExecuteScalar();
        if (idObj is null) return null;

        var modId = Convert.ToInt32(idObj);
        var versions = GetVersionsForMod(modId);
        var best = versions
            .OrderByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(v => v.Downloads)
            .FirstOrDefault();
        if (best != null) return best;

        var all = await ForgeClient.GetAllVersionsAsync(modId, App.ShutdownToken).ConfigureAwait(false);
        foreach (var v in all) UpsertVersion(modId, v);

        versions = GetVersionsForMod(modId);
        return versions
            .OrderByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(v => v.Downloads)
            .FirstOrDefault();
    }

    public void SetMeta(string key, string value)
    {
        using var c = Conn();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO meta(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public string GetMeta(string key, string def = "")
    {
        using var c = Conn();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM meta WHERE key=$k LIMIT 1";
        cmd.Parameters.AddWithValue("$k", key);
        var v = cmd.ExecuteScalar() as string;
        return v ?? def;
    }

    public void UpsertCategory(int id, string title, string slug, string? colorClass)
    {
        using var c = Conn();
        using var cmd = c.CreateCommand();
        cmd.CommandText =
            "INSERT INTO categories(id,title,slug,color_class) VALUES($i,$t,$s,$c) ON CONFLICT(id) DO UPDATE SET title=excluded.title, slug=excluded.slug, color_class=excluded.color_class";
        cmd.Parameters.AddWithValue("$i", id);
        cmd.Parameters.AddWithValue("$t", title ?? "");
        cmd.Parameters.AddWithValue("$s", slug ?? "");
        cmd.Parameters.AddWithValue("$c", colorClass ?? "");
        cmd.ExecuteNonQuery();
    }

    public void UpsertOwner(int id, string name)
    {
        using var c = Conn();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO owners(id,name) VALUES($i,$n) ON CONFLICT(id) DO UPDATE SET name=excluded.name";
        cmd.Parameters.AddWithValue("$i", id);
        cmd.Parameters.AddWithValue("$n", name ?? "");
        cmd.ExecuteNonQuery();
    }

    public void UpsertMod(ForgeClient.ModSummary m)
    {
        using var c = Conn();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
    INSERT INTO mods(id,guid,name,slug,teaser,thumbnail,downloads,detail_url,featured,contains_ads,contains_ai_content,category_id,category_slug,category_name,updated_at,published_at)
    VALUES($id,$guid,$name,$slug,$teaser,$thumb,$dl,$detail,$feat,$ads,$ai,$catId,$catSlug,$catName,$upd,$pub)
    ON CONFLICT(id) DO UPDATE SET
     guid=excluded.guid,
     name=excluded.name,
     slug=excluded.slug,
     teaser=excluded.teaser,
     thumbnail=excluded.thumbnail,
     downloads=excluded.downloads,
     detail_url=excluded.detail_url,
     featured=excluded.featured,
     contains_ads=excluded.contains_ads,
     contains_ai_content=excluded.contains_ai_content,
     category_id=excluded.category_id,
     category_slug=excluded.category_slug,
     category_name=excluded.category_name,
     updated_at=excluded.updated_at,
     published_at=excluded.published_at";
        cmd.Parameters.AddWithValue("$id", m.id);
        cmd.Parameters.AddWithValue("$guid", m.guid ?? "");
        cmd.Parameters.AddWithValue("$name", m.name ?? "");
        cmd.Parameters.AddWithValue("$slug", m.slug ?? "");
        cmd.Parameters.AddWithValue("$teaser", m.teaser ?? "");
        cmd.Parameters.AddWithValue("$thumb", m.thumbnail ?? "");
        cmd.Parameters.AddWithValue("$dl", m.downloads);
        cmd.Parameters.AddWithValue("$detail", m.detail_url ?? "");
        cmd.Parameters.AddWithValue("$feat", m.featured ? 1 : 0);
        cmd.Parameters.AddWithValue("$ads", m.contains_ads ? 1 : 0);
        cmd.Parameters.AddWithValue("$ai", m.contains_ai_content ? 1 : 0);
        cmd.Parameters.AddWithValue("$catId", m.category?.id ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$catSlug", m.category?.slug ?? "");
        cmd.Parameters.AddWithValue("$catName", m.category?.name ?? m.category?.title ?? m.category?.slug ?? "");
        cmd.Parameters.AddWithValue("$upd", m.updated_at?.ToString("o", CultureInfo.InvariantCulture) ?? "");
        cmd.Parameters.AddWithValue("$pub", m.published_at?.ToString("o", CultureInfo.InvariantCulture) ?? "");
        cmd.ExecuteNonQuery();

        if (m.category is not null && m.category.id != 0)
        {
            var catTitle = m.category.name ?? m.category.title ?? m.category.slug ?? "";
            UpsertCategory(m.category.id, catTitle, m.category.slug ?? "", m.category.color_class);
        }

        using (var del = c.CreateCommand())
        {
            del.CommandText = "DELETE FROM mod_owners WHERE mod_id=$m";
            del.Parameters.AddWithValue("$m", m.id);
            del.ExecuteNonQuery();
        }

        if (m.owner is not null)
        {
            UpsertOwner(m.owner.id, m.owner.name ?? "");
            using var ins = c.CreateCommand();
            ins.CommandText = "INSERT OR IGNORE INTO mod_owners(mod_id,owner_id) VALUES($m,$o)";
            ins.Parameters.AddWithValue("$m", m.id);
            ins.Parameters.AddWithValue("$o", m.owner.id);
            ins.ExecuteNonQuery();
        }

        if (m.authors is not null)
            foreach (var a in m.authors)
            {
                if (a is null) continue;
                UpsertOwner(a.id, a.name ?? "");
                using var ins2 = c.CreateCommand();
                ins2.CommandText = "INSERT OR IGNORE INTO mod_owners(mod_id,owner_id) VALUES($m,$o)";
                ins2.Parameters.AddWithValue("$m", m.id);
                ins2.Parameters.AddWithValue("$o", a.id);
                ins2.ExecuteNonQuery();
            }

        if (m.versions is not null)
            foreach (var v in m.versions)
                UpsertVersion(m.id, v);

        using (var delSrc = c.CreateCommand())
        {
            delSrc.CommandText = "DELETE FROM mod_sources WHERE mod_id=$m";
            delSrc.Parameters.AddWithValue("$m", m.id);
            delSrc.ExecuteNonQuery();
        }

        if (m.source_code_links is not null)
            foreach (var s in m.source_code_links)
            {
                if (s is null || string.IsNullOrWhiteSpace(s.url)) continue;
                using var insS = c.CreateCommand();
                insS.CommandText = "INSERT OR REPLACE INTO mod_sources(mod_id,url,label) VALUES($m,$u,$l)";
                insS.Parameters.AddWithValue("$m", m.id);
                insS.Parameters.AddWithValue("$u", s.url);
                insS.Parameters.AddWithValue("$l", s.label ?? (object)DBNull.Value);
                insS.ExecuteNonQuery();
            }
    }

    private List<ForgeClient.SourceLink> GetSourcesForMod(SqliteConnection c, int modId)
    {
        var list = new List<ForgeClient.SourceLink>();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT url,label FROM mod_sources WHERE mod_id=$m ORDER BY url";
        cmd.Parameters.AddWithValue("$m", modId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var url = r.IsDBNull(0) ? "" : r.GetString(0);
            if (string.IsNullOrWhiteSpace(url)) continue;
            list.Add(new ForgeClient.SourceLink
            {
                url = url,
                label = r.IsDBNull(1) ? null : r.GetString(1)
            });
        }

        return list;
    }

    public void UpsertVersion(int modId, ForgeClient.ModVersion v)
    {
        var sptNorm = SemverUtil.NormalizeToThreeParts(v.SptVersionConstraint);
        using var c = Conn();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
INSERT INTO versions(id,mod_id,version,link,spt_version_constraint,downloads,published_at,spt_norm)
VALUES($id,$m,$ver,$link,$spt,$dl,$pub,$sptn)
ON CONFLICT(id) DO UPDATE SET
 version=excluded.version,
 link=excluded.link,
 spt_version_constraint=excluded.spt_version_constraint,
 downloads=excluded.downloads,
 published_at=excluded.published_at,
 spt_norm=excluded.spt_norm";
        cmd.Parameters.AddWithValue("$id", v.Id);
        cmd.Parameters.AddWithValue("$m", modId);
        cmd.Parameters.AddWithValue("$ver", v.Version ?? "");
        cmd.Parameters.AddWithValue("$link", v.Link ?? "");
        cmd.Parameters.AddWithValue("$spt", v.SptVersionConstraint ?? "");
        cmd.Parameters.AddWithValue("$dl", v.Downloads);
        cmd.Parameters.AddWithValue("$pub", v.PublishedAt?.ToString("o", CultureInfo.InvariantCulture) ?? "");
        cmd.Parameters.AddWithValue("$sptn", string.IsNullOrWhiteSpace(sptNorm) ? DBNull.Value : sptNorm);
        cmd.ExecuteNonQuery();
    }

    public List<(int id, string title, string slug, string colorClass)> GetCategories()
    {
        using var c = Conn();
        var list = new List<(int, string, string, string)>();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,title,slug,COALESCE(color_class,'') FROM categories ORDER BY title";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add((r.GetInt32(0), r.IsDBNull(1) ? "" : r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2), r.IsDBNull(3) ? "" : r.GetString(3)));
        return list;
    }

    public List<ModRow> GetAllModsBasic()
    {
        using var c = Conn();
        var list = new List<ModRow>();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id,guid,name,slug,teaser,thumbnail,downloads,detail_url,category_slug,category_name,updated_at,published_at FROM mods";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var row = new ModRow
            {
                Id = r.GetInt32(0),
                Guid = r.IsDBNull(1) ? "" : r.GetString(1),
                Name = r.IsDBNull(2) ? "" : r.GetString(2),
                Slug = r.IsDBNull(3) ? "" : r.GetString(3),
                Teaser = r.IsDBNull(4) ? "" : r.GetString(4),
                Thumbnail = r.IsDBNull(5) ? "" : r.GetString(5),
                Downloads = r.IsDBNull(6) ? 0 : r.GetInt64(6),
                DetailUrl = r.IsDBNull(7) ? "" : r.GetString(7),
                CategorySlug = r.IsDBNull(8) ? "" : r.GetString(8),
                CategoryName = r.IsDBNull(9) ? "" : r.GetString(9)
            };
            var upd = r.IsDBNull(10) ? "" : r.GetString(10);
            var pub = r.IsDBNull(11) ? "" : r.GetString(11);
            if (DateTimeOffset.TryParse(upd, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var u)) row.UpdatedAt = u;
            if (DateTimeOffset.TryParse(pub, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var p)) row.PublishedAt = p;

            row.Owners = GetOwnersForMod(c, row.Id);
            list.Add(row);
        }

        return list;
    }

    private List<string> GetOwnersForMod(SqliteConnection c, int modId)
    {
        var owners = new List<string>();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT o.name FROM owners o JOIN mod_owners mo ON o.id=mo.owner_id WHERE mo.mod_id=$m";
        cmd.Parameters.AddWithValue("$m", modId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            owners.Add(r.IsDBNull(0) ? "" : r.GetString(0));
        return owners;
    }

    public List<ForgeClient.ModVersion> GetVersionsForMod(int modId)
    {
        using var c = Conn();
        var list = new List<ForgeClient.ModVersion>();
        using var cmd = c.CreateCommand();
        cmd.CommandText =
            "SELECT id,version,link,spt_version_constraint,downloads,published_at FROM versions WHERE mod_id=$m ORDER BY COALESCE(published_at,'' ) DESC, id DESC";
        cmd.Parameters.AddWithValue("$m", modId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            DateTimeOffset? dto = null;
            var ps = r.IsDBNull(5) ? "" : r.GetString(5);
            if (DateTimeOffset.TryParse(ps, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)) dto = parsed;

            list.Add(new ForgeClient.ModVersion
            {
                Id = r.GetInt32(0),
                Version = r.IsDBNull(1) ? "" : r.GetString(1),
                Link = r.IsDBNull(2) ? "" : r.GetString(2),
                SptVersionConstraint = r.IsDBNull(3) ? "" : r.GetString(3),
                Downloads = r.IsDBNull(4) ? 0 : r.GetInt64(4),
                PublishedAt = dto
            });
        }

        return list;
    }

    public (List<string> majors, List<string> fulls) GetAllSptTags()
    {
        var majors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fulls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var c = Conn();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
            SELECT DISTINCT
                COALESCE(spt_version_constraint,'') AS raw,
                COALESCE(spt_norm,'')               AS norm
            FROM versions
            WHERE COALESCE(spt_version_constraint,'') <> ''
               OR COALESCE(spt_norm,'')               <> ''";

        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var raw = r.IsDBNull(0) ? "" : r.GetString(0);
            var norm = r.IsDBNull(1) ? "" : r.GetString(1);

            foreach (Match m in
                     Regex.Matches(raw, @"\b(\d+)\.(\d+)\.(\d+)\b"))
            {
                var a = m.Groups[1].Value;
                var b = m.Groups[2].Value;
                var cpart = m.Groups[3].Value;
                var full = $"{a}.{b}.{cpart}";
                fulls.Add(full);
                majors.Add($"{a}.{b}");
            }

            if (!string.IsNullOrWhiteSpace(norm))
            {
                if (SemverUtil.TryParseStrict(norm, out var v))
                    majors.Add($"{v.Major}.{v.Minor}");
            }
            else
            {
                if (SemverUtil.TryParseStrict(raw, out var v))
                {
                    fulls.Add($"{v.Major}.{v.Minor}.{v.Patch}");
                    majors.Add($"{v.Major}.{v.Minor}");
                }
            }
        }

        var majorsList = majors.ToList();
        majorsList.Sort(SemverUtil.CompareTagsDesc);

        var fullsList = fulls.ToList();
        fullsList.Sort(SemverUtil.CompareTagsDesc);

        return (majorsList, fullsList);
    }

    public string? GetLatestSPTVersion()
    {
        using var c = Conn();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
WITH v AS (
  -- Keep distinct, well-formed A.B.C rows only
  SELECT DISTINCT spt_norm
  FROM versions
  WHERE spt_norm IS NOT NULL
    AND spt_norm <> ''
    AND spt_norm GLOB '[0-9]*.[0-9]*.[0-9]*'
),
p AS (
  -- Find first dot (p1) and second dot relative to (p1+1) (p2rel)
  SELECT
    spt_norm,
    instr(spt_norm, '.') AS p1,
    instr(substr(spt_norm, instr(spt_norm, '.') + 1), '.') AS p2rel
  FROM v
  -- safety: require a second dot
  WHERE instr(spt_norm, '.') > 0
    AND instr(substr(spt_norm, instr(spt_norm, '.') + 1), '.') > 0
),
n AS (
  -- Parse majors/minor/patch as integers
  SELECT
    spt_norm,
    CAST(substr(spt_norm, 1, p1 - 1) AS INT)               AS maj,
    CAST(substr(spt_norm, p1 + 1, p2rel - 1) AS INT)       AS min,
    CAST(substr(spt_norm, (p1 + p2rel) + 1) AS INT)        AS pat
  FROM p
)
SELECT spt_norm
FROM n
ORDER BY maj DESC, min DESC, pat DESC
LIMIT 1;";
        var r = cmd.ExecuteScalar();
        return r == null || r == DBNull.Value ? null : Convert.ToString(r);
    }

    public async Task RefreshAllAsync(
        IProgress<(string phase, int current, int total)>? progress = null,
        CancellationToken ct = default,
        int pageSize = 50,
        int maxPages = 1000)
    {
        try
        {
            Init();

            var sinceIso = GetMeta("mods_since_iso");
            var since = DateTimeOffset.MinValue;
            if (!string.IsNullOrWhiteSpace(sinceIso)) DateTimeOffset.TryParse(sinceIso, out since);

            progress?.Report(("Fetching categories", 0, 1));
            var cats = await ForgeClient.GetCategoriesAsync(ct).ConfigureAwait(false);
            foreach (var c in cats)
            {
                ct.ThrowIfCancellationRequested();
                UpsertCategory(c.id, c.title, c.slug, c.color_class);
            }

            progress?.Report(("Fetched categories", 1, 1));

            var cursorKey = "mods_cursor_page";
            var cursorSinceKey = "mods_cursor_since";
            var page = 1;
            if (int.TryParse(GetMeta(cursorKey, "1"), out var saved) && saved > 1 &&
                string.Equals(GetMeta(cursorSinceKey), sinceIso, StringComparison.Ordinal))
            {
                page = saved;
            }
            else
            {
                SetMeta(cursorSinceKey, sinceIso ?? "");
                SetMeta(cursorKey, "1");
            }

            var totalPages = 1;
            var newestSeen = since;

            var delayMs = 450;
            var rnd = new Random();

            while (page <= maxPages)
            {
                ct.ThrowIfCancellationRequested();

                progress?.Report(($"Fetching mods", page, Math.Max(totalPages, 1)));

                var chunk = await ForgeClient.GetModsPageAsync(
                    page, pageSize,
                    true, true, true, true,
                    "",
                    "-updated_at",
                    true,
                    ct).ConfigureAwait(false);

                if (page == 1) totalPages = Math.Min(maxPages, Math.Max(1, chunk.LastPage));

                if (chunk.Items.Count == 0) break;

                var allOlderOrEqual = true;
                foreach (var m in chunk.Items)
                {
                    ct.ThrowIfCancellationRequested();
                    UpsertMod(m);
                    if (m.updated_at is DateTimeOffset u)
                    {
                        if (u > newestSeen) newestSeen = u;
                        if (u > since) allOlderOrEqual = false;
                    }
                    else
                    {
                        allOlderOrEqual = false;
                    }
                }

                SetMeta(cursorKey, page.ToString());

                if (allOlderOrEqual) break;
                if (page >= chunk.LastPage) break;

                page++;

                await Task.Delay(delayMs, ct).ConfigureAwait(true);
                delayMs = Math.Min((int)(delayMs * 1.15) + 50, 1000);
            }

            if (newestSeen > since)
                SetMeta("mods_since_iso", newestSeen.ToString("o", CultureInfo.InvariantCulture));

            SetMeta(cursorKey, "1");

            progress?.Report(("Finishing up", 1, 1));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("[CacheDb] RefreshAllAsync failed: " + ex);
            Console.WriteLine("[CacheDb] RefreshAllAsync failed: " + ex);
            throw;
        }
    }

    public (int id, string? name, string? guid)? TryGetModById(int id)
    {
        using var c = Conn();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"SELECT id, COALESCE(name,''), COALESCE(guid,'') FROM mods WHERE id = $id LIMIT 1";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (r.Read())
        {
            var mid  = r.IsDBNull(0) ? 0  : r.GetInt32(0);
            var name = r.IsDBNull(1) ? "" : r.GetString(1);
            var guid = r.IsDBNull(2) ? "" : r.GetString(2);
            return (mid, string.IsNullOrWhiteSpace(name) ? null : name, string.IsNullOrWhiteSpace(guid) ? null : guid);
        }
        return null;
    }

    public async Task EnsureVersionsCachedAsync(int modId)
    {
        var have = GetVersionsForMod(modId);
        if (have.Count > 0) return;
        var all = await ForgeClient.GetAllVersionsAsync(modId, App.ShutdownToken).ConfigureAwait(false);
        foreach (var v in all) UpsertVersion(modId, v);
    }

    public async Task<ForgeClient.ModVersion?> GetLatestVersionForModNameAsync(string name)
    {
        using var c = Conn();
        using var cmd = c.CreateCommand();
        cmd.CommandText = @"
SELECT m.id FROM mods m
WHERE m.name = $n COLLATE NOCASE
LIMIT 1";
        cmd.Parameters.AddWithValue("$n", name);
        var idObj = cmd.ExecuteScalar();
        if (idObj is null) return null;

        var modId = Convert.ToInt32(idObj);
        var versions = GetVersionsForMod(modId);
        var best = versions
            .OrderByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue)
            .ThenByDescending(v => v.Downloads)
            .FirstOrDefault();
        if (best == null)
        {
            var all = await ForgeClient.GetAllVersionsAsync(modId, App.ShutdownToken).ConfigureAwait(false);
            foreach (var v in all) UpsertVersion(modId, v);

            versions = GetVersionsForMod(modId);
            best = versions
                .OrderByDescending(v => v.PublishedAt ?? DateTimeOffset.MinValue)
                .ThenByDescending(v => v.Downloads)
                .FirstOrDefault();
        }

        return best;
    }

    public List<ForgeClient.SourceLink> GetSourcesForMod(int modId)
    {
        using var c = Conn();
        return GetSourcesForMod(c, modId);
    }

    public void Close()
    {
        try
        {
            using var c = new SqliteConnection("Data Source=" + path);
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(FULL);";
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // good girl action
        }
        finally
        {
            try
            {
                SqliteConnection.ClearAllPools();
            }
            catch
            {
                // good girl action
            }
        }
    }

    public sealed class ModRow
    {
        public int Id { get; set; }
        public string Guid { get; set; } = "";
        public string Name { get; set; } = "";
        public string Slug { get; set; } = "";
        public string Teaser { get; set; } = "";
        public string Thumbnail { get; set; } = "";
        public long Downloads { get; set; }
        public string DetailUrl { get; set; } = "";
        public string CategorySlug { get; set; } = "";
        public string CategoryName { get; set; } = "";
        public DateTimeOffset? UpdatedAt { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
        public List<string> Owners { get; set; } = new();
    }
}