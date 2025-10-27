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
        var stage = Path.Combine(string.IsNullOrWhiteSpace(App.Config.Paths.DataFolder) ? Paths.DataDir : App.Config.Paths.DataFolder, "stage",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);

        progress?.Report(("inspect", -1));
        ct.ThrowIfCancellationRequested();
        var entries = await Task.Run(() => sevenZip.ListEntries(archivePath), ct).ConfigureAwait(false);
        
        bool HasTopLevelAllowed(List<string> list)
        {
            foreach (var raw in list)
            {
                var p = (raw ?? "").Replace('\\', '/').TrimStart('/');
                if (string.IsNullOrWhiteSpace(p)) continue;
                var top = p.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (top == null) continue;
                if (top.Equals("BepInEx", StringComparison.OrdinalIgnoreCase) ||
                    top.Equals("SPT", StringComparison.OrdinalIgnoreCase) ||
                    top.Equals("user", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        if (!HasTopLevelAllowed(entries))
        {
            Notifications.Current.ShowError("Installation Failed",
                $"The mod '{Path.GetFileName(archivePath)}' is invalid or not structured properly, install cancelled.");
            Console.WriteLine(
                $"[Installer] Unsupported mod '{Path.GetFileName(archivePath)}' The mod '{archivePath}' does not have proper folder structure and should be reported to the mod author. (unless this is a 3rd party tool and not an actual mod)");
            return new InstallResult(0, 0);
        }

        var intent = DetectIntent(entries);

        progress?.Report(("extract", -1));
        ct.ThrowIfCancellationRequested();
        await sevenZip.ExtractAsync(archivePath, stage).ConfigureAwait(false);

        var movedClientRel = new List<string>();
        var movedServerRel = new List<string>();
        int movedClient = 0, movedServer = 0, processed = 0;

        var all = EnumerateFiles(stage).ToList();

        static void CopyOverwrite(string stageRoot, string rel, string toFull)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(toFull)!);
            var src = Path.Combine(stageRoot, rel);
            File.Copy(src, toFull, true);
            try
            {
                File.Delete(src);
            }
            catch
            {
            }
        }

        var allowClient = ctx?.PreferredTarget is null || ctx.PreferredTarget == Target.Client;
        var allowServer = ctx?.PreferredTarget is null || ctx.PreferredTarget == Target.Server;

        foreach (var rel in all)
        {
            ct.ThrowIfCancellationRequested();
            var unix = rel.Replace('\\', '/');

            if (allowClient && TryMap(unix, intent.clientRoots, out var afterClient))
            {
                CopyOverwrite(stage, rel, Path.Combine(Spt.ClientModsPath, afterClient));
                movedClientRel.Add(afterClient);
                movedClient++;
            }
            else if (allowServer && TryMap(unix, intent.serverRoots, out var afterServer))
            {
                CopyOverwrite(stage, rel, Path.Combine(Spt.ServerModsPath, afterServer));
                movedServerRel.Add(afterServer);
                movedServer++;
            }
            else if (allowClient && (
                         unix.StartsWith("BepInEx/patchers/", StringComparison.OrdinalIgnoreCase) ||
                         unix.Equals("BepInEx/patchers", StringComparison.OrdinalIgnoreCase) ||
                         unix.StartsWith("patchers/", StringComparison.OrdinalIgnoreCase) ||
                         unix.Equals("patchers", StringComparison.OrdinalIgnoreCase)))
            {
                var sub = TrimPrefix(unix, "BepInEx/patchers/") ?? TrimPrefix(unix, "patchers/") ?? "";
                var bepinRoot = GetBepInExRoot();
                var toFull = Path.Combine(bepinRoot, "patchers", sub.Replace('/', Path.DirectorySeparatorChar));
                CopyOverwrite(stage, rel, toFull);

                var relFromPlugins = string.IsNullOrWhiteSpace(sub) ? "../patchers" : "../patchers/" + sub;
                movedClientRel.Add(relFromPlugins.Replace('\\', '/'));
                movedClient++;
            }
            else if (allowClient && (unix.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase) ||
                                     unix.StartsWith("plugins/", StringComparison.OrdinalIgnoreCase)))
            {
                var after = TrimPrefix(unix, "BepInEx/plugins/") ?? TrimPrefix(unix, "plugins/") ?? unix;
                CopyOverwrite(stage, rel, Path.Combine(Spt.ClientModsPath, after));
                movedClientRel.Add(after);
                movedClient++;
            }
            else if (allowServer && (unix.StartsWith("SPT/user/mods/", StringComparison.OrdinalIgnoreCase) ||
                                     unix.StartsWith("user/mods/", StringComparison.OrdinalIgnoreCase)))
            {
                var after = TrimPrefix(unix, "SPT/user/mods/") ?? TrimPrefix(unix, "user/mods/") ?? unix;
                CopyOverwrite(stage, rel, Path.Combine(Spt.ServerModsPath, after));
                movedServerRel.Add(after);
                movedServer++;
            }
            else
            {
                if (allowClient && (!allowServer || intent.clientLikely))
                {
                    CopyOverwrite(stage, rel, Path.Combine(Spt.ClientModsPath, unix));
                    movedClientRel.Add(unix);
                    movedClient++;
                }
                else if (allowServer)
                {
                    CopyOverwrite(stage, rel, Path.Combine(Spt.ServerModsPath, unix));
                    movedServerRel.Add(unix);
                    movedServer++;
                }
            }

            processed++;
            var pct = all.Count == 0 ? 100 : (int)(processed * 100.0 / all.Count);
            progress?.Report(("install", pct));
        }

        var name = ctx?.Name ?? Path.GetFileNameWithoutExtension(archivePath);
        var version = string.IsNullOrWhiteSpace(ctx?.Version) ? "Custom Install" : ctx!.Version!;
        var guid = ctx?.Guid ?? "";
        var sourceUrl = ctx?.SourceUrl ?? "";

        string ResolveModId(Target t)
        {
            if (!string.IsNullOrWhiteSpace(ctx?.FixedModId)) return ctx!.FixedModId!;
            var baseKey = !string.IsNullOrWhiteSpace(guid) ? guid : San(name);
            return $"{baseKey}-{(t == Target.Client ? "client" : "server")}".ToLowerInvariant();
        }

        if (movedClientRel.Count > 0)
            App.Db.RecordInstallWithSource(
                ResolveModId(Target.Client),
                name, version, movedClientRel, Spt.ClientModsPath, Target.Client, sourceUrl, guid);

        if (movedServerRel.Count > 0)
            App.Db.RecordInstallWithSource(
                ResolveModId(Target.Server),
                name, version, movedServerRel, Spt.ServerModsPath, Target.Server, sourceUrl, guid);

        TryDeleteDir(stage);
        App.NotifyInstallsChanged();
        return new InstallResult(movedClient, movedServer);
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
                Console.WriteLine($"[Installer] Failed to snapshot file '{f}': {ex}");
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
            Console.WriteLine($"[Installer] Failed to delete staging dir '{dir}': {ex}");
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
            Console.WriteLine($"[Installer] Failed to get BepInEx root: {ex}");
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