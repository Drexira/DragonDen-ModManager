using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DragonDen.ModManager.Services;

public static class HasInstalledBefore
{
    private static readonly object Gate = new();

    static HasInstalledBefore()
    {
        try
        {
            Logger.Info($"[HasInstalledBefore] Using path: {Paths.HasInstalledPath}");
            var dir = Path.GetDirectoryName(Paths.HasInstalledPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir!);
        }
        catch (Exception ex)
        {
            Logger.Error($"[HasInstalledBefore] Init failed: {ex}");
        }
    }

    public sealed class Installed
    {
        public string Key { get; set; } = "";
        public string Url { get; set; } = "";
        public long InstalledAt { get; set; }
    }

    public sealed class Store
    {
        public Dictionary<string, Installed> InstalledBefore { get; set; } = new(StringComparer.Ordinal);
    }

    private static string BuildHasInstalledKey(string? guid, string? name)
    {
        var g = (guid ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(g)) return g;
        var n = (name ?? "").Trim();
        return n.ToUpperInvariant();
    }

    private static Store Load()
    {
        lock (Gate)
        {
            var path = Paths.HasInstalledPath;
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir!);
                if (!File.Exists(path)) return new Store();

                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize(json, HasInstalledBeforeJsonContext.Default.Store) ?? new Store();
            }
            catch (Exception ex)
            {
                Logger.Error($"[HasInstalledBefore] Load failed for '{path}': {ex}");
                return new Store();
            }
        }
    }

    private static void Save(Store s)
    {
        lock (Gate)
        {
            var path = Paths.HasInstalledPath;
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir!);

                var json = JsonSerializer.Serialize(s, HasInstalledBeforeJsonContext.Default.Store);

                var tmp = path + ".tmp";
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var sw = new StreamWriter(fs))
                    sw.Write(json);

                if (File.Exists(path)) File.Replace(tmp, path, null);
                else File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                Logger.Error($"[HasInstalledBefore] Save failed for '{path}': {ex}");
            }
        }
    }

    public static bool HasModInstalledBefore(string? guid, string? name)
    {
        var key = BuildHasInstalledKey(guid, name);
        var s = Load();
        return s.InstalledBefore.ContainsKey(key);
    }

    public static void RecordModInstalled(string? guid, string? name, string? url)
    {
        var key = BuildHasInstalledKey(guid, name);
        var s = Load();
        s.InstalledBefore[key] = new Installed { Key = key, Url = url ?? "", InstalledAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds() };
        Save(s);
    }
}