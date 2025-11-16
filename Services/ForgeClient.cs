using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DragonDen.ModManager.Services;

public static class ForgeClient
{
    private static readonly SemaphoreSlim _rateGate = new(1, 1);
    private static readonly TimeSpan _perRequestDelay = TimeSpan.FromMilliseconds(550);

    private static readonly ConcurrentDictionary<string, Lazy<Task<FetchResult>>> _inflight = new();
    private static readonly ConcurrentDictionary<string, CacheEntry> _cache = new();

    private static void RaiseStatus(string msg)
    {
        try
        {
            StatusMessage?.Invoke(msg);
        }
        catch (Exception ex)
        {
            Logger.Error($"[ForgeClient] Exception in StatusMessage handler: {ex}");
        }
    }

    private static readonly HttpClient http = new()
    {
        Timeout = TimeSpan.FromSeconds(100)
    };

    private static string BaseUrl => App.Config.Forge.BaseUrl?.TrimEnd('/') ?? "https://forge.sp-tarkov.com";
    public static event Action<string>? StatusMessage;

    private static HttpRequestMessage NewGet(string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrWhiteSpace(App.Config.Forge.Token))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", App.Config.Forge.Token);
        req.Headers.Accept.ParseAdd("application/json");
        return req;
    }

    public static string ResolveImageUrl(string? pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl)) return "";
        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out _)) return pathOrUrl!;
        return $"{BaseUrl}/{pathOrUrl!.TrimStart('/')}";
    }

    private static async Task DelayWithStatus(
        TimeSpan delay,
        CancellationToken ct,
        Func<TimeSpan, string> messageForRemaining)
    {
        var deadline = DateTimeOffset.UtcNow + delay;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero) break;

            var secs = (int)Math.Ceiling(remaining.TotalSeconds);
            if (secs < 1) secs = 1;

            RaiseStatus(messageForRemaining(TimeSpan.FromSeconds(secs)));

            var step = TimeSpan.FromSeconds(1);
            if (remaining < step) step = remaining;

            await Task.Delay(step, ct);
        }
    }

    private static async Task<JsonDocument> FetchJsonWithRetries(string url, CancellationToken ct)
    {
        const int maxRetries = 10;
        var attempt = 0;
        Exception? last = null;

        while (attempt < maxRetries)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var res = await GetResultDeDupedAsync(url, ct).ConfigureAwait(false);

                if (res.Status == (int)HttpStatusCode.Unauthorized || res.Status == (int)HttpStatusCode.Forbidden)
                {
                    var bodyAuth = Encoding.UTF8.GetString(res.Bytes);
                    RaiseStatus("Forge rejected the request (unauthenticated/forbidden).");
                    throw new HttpRequestException($"HTTP {res.Status} while GET {url}\n" +
                                                   "Forge API rejected the request (unauthenticated/forbidden). " +
                                                   "Check your API token.\n" + bodyAuth);
                }

                if (res.Status == 1015)
                {
                    var retry = res.RetryAfter ?? TimeSpan.FromSeconds(4 * Math.Pow(2, attempt));
                    await DelayWithStatus(retry, ct, rem => $"Cloudflare rate limited - retrying in {rem.Seconds}s...");
                    attempt++;
                    continue;
                }

                if (res.Status == 429)
                {
                    var retry = res.RetryAfter ?? TimeSpan.FromSeconds(2 * Math.Pow(2, attempt));
                    await DelayWithStatus(retry, ct, rem => $"Cloudflare rate limited - retrying in {rem.Seconds}s...");
                    attempt++;
                    continue;
                }

                if (res.Status is >= 500 and < 600)
                {
                    var retry = TimeSpan.FromSeconds(1.5 * Math.Pow(2, attempt));
                    await DelayWithStatus(retry, ct, rem => $"Cloudflare error {res.Status} - retrying in {rem.Seconds}s...");
                    attempt++;
                    continue;
                }

                if (res.Status is < 200 or >= 300)
                {
                    var body = Encoding.UTF8.GetString(res.Bytes);
                    throw new HttpRequestException($"HTTP {res.Status} while GET {url}\n{body}");
                }

                using var s = new MemoryStream(res.Bytes, false);
                return await JsonDocument.ParseAsync(s, cancellationToken: ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (HttpRequestException ex) when (++attempt <= maxRetries)
            {
                last = ex;
                var retry = TimeSpan.FromMilliseconds(300 * Math.Pow(2, attempt - 1));
                await DelayWithStatus(retry, ct, rem => $"Network trouble - retrying in {rem.Seconds}s...");
            }
            catch (Exception ex) when (++attempt <= maxRetries)
            {
                last = ex;
                var retry = TimeSpan.FromMilliseconds(300 * Math.Pow(2, attempt - 1));
                await DelayWithStatus(retry, ct, rem => $"Network trouble - retrying in {rem.Seconds}s...");
            }
        }

        throw new HttpRequestException($"Failed to GET {url} after {maxRetries} attempts", last);
    }

    private static TimeSpan? TryGetRetryAfter(HttpResponseMessage res)
    {
        if (res.Headers.RetryAfter?.Delta is TimeSpan d) return d;
        if (res.Headers.RetryAfter?.Date is DateTimeOffset when)
        {
            var delta = when - DateTimeOffset.UtcNow;
            if (delta > TimeSpan.Zero && delta < TimeSpan.FromMinutes(5)) return delta;
        }

        return null;
    }

    public static async Task<List<ModDependency>> GetDependenciesForVersionAsync(
        int modId, int versionId, CancellationToken ct = default)
    {
        string ts()
        {
            return DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }

        var url =
            $"{BaseUrl}/api/v0/mod/{modId}/versions" +
            $"?include=dependencies&filter[id]={versionId}&per_page=1";

        using var doc = await FetchJsonWithRetries(url, ct);

        var deps = new List<ModDependency>();
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            var first = data.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object &&
                first.TryGetProperty("dependencies", out var depsEl) &&
                depsEl.ValueKind == JsonValueKind.Array)
                foreach (var d in depsEl.EnumerateArray())
                {
                    if (d.ValueKind != JsonValueKind.Object) continue;
                    deps.Add(new ModDependency
                    {
                        id = d.GetPropertyOrDefault("id", 0),
                        mod_id = d.GetPropertyOrDefault("mod_id", 0),
                        mod_guid = d.GetPropertyOrDefault("mod_guid", (string?)null),
                        mod_name = d.GetPropertyOrDefault("mod_name", (string?)null),
                        version_constraint = d.GetPropertyOrDefault("version_constraint", (string?)null),
                        is_optional = d.GetPropertyOrDefault("is_optional", false)
                    });
                }
        }

        return deps;
    }

    public static async Task<List<ModDependency>> GetDependenciesForLatestAsync(
        int modId, string? sptConstraint = null, CancellationToken ct = default)
    {
        string ts()
        {
            return DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        }

        var query = "include=dependencies&per_page=1&sort=-published_at";
        if (!string.IsNullOrWhiteSpace(sptConstraint))
            query += $"&filter[spt_version]={Uri.EscapeDataString(sptConstraint)}";

        var url = $"{BaseUrl}/api/v0/mod/{modId}/versions?{query}";

        using var doc = await FetchJsonWithRetries(url, ct);

        var deps = new List<ModDependency>();
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            var first = data.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.Object &&
                first.TryGetProperty("dependencies", out var depsEl) &&
                depsEl.ValueKind == JsonValueKind.Array)
                foreach (var d in depsEl.EnumerateArray())
                {
                    if (d.ValueKind != JsonValueKind.Object) continue;
                    deps.Add(new ModDependency
                    {
                        id = d.GetPropertyOrDefault("id", 0),
                        mod_id = d.GetPropertyOrDefault("mod_id", 0),
                        mod_guid = d.GetPropertyOrDefault("mod_guid", (string?)null),
                        mod_name = d.GetPropertyOrDefault("mod_name", (string?)null),
                        version_constraint = d.GetPropertyOrDefault("version_constraint", (string?)null),
                        is_optional = d.GetPropertyOrDefault("is_optional", false)
                    });
                }
        }

        return deps;
    }

    public static async Task<List<Category>> GetCategoriesAsync(CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/api/v0/mod-categories";
        using var doc = await FetchJsonWithRetries(url, ct);
        var list = new List<Category>();
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            foreach (var c in data.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                list.Add(new Category
                {
                    id = c.GetPropertyOrDefault("id", 0),
                    title = c.GetPropertyOrDefault("title", ""),
                    slug = c.GetPropertyOrDefault("slug", ""),
                    color_class = c.GetPropertyOrDefault("color_class", "")
                });
            }

        return list;
    }

    public static async Task<ModSummary?> GetModAsync(
        int modId,
        bool includeCategory = true,
        bool includeVersions = false,
        bool includeSourceLinks = false,
        CancellationToken ct = default)
    {
        var includes = new List<string>();

        if (includeCategory) includes.Add("category");
        if (includeVersions) includes.Add("versions");
        if (includeSourceLinks) includes.Add("source_code_links");

        var inc = includes.Count > 0 ? "?include=" + string.Join(",", includes) : "";

        var url = $"{BaseUrl}/api/v0/mod/{modId}{inc}";
        using var doc = await FetchJsonWithRetries(url, ct);

        var root = doc.RootElement;
        var el = root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object ? data : root;
        if (el.ValueKind != JsonValueKind.Object) return null;

        DateTimeOffset? upd = null;
        if (el.TryGetProperty("updated_at", out var updEl) && updEl.ValueKind == JsonValueKind.String)
            if (DateTimeOffset.TryParse(updEl.GetString(), out var u))
                upd = u;

        DateTimeOffset? pub = null;
        if (el.TryGetProperty("published_at", out var pubEl) && pubEl.ValueKind == JsonValueKind.String)
            if (DateTimeOffset.TryParse(pubEl.GetString(), out var p))
                pub = p;

        return new ModSummary
        {
            id = el.GetPropertyOrDefault("id", 0),
            name = el.GetPropertyOrDefault("name", ""),
            guid = el.GetPropertyOrDefault("guid", (string?)null),
            teaser = el.GetPropertyOrDefault("teaser", (string?)null),
            slug = el.GetPropertyOrDefault("slug", (string?)null),
            downloads = el.GetPropertyOrDefault("downloads", 0L),
            thumbnail = ResolveImageUrl(el.GetPropertyOrDefault("thumbnail", (string?)null)),
            detail_url = el.GetPropertyOrDefault("detail_url", (string?)null),
            featured = el.GetPropertyAsBool("featured", false),
            contains_ads = el.GetPropertyAsBool("contains_ads", false),
            contains_ai_content = el.GetPropertyAsBool("contains_ai_content", false),
            fika_compatibility = el.GetPropertyAsBool("fika_compatibility", false),
            versions = ParseVersions(el.TryGetProperty("versions", out var vEl) ? vEl : default),
            owner = ParsePerson(el.TryGetProperty("owner", out var ow) ? ow : default),
            authors = el.TryGetProperty("additional_authors", out var auNew)
                ? ParsePersons(auNew)
                : (el.TryGetProperty("authors", out var auOld) ? ParsePersons(auOld) : null),
            category = ParseCategory(el.TryGetProperty("category", out var cat) ? cat : default),
            updated_at = upd,
            published_at = pub,
            source_code_links = ParseSourceLinks(el.TryGetProperty("source_code_links", out var src) ? src : default)
        };
    }

    public static async Task<PagedMods> GetModsPageAsync(
        int page,
        int perPage,
        bool includeVersions,
        bool includeCategory,
        string query,
        string sortApi,
        bool includeSourceLinks = false,
        CancellationToken ct = default)
    {
        var includes = new List<string>();
        if (includeCategory) includes.Add("category");
        if (includeVersions) includes.Add("versions");
        if (includeSourceLinks) includes.Add("source_code_links");
        var inc = includes.Count > 0 ? "&include=" + string.Join(",", includes) : "";

        var q = string.IsNullOrWhiteSpace(query) ? "" : "&query=" + Uri.EscapeDataString(query);
        var url = $"{BaseUrl}/api/v0/mods?per_page={perPage}&page={page}&sort={Uri.EscapeDataString(sortApi)}{inc}{q}";
        using var doc = await FetchJsonWithRetries(url, ct);

        var list = new List<ModSummary>();
        int currentPage = 1, lastPage = 1, total = 0;

        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            foreach (var el in data.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();

                DateTimeOffset? upd = null;
                if (el.TryGetProperty("updated_at", out var updEl) && updEl.ValueKind == JsonValueKind.String)
                    if (DateTimeOffset.TryParse(updEl.GetString(), out var u))
                        upd = u;

                DateTimeOffset? pub = null;
                if (el.TryGetProperty("published_at", out var pubEl) && pubEl.ValueKind == JsonValueKind.String)
                    if (DateTimeOffset.TryParse(pubEl.GetString(), out var p))
                        pub = p;

                var mod = new ModSummary
                {
                    id = el.GetPropertyOrDefault("id", 0),
                    name = el.GetPropertyOrDefault("name", ""),
                    guid = el.GetPropertyOrDefault("guid", (string?)null),
                    teaser = el.GetPropertyOrDefault("teaser", (string?)null),
                    slug = el.GetPropertyOrDefault("slug", (string?)null),
                    downloads = el.GetPropertyOrDefault("downloads", 0L),
                    thumbnail = ResolveImageUrl(el.GetPropertyOrDefault("thumbnail", (string?)null)),
                    detail_url = el.GetPropertyOrDefault("detail_url", (string?)null),
                    featured = el.GetPropertyAsBool("featured", false),
                    contains_ads = el.GetPropertyAsBool("contains_ads", false),
                    contains_ai_content = el.GetPropertyAsBool("contains_ai_content", false),
                    fika_compatibility = el.GetPropertyAsBool("fika_compatibility", false),
                    versions = ParseVersions(el.TryGetProperty("versions", out var vEl) ? vEl : default),
                    owner = ParsePerson(el.TryGetProperty("owner", out var ow) ? ow : default),
                    authors = el.TryGetProperty("additional_authors", out var auNew)
                        ? ParsePersons(auNew)
                        : (el.TryGetProperty("authors", out var auOld) ? ParsePersons(auOld) : null),
                    category = ParseCategory(el.TryGetProperty("category", out var cat) ? cat : default),
                    updated_at = upd,
                    published_at = pub,
                    source_code_links = ParseSourceLinks(el.TryGetProperty("source_code_links", out var src) ? src : default)
                };
                list.Add(mod);
            }

        if (doc.RootElement.TryGetProperty("meta", out var meta) && meta.ValueKind == JsonValueKind.Object)
        {
            currentPage = meta.GetPropertyOrDefault("current_page", 1);
            lastPage = meta.GetPropertyOrDefault("last_page", currentPage);
            total = meta.GetPropertyOrDefault("total", 0);
        }

        return new PagedMods { Items = list, CurrentPage = currentPage, LastPage = lastPage, Total = total };
    }

    public static async Task<List<ModVersion>> GetAllVersionsAsync(int modId, CancellationToken ct = default, bool includeDependencies = false)
    {
        var include = includeDependencies ? "&include=dependencies" : "";
        var url = $"{BaseUrl}/api/v0/mod/{modId}/versions?per_page=50&sort=-published_at{include}";
        using var doc = await FetchJsonWithRetries(url, ct);

        var list = new List<ModVersion>();
        if (doc.RootElement.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            var parsed = ParseVersions(data);
            if (parsed != null) list.AddRange(parsed);
        }

        return list;
    }

    public static async Task<string> DownloadToTempAsync(string url, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var baseDir = string.IsNullOrWhiteSpace(App.Config.Paths.DataFolder) ? Paths.DataDir : App.Config.Paths.DataFolder;
        var tempDir = Path.Combine(baseDir, "downloads");
        Directory.CreateDirectory(tempDir);
        var fileName = GetFileNameFromUrl(url);
        var dst = Path.Combine(tempDir, fileName);

        const int maxRetries = 5;
        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                using var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);

                if ((int)res.StatusCode == 1015 || res.StatusCode == (HttpStatusCode)429)
                {
                    var retry = TryGetRetryAfter(res) ?? TimeSpan.FromSeconds(2 * Math.Pow(2, attempt));
                    await DelayWithStatus(retry, ct, rem => $"Cloudflare rate limited - retrying in {rem.Seconds}s...");
                    continue;
                }

                if (!res.IsSuccessStatusCode)
                {
                    if ((int)res.StatusCode >= 500 && (int)res.StatusCode < 600)
                    {
                        var retry = TimeSpan.FromSeconds(1.5 * Math.Pow(2, attempt));
                        await DelayWithStatus(retry, ct, rem => $"Forge error {(int)res.StatusCode} - retrying in {rem.Seconds}s...");
                        continue;
                    }

                    var bodyText = await res.Content.ReadAsStringAsync(ct);
                    throw new HttpRequestException($"HTTP {(int)res.StatusCode} while downloading {url}\n{bodyText}");
                }

                var total = res.Content.Headers.ContentLength ?? -1L;
                var canReport = total > 0 && progress != null;

                using var src = await res.Content.ReadAsStreamAsync(ct);
                using var dstFs = File.Create(dst);
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await src.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await dstFs.WriteAsync(buffer.AsMemory(0, n), ct);
                    read += n;
                    if (canReport)
                    {
                        var pct = (int)Math.Clamp(read * 100.0 / total, 0, 100);
                        progress!.Report(pct);
                    }
                }

                progress?.Report(100);
                return dst;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                var retry = TimeSpan.FromMilliseconds(300 * Math.Pow(2, attempt));
                await DelayWithStatus(retry, ct, rem => $"Network trouble - retrying in {rem.Seconds}s...");
            }
        }

        throw new IOException("Failed to download after multiple attempts: " + url);
    }

    private static SourceLink[]? ParseSourceLinks(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Array) return null;
        var list = new List<SourceLink>();
        foreach (var s in el.EnumerateArray())
        {
            if (s.ValueKind != JsonValueKind.Object) continue;
            var url = s.GetPropertyOrDefault("url", "");
            if (string.IsNullOrWhiteSpace(url)) continue;
            list.Add(new SourceLink
            {
                url = url,
                label = s.GetPropertyOrDefault("label", (string?)null)
            });
        }

        return list.Count == 0 ? null : list.ToArray();
    }

    private static string GetFileNameFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var last = Path.GetFileName(uri.LocalPath);
            return string.IsNullOrWhiteSpace(last) ? "download" : last;
        }
        catch
        {
            return "download";
        }
    }

    private static ModVersion[]? ParseVersions(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Array) return null;
        var list = new List<ModVersion>();

        foreach (var v in el.EnumerateArray())
        {
            DateTimeOffset? dto = null;
            if (v.TryGetProperty("published_at", out var pe) &&
                pe.ValueKind == JsonValueKind.String &&
                DateTimeOffset.TryParse(pe.GetString(), out var p))
                dto = p;

            var mv = new ModVersion
            {
                Id = v.GetPropertyOrDefault("id", 0),
                Version = v.GetPropertyOrDefault("version", (string?)null),
                Link = v.GetPropertyOrDefault("link", (string?)null),
                Description = v.GetPropertyOrDefault("description", (string?)null),
                SptVersionConstraint = v.GetPropertyOrDefault("spt_version_constraint", (string?)null),
                Downloads = v.GetPropertyOrDefault("downloads", 0L),
                PublishedAt = dto,
                ContentLength = v.GetPropertyOrDefault("content_length", 0L),
                FikaCompatibility = v.GetPropertyOrDefault("fika_compatibility", (string?)null),
                Dependencies = null
            };

            if (v.TryGetProperty("dependencies", out var depsEl) && depsEl.ValueKind == JsonValueKind.Array)
            {
                var deps = new List<ModDependency>();
                foreach (var d in depsEl.EnumerateArray())
                {
                    if (d.ValueKind != JsonValueKind.Object) continue;
                    deps.Add(new ModDependency
                    {
                        id = d.GetPropertyOrDefault("id", 0),
                        mod_id = d.GetPropertyOrDefault("mod_id", 0),
                        mod_guid = d.GetPropertyOrDefault("mod_guid", (string?)null),
                        mod_name = d.GetPropertyOrDefault("mod_name", (string?)null),
                        version_constraint = d.GetPropertyOrDefault("version_constraint", (string?)null),
                        is_optional = d.GetPropertyOrDefault("is_optional", false)
                    });
                }
                if (deps.Count > 0) mv.Dependencies = deps;
            }

            list.Add(mv);
        }

        return list.ToArray();
    }

    private static Person? ParsePerson(JsonElement el)
    {
        return el.ValueKind == JsonValueKind.Object
            ? new Person
            {
                id = el.GetPropertyOrDefault("id", 0),
                name = el.GetPropertyOrDefault("name", ""),
                profile_photo_url = el.GetPropertyOrDefault("profile_photo_url", (string?)null),
                cover_photo_url = el.GetPropertyOrDefault("cover_photo_url", (string?)null)
            }
            : null;
    }

    private static Person[]? ParsePersons(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Array) return null;
        var list = new List<Person>();
        foreach (var c in el.EnumerateArray())
            if (c.ValueKind == JsonValueKind.Object)
                list.Add(new Person
                {
                    id = c.GetPropertyOrDefault("id", 0),
                    name = c.GetPropertyOrDefault("name", ""),
                    profile_photo_url = c.GetPropertyOrDefault("profile_photo_url", (string?)null),
                    cover_photo_url = c.GetPropertyOrDefault("cover_photo_url", (string?)null)
                });

        return list.ToArray();
    }

    private static CategoryInfo? ParseCategory(JsonElement el)
    {
        return el.ValueKind == JsonValueKind.Object
            ? new CategoryInfo
            {
                id = el.GetPropertyOrDefault("id", 0),
                name = el.GetPropertyOrDefault("name", ""),
                title = el.GetPropertyOrDefault("title", ""),
                slug = el.GetPropertyOrDefault("slug", ""),
                color_class = el.GetPropertyOrDefault("color_class", "")
            }
            : null;
    }
    
    private static bool GetPropertyAsBool(this JsonElement el, string name, bool def)
    {
        if (!el.TryGetProperty(name, out var v)) return def;
        switch (v.ValueKind)
        {
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            case JsonValueKind.Number:
                return v.TryGetInt64(out var n) ? n != 0 : def;
            case JsonValueKind.String:
                var s = v.GetString();
                if (bool.TryParse(s, out var b)) return b;
                if (long.TryParse(s, out var n2)) return n2 != 0;
                return def;
            default:
                return def;
        }
    }

    private static T GetPropertyOrDefault<T>(this JsonElement el, string name, T def)
    {
        if (!el.TryGetProperty(name, out var v)) return def;
        try
        {
            var t = typeof(T);
            if (t == typeof(int))
            {
                if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i)) return (T)(object)i;
                if (v.ValueKind == JsonValueKind.True) return (T)(object)1;
                if (v.ValueKind == JsonValueKind.False) return (T)(object)0;
                if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var si)) return (T)(object)si;
                return def;
            }

            if (t == typeof(long))
            {
                if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l)) return (T)(object)l;
                if (v.ValueKind == JsonValueKind.True) return (T)(object)1L;
                if (v.ValueKind == JsonValueKind.False) return (T)(object)0L;
                if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var sl)) return (T)(object)sl;
                return def;
            }

            if (t == typeof(bool))
            {
                if (v.ValueKind == JsonValueKind.True) return (T)(object)true;
                if (v.ValueKind == JsonValueKind.False) return (T)(object)false;
                if (v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var nb)) return (T)(object)(nb != 0);
                if (v.ValueKind == JsonValueKind.String && bool.TryParse(v.GetString(), out var sb)) return (T)(object)sb;
                if (v.ValueKind == JsonValueKind.String && long.TryParse(v.GetString(), out var lb)) return (T)(object)(lb != 0);
                return def;
            }

            if (t == typeof(string))
            {
                if (v.ValueKind == JsonValueKind.String) return (T)(object)(v.GetString() ?? "");
                if (v.ValueKind == JsonValueKind.True) return (T)(object)"true";
                if (v.ValueKind == JsonValueKind.False) return (T)(object)"false";
                return (T)(object)v.GetRawText();
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"[ForgeClient] GetPropertyOrDefault('{name}', {typeof(T).Name}) fell back to default: {ex.Message}");
        }

        return def;
    }

    private static TimeSpan GetTtlFor(string url)
    {
        if (url.Contains("/api/v0/mod-categories", StringComparison.OrdinalIgnoreCase))
            return TimeSpan.FromMinutes(20);

        if (url.Contains("/api/v0/mod/", StringComparison.OrdinalIgnoreCase) && url.Contains("/versions", StringComparison.OrdinalIgnoreCase))
            return TimeSpan.FromMinutes(2);

        if (url.Contains("/api/v0/mods", StringComparison.OrdinalIgnoreCase))
            return TimeSpan.FromSeconds(30);

        return TimeSpan.FromSeconds(5);
    }

    private static async Task<FetchResult> GetResultDeDupedAsync(string url, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        if (_cache.TryGetValue(url, out var hit))
            if (now - hit.At < GetTtlFor(url))
            {
                return new FetchResult { Status = 200, Bytes = hit.Bytes };
            }

        var lazy = _inflight.GetOrAdd(url, _ => new Lazy<Task<FetchResult>>(() => FetchBytesCoreAsync(url, ct)));
        try
        {
            var res = await lazy.Value.ConfigureAwait(false);

            if (res.Status is >= 200 and < 300)
                _cache[url] = new CacheEntry(res.Bytes, DateTimeOffset.UtcNow);

            return res;
        }
        finally
        {
            _inflight.TryRemove(url, out _);
        }
    }

    private static async Task<FetchResult> FetchBytesCoreAsync(string url, CancellationToken ct)
    {
        await _rateGate.WaitAsync(ct);
        try
        {
            var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(80, 220));
            await Task.Delay(_perRequestDelay + jitter, ct);

            using var res = await http.SendAsync(NewGet(url), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

            var retryAfter =
                res.Headers.RetryAfter?.Delta ??
                (res.Headers.RetryAfter?.Date is DateTimeOffset when
                    ? when - DateTimeOffset.UtcNow
                    : null);

            if (!res.IsSuccessStatusCode)
            {
                var bytesErr = await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                var statusCode = (int)res.StatusCode;

                return new FetchResult { Status = statusCode, Bytes = bytesErr, RetryAfter = retryAfter };
            }

            var okBytes = await res.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            return new FetchResult { Status = (int)res.StatusCode, Bytes = okBytes, RetryAfter = retryAfter };
        }
        finally
        {
            _rateGate.Release();
        }
    }

    private sealed record CacheEntry(byte[] Bytes, DateTimeOffset At);

    private sealed class FetchResult
    {
        public byte[] Bytes = Array.Empty<byte>();
        public TimeSpan? RetryAfter;
        public int Status;
    }

    public sealed class MissingDep
    {
        public int ModId { get; init; }
        public string Name { get; init; } = "";
        public string? Guid { get; init; }
        public string? VersionConstraint { get; init; }
        public bool IsOptional { get; init; }
    }

    public sealed class ModVersion
    {
        public int Id { get; set; }
        public string? Version { get; set; }
        public string? Link { get; set; }
        public string? Description  { get; set; }
        public string? SptVersionConstraint { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
        public long Downloads { get; set; }
        public long ContentLength { get; set; }
        public string? FikaCompatibility { get; set; }
        public List<ModDependency>? Dependencies { get; set; }
    }

    public sealed class Person
    {
        public int id { get; set; }
        public string name { get; set; } = "";
        public string? profile_photo_url { get; set; }
        public string? cover_photo_url { get; set; }
    }

    public sealed class CategoryInfo
    {
        public int id { get; set; }
        public string? name { get; set; }
        public string? title { get; set; }
        public string? slug { get; set; }
        public string? color_class { get; set; }
    }

    public sealed class ModSummary
    {
        public int id { get; set; }
        public string name { get; set; } = "";
        public string? guid { get; set; }
        public string? teaser { get; set; }
        public string? slug { get; set; }
        public string? thumbnail { get; set; }
        public long downloads { get; set; }
        public string? detail_url { get; set; }
        public bool featured { get; set; }
        public bool contains_ads { get; set; }
        public bool contains_ai_content { get; set; }
        public bool fika_compatibility { get; set; }
        public Person? owner { get; set; }
        public Person[]? authors { get; set; }
        public CategoryInfo? category { get; set; }
        public ModVersion[]? versions { get; set; }
        public DateTimeOffset? updated_at { get; set; }
        public DateTimeOffset? published_at { get; set; }
        public SourceLink[]? source_code_links { get; set; }
        public ModVersion? latestVersion => versions is { Length: > 0 } ? versions[0] : null;
    }

    public sealed class ModDependency
    {
        public int id { get; set; }
        public int mod_id { get; set; }
        public string? mod_guid { get; set; }
        public string? mod_name { get; set; }
        public string? version_constraint { get; set; }
        public bool is_optional { get; set; }
    }

    public sealed class PagedMods
    {
        public List<ModSummary> Items { get; init; } = new();
        public int CurrentPage { get; init; }
        public int LastPage { get; init; }
        public int Total { get; init; }
    }

    public sealed class Category
    {
        public int id { get; set; }
        public string title { get; set; } = "";
        public string slug { get; set; } = "";
        public string color_class { get; set; } = "";
    }

    public sealed class SourceLink
    {
        public string url { get; set; } = "";
        public string? label { get; set; }
    }
}