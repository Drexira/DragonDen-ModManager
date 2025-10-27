using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DragonDen.ModManager.Services;

public static class InstalledScanner
{
    public static ImportStats ImportFromDisk()
    {
        int imported = 0, updated = 0, skipped = 0;

        var sptRoot = App.Config.Paths.SptRoot;
        if (string.IsNullOrWhiteSpace(sptRoot) || !Directory.Exists(sptRoot))
            return new ImportStats(0, 0, 0);

        var bepinPlugins = Path.Combine(sptRoot, "BepInEx", "plugins");
        var bepinPatchers = Path.Combine(sptRoot, "BepInEx", "patchers");
        var serverModsA = Path.Combine(sptRoot, "SPT", "user", "mods");
        var serverModsB = Path.Combine(sptRoot, "user", "mods");

        if (Directory.Exists(bepinPlugins))
            ScanRoot(sptRoot, bepinPlugins, "BepInEx/plugins", Installer.Target.Client, ref imported, ref updated, ref skipped);

        if (Directory.Exists(bepinPatchers))
            ScanRoot(sptRoot, bepinPatchers, "BepInEx/patchers", Installer.Target.Client, ref imported, ref updated, ref skipped);

        if (Directory.Exists(serverModsA))
            ScanRoot(sptRoot, serverModsA, "SPT/user/mods", Installer.Target.Server, ref imported, ref updated, ref skipped);

        if (Directory.Exists(serverModsB))
            ScanRoot(sptRoot, serverModsB, "user/mods", Installer.Target.Server, ref imported, ref updated, ref skipped);

        return new ImportStats(imported, updated, skipped);
    }

