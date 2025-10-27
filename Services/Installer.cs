using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DragonDen.ModManager.Services;

public static class Installer
{
    public enum Target
    {
        Client,
        Server
    }

    public static async Task<InstallResult> InstallAuto(string archivePath, SevenZip sevenZip,
        IProgress<(string phase, int pct)>? progress = null, InstallContext? ctx = null, CancellationToken ct = default)
    {
        var sptRoot = App.Config.Paths.SptRoot;
        if (string.IsNullOrWhiteSpace(sptRoot) || !Directory.Exists(sptRoot))
        {
            Notifications.Current.ShowError("Installation Failed", "SPT root is not configured or does not exist");
            return new InstallResult(0, 0);
        }

        progress?.Report(("inspect", -1));
        ct.ThrowIfCancellationRequested();
        var rawEntries = await Task.Run(() => sevenZip.ListEntries(archivePath), ct).ConfigureAwait(false);

        static string NormSlash(string s) => (s ?? "").Replace('\\', '/').TrimStart('/');

        bool HasTopLevelAllowed(IEnumerable<string> list)
        {
            foreach (var raw in list)
            {
                var p = NormSlash(raw);
                if (string.IsNullOrWhiteSpace(p)) continue;
                var top = p.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (top is null) continue;
                if (top.Equals("BepInEx", StringComparison.OrdinalIgnoreCase) ||
                    top.Equals("SPT", StringComparison.OrdinalIgnoreCase) ||
                    top.Equals("user", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        if (!HasTopLevelAllowed(rawEntries))
        {
            Notifications.Current.ShowError("Installation Failed",
                $"The mod '{Path.GetFileName(archivePath)}' is invalid or not structured properly, install cancelled.");
            Logger.Error($"[Installer] Top level check failed. entries={rawEntries.Count}");
            return new InstallResult(0, 0);
        }

        var entries = rawEntries.Select(NormSlash).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        progress?.Report(("extract", -1));
        ct.ThrowIfCancellationRequested();
        await sevenZip.ExtractAsync(archivePath, sptRoot).ConfigureAwait(false);

        var placedFiles = new List<string>();
        var placedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int filesExpected = 0, dirsExpected = 0, filesFound = 0, dirsFound = 0;
        var missingSample = new List<string>();

        foreach (var rel in entries)
        {
            var full = Path.Combine(sptRoot, rel);
            var looksLikeDir = rel.EndsWith("/", StringComparison.Ordinal) || rel.EndsWith("\\", StringComparison.Ordinal);
            if (looksLikeDir || !Path.GetFileName(rel).Contains('.'))
                dirsExpected++;
            else
                filesExpected++;

            if (File.Exists(full))
            {
                placedFiles.Add(rel);
                filesFound++;
                var dir = Path.GetDirectoryName(rel)?.Replace('\\', '/').TrimEnd('/');
                while (!string.IsNullOrEmpty(dir))
                {
                    placedDirs.Add(dir);
                    var next = Path.GetDirectoryName(dir);
                    if (string.IsNullOrEmpty(next)) break;
                    dir = next.Replace('\\', '/').TrimEnd('/');
                }
            }
            else if (Directory.Exists(full))
            {
                placedDirs.Add(rel.TrimEnd('/'));
                dirsFound++;
            }
            else if (missingSample.Count < 8)
            {
                missingSample.Add(rel);
            }
        }

        static bool IsServerPath(string rel)
        {
            var u = NormSlash(rel);
            return u.StartsWith("spt/user/mods/", StringComparison.OrdinalIgnoreCase)
                   || u.Equals("spt/user/mods", StringComparison.OrdinalIgnoreCase)
                   || u.StartsWith("user/mods/", StringComparison.OrdinalIgnoreCase)
                   || u.Equals("user/mods", StringComparison.OrdinalIgnoreCase);
        }

        var clientFiles = new List<string>();
        var serverFiles = new List<string>();
        foreach (var f in placedFiles)
        {
            if (IsServerPath(f)) serverFiles.Add(f);
            else clientFiles.Add(f);
        }

        var clientDirs = new List<string>();
        var serverDirs = new List<string>();
        foreach (var d in placedDirs)
        {
            if (IsServerPath(d)) serverDirs.Add(d);
            else clientDirs.Add(d);
        }
        
        var name = ctx?.Name ?? Path.GetFileNameWithoutExtension(archivePath);
        var version = string.IsNullOrWhiteSpace(ctx?.Version) ? "Custom Install" : ctx!.Version!;
        var guid = ctx?.Guid ?? "";
        var sourceUrl = ctx?.SourceUrl ?? "";

        string San(string s) => new string((s ?? "").Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.').ToArray());

        string ResolveModId(Target t)
        {
            if (!string.IsNullOrWhiteSpace(ctx?.FixedModId)) return ctx!.FixedModId!;
            var baseKey = !string.IsNullOrWhiteSpace(guid) ? guid : San(name);
            return $"{baseKey}-{(t == Target.Client ? "client" : "server")}".ToLowerInvariant();
        }

        if (clientFiles.Count > 0 || clientDirs.Count > 0)
            App.Db.RecordInstallWithSource(
                ResolveModId(Target.Client),
                name, version, clientFiles, sptRoot, Target.Client, sourceUrl, guid, clientDirs);

        if (serverFiles.Count > 0 || serverDirs.Count > 0)
            App.Db.RecordInstallWithSource(
                ResolveModId(Target.Server),
                name, version, serverFiles, sptRoot, Target.Server, sourceUrl, guid, serverDirs);

        var result = new InstallResult(clientFiles.Count, serverFiles.Count);
        App.NotifyInstallsChanged();
        return result;
    }

    private static string San(string s)
    {
        return new string((s ?? "").Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.').ToArray());
    }

    private static string CreateStableModId(Target target, InstallContext? ctx, string archivePath)
    {
        var baseKey =
            !string.IsNullOrWhiteSpace(ctx?.Guid) ? ctx!.Guid.Trim()
            : !string.IsNullOrWhiteSpace(ctx?.Name) ? ctx!.Name.Trim()
            : Path.GetFileNameWithoutExtension(archivePath);

        var clean = new string(baseKey.Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.').ToArray());
        if (string.IsNullOrWhiteSpace(clean)) clean = "mod";

        var t = target == Target.Client ? "client" : "server";
        return $"{clean}-{t}".ToLowerInvariant();
    }

    private static Intent DetectIntent(List<string> entries)
    {
        var intent = new Intent();
        foreach (var e in entries)
        {
            var p = e.Replace('\\', '/');

            if (p.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase) ||
                p.Contains("/plugins/", StringComparison.OrdinalIgnoreCase))
            {
                intent.clientLikely = true;
                intent.clientRoots.Add("BepInEx/plugins");
                intent.clientRoots.Add("plugins");
            }

            if (p.StartsWith("SPT/user/mods/", StringComparison.OrdinalIgnoreCase) ||
                p.StartsWith("user/mods/", StringComparison.OrdinalIgnoreCase))
            {
                intent.serverLikely = true;
                intent.serverRoots.Add("SPT/user/mods");
                intent.serverRoots.Add("user/mods");
            }

            var top = p.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
            if (string.Equals(top, "BepInEx", StringComparison.OrdinalIgnoreCase)) intent.clientLikely = true;
            if (string.Equals(top, "SPT", StringComparison.OrdinalIgnoreCase)) intent.serverLikely = true;
        }

        if (intent.clientLikely && intent.clientRoots.Count == 0) intent.clientRoots.Add("BepInEx/plugins");
        if (intent.serverLikely && intent.serverRoots.Count == 0) intent.serverRoots.Add("SPT/user/mods");
        return intent;
    }

    private static bool TryMap(string unixPath, IReadOnlyList<string> roots, out string afterRoot)
    {
        foreach (var r in roots)
        {
            var norm = r.Replace('\\', '/').Trim('/');
            if (norm.Length == 0) continue;
            var pref = norm + "/";
            if (unixPath.StartsWith(pref, StringComparison.OrdinalIgnoreCase))
            {
                afterRoot = unixPath[pref.Length..];
                return true;
            }
        }

        afterRoot = unixPath;
        return false;
    }

    private static void Move(string stageRoot, string rel, string toFull)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(toFull)!);
        File.Move(Path.Combine(stageRoot, rel), toFull, true);
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(root, p));
    }

    public static HashSet<string> Snapshot(string root)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return set;

        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            try
            {
                var rel = Path.GetRelativePath(root, f).Replace('\\', '/');
                if (!string.IsNullOrWhiteSpace(rel)) set.Add(rel);
            }
            catch (Exception ex)
            {
                Logger.Error($"[Installer] Failed to snapshot file '{f}': {ex}");
            }

        return set;
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch (Exception ex)
        {
            Logger.Error($"[Installer] Failed to delete staging dir '{dir}': {ex}");
        }
    }

    private static string? TrimPrefix(string text, string prefix)
    {
        return text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? text[prefix.Length..] : null;
    }
    
    private static string GetBepInExRoot()
    {
        try
        {
            var p = Directory.GetParent(Spt.ClientModsPath);
            if (p != null) return p.FullName;
        }
        catch (Exception ex)
        {
            Logger.Error($"[Installer] Failed to get BepInEx root: {ex}");
        }
        return Path.GetFullPath(Path.Combine(Spt.ClientModsPath, ".."));
    }

    public sealed record InstallResult(int clientFiles, int serverFiles)
    {
        public string ToHuman()
        {
            if (clientFiles == 0 && serverFiles == 0) return "no files placed";
            if (clientFiles > 0 && serverFiles > 0) return $"installed: client {clientFiles}, server {serverFiles}";
            if (clientFiles > 0) return $"installed: client {clientFiles}";
            return $"installed: server {serverFiles}";
        }
    }

    public sealed class InstallContext
    {
        public string Name { get; init; } = "";
        public string Version { get; init; } = "";
        public string Guid { get; init; } = "";
        public string SourceUrl { get; init; } = "";
        public Target? PreferredTarget { get; init; }
        public string? FixedModId { get; init; }
    }

    private sealed class Intent
    {
        public readonly List<string> clientRoots = new();
        public readonly List<string> serverRoots = new();
        public bool clientLikely;
        public bool serverLikely;
    }
}