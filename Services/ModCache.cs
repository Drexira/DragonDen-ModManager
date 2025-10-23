using System;
using System.Threading;
using System.Threading.Tasks;

namespace DragonDen.ModManager.Services;

public static class ModCache
{
    private static readonly SemaphoreSlim _gate = new(1, 1);
    private static volatile bool _warmed;

    public static bool IsWarm => _warmed;

    public static async Task EnsureWarmAsync()
    {
        if (_warmed) return;
        await _gate.WaitAsync();
        try
        {
            if (_warmed) return;
            await Storage.CacheDb.EnsureInitializedAsync();
            await Storage.CacheDb.RefreshModsIncrementalAsync(default, default);
            _warmed = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public static async Task RefreshAsync(IProgress<(string phase, int current, int total)>? progress = null, CancellationToken ct = default)
    {
        await Storage.CacheDb.RefreshModsIncrementalAsync(progress, ct);
        _warmed = true;
    }
}