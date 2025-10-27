using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DragonDen.ModManager.Services;

public static class ModsDbRegistry
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var full = Path.GetFullPath(path);
        full = full.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full;
    }

    private static string Hash8(string text)
    {
        using var sha = SHA1.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
        return string.Concat(bytes.Take(4).Select(b => b.ToString("x2")));
    }

    private static Model Load()
    {
        try
        {
            var path = Paths.ModsRegistryPath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var m = JsonSerializer.Deserialize<Model>(json, JsonOpts);
                if (m is not null) return m;
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"[ModsDbRegistry] Failed to load registry: {ex}");
        }

        return new Model();
    }

    private static void Save(Model m)
    {
        try
        {
            var dir = Path.GetDirectoryName(Paths.ModsRegistryPath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Paths.ModsRegistryPath, JsonSerializer.Serialize(m, JsonOpts));
        }
        catch (Exception ex)
        {
            Logger.Error($"[ModsDbRegistry] Failed to save registry: {ex}");
        }
    }

    public static string GetDbPathFor(string sptRoot)
    {
        var norm = Normalize(sptRoot);
        var model = Load();

        var hit = model.Entries.FirstOrDefault(e => Normalize(e.SptRoot) == norm);
        if (hit is null)
        {
            var hash = Hash8(norm);
            var file = $"mods_{hash}.db";
            hit = new Entry
            {
                SptRoot = sptRoot ?? "",
                DbFile = file,
                Created = DateTimeOffset.UtcNow,
                LastUsed = DateTimeOffset.UtcNow
            };
            model.Entries.Add(hit);
        }
        else
        {
            hit.LastUsed = DateTimeOffset.UtcNow;
        }

        Save(model);

        Directory.CreateDirectory(Paths.ModsDir);

        return Path.Combine(Paths.ModsDir, hit.DbFile);
    }

    public sealed class Entry
    {
        public string SptRoot { get; set; } = "";
        public string DbFile { get; set; } = "";
        public DateTimeOffset Created { get; set; }
        public DateTimeOffset LastUsed { get; set; }
    }

    public sealed class Model
    {
        public List<Entry> Entries { get; set; } = new();
    }
}