    private static void ScanRoot(string sptRoot, string scanRoot, string scanPrefix, Installer.Target target, ref int imported, ref int updated, ref int skipped)
    {
        var protectedPaths = App.Db.GetInstalledPathsForTarget(target);

        bool FolderIsProtected(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName)) return false;
            var pref = $"{scanPrefix.TrimEnd('/')}/{folderName.TrimEnd('/')}/".Replace('\\', '/');
            return protectedPaths.Any(p => p.StartsWith(pref, StringComparison.OrdinalIgnoreCase));
        }

        bool FileIsProtected(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            var rel = $"{scanPrefix.TrimEnd('/')}/{fileName}".Replace('\\', '/');
            return protectedPaths.Contains(rel);
        }

        var folderNames = Directory.EnumerateDirectories(scanRoot)
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var consumedDllStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dir in Directory.EnumerateDirectories(scanRoot))
        {
            var folderName = Path.GetFileName(dir);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                skipped++;
                continue;
            }

            if (folderName.Contains("spt", StringComparison.CurrentCultureIgnoreCase))
            {
                skipped++;
                continue;
            }

            if (FolderIsProtected(folderName))
            {
                skipped++;
                continue;
            }

            var dllsInFolder = Directory.EnumerateFiles(dir, "*.dll", SearchOption.TopDirectoryOnly).ToList();
            var dllStems = dllsInFolder.Select(d => Path.GetFileNameWithoutExtension(d)!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var candidates = new List<string>();
            candidates.AddRange(dllStems);
            if (!candidates.Contains(folderName, StringComparer.OrdinalIgnoreCase))
                candidates.Add(folderName);

            var canonicalName = folderName;
            var versionFromDb = "";
            foreach (var cand in candidates)
                if (App.Db.TryGetInstalled(cand, out var v))
                {
                    canonicalName = cand;
                    versionFromDb = v ?? "";
                    break;
                }

            if (App.Db.HasRealInstall(canonicalName))
            {
                skipped++;
                continue;
            }

            string version;
            if (!string.IsNullOrWhiteSpace(versionFromDb))
            {
                version = versionFromDb;
            }
            else
            {
                var chosenDll = dllsInFolder.FirstOrDefault(d =>
                    string.Equals(Path.GetFileNameWithoutExtension(d), canonicalName, StringComparison.OrdinalIgnoreCase));
                version = !string.IsNullOrWhiteSpace(chosenDll)
                    ? DllVersion.FromDll(chosenDll) ?? "Custom Install"
                    : DllVersion.DetectFromFolder(dir) ?? "Custom Install";
            }

            var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(dir, p).Replace('\\', '/'))
                .Select(p => $"{scanPrefix}/{folderName}/{p}".Replace('\\', '/'))
                .ToList();

            var dirs = Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(sptRoot, p).Replace('\\', '/'))
                .ToList();
            dirs.Insert(0, $"{scanPrefix}/{folderName}".Replace('\\', '/'));

            var possibleLooseDll = Path.Combine(scanRoot, canonicalName + ".dll");
            if (File.Exists(possibleLooseDll))
            {
                var rel = Path.GetFileName(possibleLooseDll);
                if (!FileIsProtected(rel))
                {
                    files.Add($"{scanPrefix}/{rel}".Replace('\\', '/'));
                    consumedDllStems.Add(canonicalName);
                }
            }

            if (files.Count == 0 && dirs.Count == 0)
            {
                skipped++;
                continue;
            }

            var detectedGuid =
                DllGuidExtractor.TryExtractModGuidFromFolder(dir)
                ?? "";

            var modId = BuildId(canonicalName, target);
            UpsertScan(modId, canonicalName, version, files, dirs, sptRoot, target, detectedGuid, ref imported, ref updated, ref skipped);
        }

        if (string.Equals(scanPrefix, "BepInEx/plugins", StringComparison.OrdinalIgnoreCase))
        {
            var looseDlls = Directory.EnumerateFiles(scanRoot, "*.dll", SearchOption.TopDirectoryOnly).ToList();
            foreach (var dll in looseDlls)
            {
                var stem = Path.GetFileNameWithoutExtension(dll);
                if (string.IsNullOrWhiteSpace(stem)) continue;
                if (folderNames.Contains(stem) || consumedDllStems.Contains(stem)) continue;

                var rel = Path.GetFileName(dll);
                if (FileIsProtected(rel))
                {
                    skipped++;
                    continue;
                }

                if (App.Db.HasRealInstall(stem))
                {
                    skipped++;
                    continue;
                }

                var version = App.Db.TryGetInstalled(stem, out var v) && !string.IsNullOrWhiteSpace(v)
                    ? v!
                    : DllVersion.FromDll(dll) ?? "Custom Install";

                var files = new List<string> { $"{scanPrefix}/{rel}".Replace('\\', '/') };
                var dirs = new List<string> { scanPrefix.Replace('\\', '/') };

                var detectedGuid =
                    DllGuidExtractor.TryExtractModGuidFromDll(dll)
                    ?? DllGuidExtractor.TryExtractServerModGuidFromDll(dll)
                    ?? "";

                var modId = BuildId(stem, target);
                UpsertScan(modId, stem, version, files, dirs, sptRoot, target, detectedGuid, ref imported, ref updated, ref skipped);
            }
        }
    }

    private static void UpsertScan(string modId, string name, string version, List<string> files, List<string> dirs, string root,
        Installer.Target target, string guid, ref int imported, ref int updated, ref int skipped)
    {
        var existing = App.Db.ListMods().FirstOrDefault(m =>
            string.Equals(m.name, name, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(existing.name))
        {
            if (App.Db.HasRealInstall(name)) { skipped++; return; }

            var changed = !string.Equals(existing.version, version, StringComparison.OrdinalIgnoreCase)
                          || existing.fileCount != files.Count;

            if (changed)
            {
                App.Db.RecordInstallDetailed(
                    modId, name, version, files, root, target,
                    "scan", "manual", guid ?? "", dirs);
                updated++;
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(guid) && string.IsNullOrWhiteSpace(existing.guid))
                    App.Db.RecordInstallDetailed(
                        modId, name, version, files, root, target,
                        "scan", "manual", guid ?? "", dirs);
                skipped++;
            }

            return;
        }

        App.Db.RecordInstallDetailed(
            modId, name, version, files, root, target,
            "scan", "manual", guid ?? "", dirs);
        imported++;
    }
    
    private static string Normalize(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var filtered = s.Where(ch => char.IsLetterOrDigit(ch)).ToArray();
        return new string(filtered).ToLowerInvariant();
    }

    private static string BuildId(string name, Installer.Target target)
    {
        return $"{San(name)}-{(target == Installer.Target.Client ? "client" : "server")}".ToLowerInvariant();
    }

    private static string San(string s)
    {
        var clean = new string(s.Where(ch => char.IsLetterOrDigit(ch) || ch == '-' || ch == '_').ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "mod" : clean;
    }

    public sealed record ImportStats(int imported, int updated, int skipped);
}