using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DragonDen.ModManager.Services;

namespace DragonDen.ModManager.Storage;

public static class CacheDb
{
    public static Task EnsureInitializedAsync()
    {
        App.Cache.Init();
        return Task.CompletedTask;
    }

    public static async Task RefreshModsIncrementalAsync(IProgress<(string phase, int current, int total)>? progress, CancellationToken ct)
    {
        await Task.Run(async () => { await App.Cache.RefreshAllAsync(progress, ct).ConfigureAwait(false); }, ct).ConfigureAwait(false);
    }

    public static async Task<QueryResult> QueryModsAsync(Query q, bool hideFeatured = false, bool hideAds = false, bool hideAi = false, CancellationToken ct = default)
    {
        var (items, total) = await App.Cache.QueryModsAsync(
            q.Text, q.Author, q.CategorySlug, q.SptConstraint, q.Sort,
            q.Page, q.PageSize, ct,
            hideFeatured, hideAds, hideAi
        ).ConfigureAwait(false);

        return new QueryResult { items = items, total = total };
    }

    public static Task<List<(int id, string title, string slug, string colorClass)>> GetCategoriesAsync()
    {
        return Task.FromResult(App.Cache.GetCategories());
    }

    public static Task<List<ForgeClient.ModVersion>> GetVersionsAsync(int modId)
    {
        return Task.FromResult(App.Cache.GetVersionsForMod(modId));
    }

    public static Task<ForgeClient.ModVersion?> GetLatestVersionForModNameAsync(string name)
    {
        return App.Cache.GetLatestVersionForModNameAsync(name);
    }

    public static Task<(List<string> majors, List<string> fulls)> GetAllSptTagsAsync()
    {
        return Task.FromResult(App.Cache.GetAllSptTags());
    }

    public static Task EnsureVersionsCachedAsync(int modId)
    {
        return App.Cache.EnsureVersionsCachedAsync(modId);
    }

    public static Task<List<ForgeClient.SourceLink>> GetSourcesForModAsync(int modId)
    {
        return Task.FromResult(App.Cache.GetSourcesForMod(modId));
    }

    public sealed class Query
    {
        public string Text { get; set; } = "";
        public string Author { get; set; } = "";
        public string CategorySlug { get; set; } = "";
        public string SptConstraint { get; set; } = "";
        public string Sort { get; set; } = "recent";
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 12;

        public bool FeaturedOnly { get; set; }
        public bool AdsOnly { get; set; }
        public bool AiOnly { get; set; }
    }

    public sealed class QueryResult
    {
        public List<ForgeClient.ModSummary> items { get; set; } = new();
        public int total { get; set; }
    }
}