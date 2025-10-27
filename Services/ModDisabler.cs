using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DragonDen.ModManager.Services
{
    public static class ModDisabler
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);

        private sealed record TargetCtx(string LiveRoot, string DisabledRoot, List<string> Files);

        public static Task<bool> IsDisabledAsync(long modId) => IsDisabledAsync(modId.ToString());

        public static async Task<bool> IsDisabledAsync(string modId)
        {
            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var targets = BuildTargets(modId);
                if (targets.Count == 0) return false;

                var anyLive = false;
                var anyDisabled = false;

                foreach (var t in targets)
                {
                    if (anyLive && anyDisabled) break;

                    foreach (var rel in t.Files)
                    {
                        var live = Path.Combine(t.LiveRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                        var dis  = Path.Combine(t.DisabledRoot, modId, rel.Replace('/', Path.DirectorySeparatorChar));

                        if (!anyLive && File.Exists(live)) anyLive = true;
                        if (!anyDisabled && File.Exists(dis)) anyDisabled = true;

                        if (anyLive && anyDisabled) break;
                    }
                }

                return !anyLive && anyDisabled;
            }
            catch (Exception ex)
            {
                Logger.Error("[ModDisabler] IsDisabled failed: " + ex.Message);
                return false;
            }
            finally
            {
                Gate.Release();
            }
        }

        public static Task DisableAsync(long modId, string modName) => DisableAsync(modId.ToString(), modName);

        public static async Task DisableAsync(string modId, string modName)
        {
            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var targets = BuildTargets(modId);
                if (targets.Count == 0) return;

                var sptRoot = App.Config.Paths.SptRoot;
                var moved = 0;
                using var scope = new DetailScope($"ModDisabler.Disable({modId})");

                foreach (var t in targets)
                {
                    foreach (var rel in t.Files)
                    {
                        var src = Path.Combine(t.LiveRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                        var dst = Path.Combine(t.DisabledRoot, modId, rel.Replace('/', Path.DirectorySeparatorChar));

                        EnsureDir(Path.GetDirectoryName(dst) ?? "");
                        if (MoveFileIfExistsQuiet(src, dst, scope))
                        {
                            moved++;
                            TryDeleteEmptyParentsQuiet(Path.GetDirectoryName(src) ?? "", sptRoot, scope);
                        }
                    }
                }

                var dirs = App.Db.ListDirsForModId(modId);
                foreach (var d in dirs.OrderByDescending(x => x.Length))
                {
                    var liveDir = Path.Combine(sptRoot, d.Replace('/', Path.DirectorySeparatorChar));
                    try
                    {
                        if (Directory.Exists(liveDir) &&
                            Directory.GetFileSystemEntries(liveDir).Length == 0 &&
                            !IsProtectedDir(sptRoot, liveDir))
                        {
                            Directory.Delete(liveDir);
                        }
                    }
                    catch (Exception ex)
                    {
                        scope.Detail($"dir delete warn '{liveDir}': {ex.Message}");
                    }

                    TryDeleteEmptyParentsQuiet(Path.GetDirectoryName(liveDir) ?? "", sptRoot, scope);
                }

                if (moved > 0)
                {
                    try
                    {
                        App.Db.SetDisabled(modId, true);
                    }
                    catch (Exception ex)
                    {
                        scope.Detail($"DB flag set warn: {ex.Message}");
                    }

                    Logger.Info($"[ModDisabler] Disabled '{modName}' ({modId}) - moved {moved} file(s).");
                }
                else
                {
                    Logger.Error($"[ModDisabler] No files moved - DB flag not changed for {modId}");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[ModDisabler] Disable failed: {ex.Message}");
                throw;
            }
            finally
            {
                Gate.Release();
            }
        }

        public static Task EnableAsync(long modId, string modName) => EnableAsync(modId.ToString(), modName);

        public static async Task EnableAsync(string modId, string modName)
        {
            await Gate.WaitAsync().ConfigureAwait(false);
            try
            {
                var targets = BuildTargets(modId);
                if (targets.Count == 0) return;

                var sptRoot = App.Config.Paths.SptRoot;
                var moved = 0;
                using var scope = new DetailScope($"ModDisabler.Enable({modId})");

                var dirs = App.Db.ListDirsForModId(modId);
                foreach (var d in dirs)
                {
                    var dstDir = Path.Combine(sptRoot, d.Replace('/', Path.DirectorySeparatorChar));
                    EnsureDir(dstDir);
                }

                foreach (var t in targets)
                {
                    foreach (var rel in t.Files)
                    {
                        var src = Path.Combine(t.DisabledRoot, modId, rel.Replace('/', Path.DirectorySeparatorChar));
                        var dst = Path.Combine(t.LiveRoot, rel.Replace('/', Path.DirectorySeparatorChar));

                        EnsureDir(Path.GetDirectoryName(dst) ?? "");
                        if (MoveFileIfExistsQuiet(src, dst, scope))
                        {
                            moved++;
                            TryDeleteEmptyParentsQuiet(Path.GetDirectoryName(src) ?? "", Path.Combine(t.DisabledRoot, modId), scope);
                        }
                    }

                    foreach (var d in dirs.OrderByDescending(x => x.Length))
                    {
                        var disDir = Path.Combine(t.DisabledRoot, modId, d.Replace('/', Path.DirectorySeparatorChar));
                        try
                        {
                            if (Directory.Exists(disDir) && Directory.GetFileSystemEntries(disDir).Length == 0)
                                Directory.Delete(disDir);
                        }
                        catch (Exception ex)
                        {
                            scope.Detail($"disabled dir delete warn '{disDir}': {ex.Message}");
                        }

                        TryDeleteEmptyParentsQuiet(Path.GetDirectoryName(disDir) ?? "", Path.Combine(t.DisabledRoot, modId), scope);
                    }

                    var perModRoot = Path.Combine(t.DisabledRoot, modId);
                    DeleteDirIfOnlyDirectories(perModRoot, scope);
                    TryDeleteEmptyParentsQuiet(perModRoot, t.DisabledRoot, scope);

                    var legacyClient = Path.Combine(t.DisabledRoot, "client", modId);
                    var legacyServer = Path.Combine(t.DisabledRoot, "server", modId);
                    DeleteDirIfOnlyDirectories(legacyClient, scope);
                    DeleteDirIfOnlyDirectories(legacyServer, scope);
                    TryDeleteEmptyParentsQuiet(Path.Combine(t.DisabledRoot, "client"), t.DisabledRoot, scope);
                    TryDeleteEmptyParentsQuiet(Path.Combine(t.DisabledRoot, "server"), t.DisabledRoot, scope);

                    TryDeleteEmptyParentsQuiet(t.DisabledRoot, Path.Combine(Paths.DataDir, "Disabled Mods"), scope);
                }

                if (moved > 0)
                {
                    try
                    {
                        App.Db.SetDisabled(modId, false);
                    }
                    catch (Exception ex)
                    {
                        scope.Detail($"DB flag clear warn: {ex.Message}");
                    }

                    Logger.Info($"[ModDisabler] Enabled '{modName}' ({modId}) - moved {moved} file(s).");
                }
                else
                {
                    Logger.Info($"[ModDisabler] No files restored for '{modName}' ({modId}); DB unchanged.");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[ModDisabler] Enable failed: {ex.Message}");
                throw;
            }
            finally
            {
                Gate.Release();
            }
        }

        private static List<TargetCtx> BuildTargets(string modId)
        {
            var list = new List<TargetCtx>();

            List<(string path, string target)> files;
            try
            {
                files = App.Db.ListFilesForModId(modId) ?? new List<(string, string)>();
            }
            catch (Exception ex)
            {
                Logger.Error("[ModDisabler] DB error: " + ex.Message);
                return list;
            }

            if (files.Count == 0) return list;

            var dbFolder = Path.GetFileNameWithoutExtension(Paths.ModsDbPath) ?? "default";
            var disabledRoot = Path.Combine(Paths.DataDir, "Disabled Mods", dbFolder);
            var liveRoot = App.Config.Paths.SptRoot;

            var rels = files.Select(x => (x.path ?? "").Replace('\\', '/'))
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (string.IsNullOrWhiteSpace(liveRoot) || rels.Count == 0) return list;

            list.Add(new TargetCtx(liveRoot, disabledRoot, rels));
            return list;
        }

        private static bool MoveFileIfExistsQuiet(string src, string dst, DetailScope scope)
        {
            try
            {
                if (!File.Exists(src)) { scope.Detail($"skip: !exists '{src}'"); return false; }

                if (File.Exists(dst))
                {
                    try { File.Delete(dst); } catch (Exception ex) { scope.Detail($"dst delete warn '{dst}': {ex.Message}"); }
                }

                EnsureDir(Path.GetDirectoryName(dst) ?? "");
                File.Move(src, dst);
                return true;
            }
            catch (Exception ex)
            {
                scope.Detail($"move failed → copy fallback: {src} -> {dst} ({ex.Message})");
                try
                {
                    if (!File.Exists(src)) return false;
                    EnsureDir(Path.GetDirectoryName(dst) ?? "");
                    File.Copy(src, dst, true);
                    try { File.Delete(src); } catch (Exception ex2) { scope.Detail($"src delete warn '{src}': {ex2.Message}"); }
                    return true;
                }
                catch (Exception ex2)
                {
                    scope.Fail($"copy fallback failed: {ex2}");
                    return false;
                }
            }
        }

        private static void EnsureDir(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            try
            {
                Directory.CreateDirectory(dir);
            }
            catch (Exception ex)
            {
                Logger.Error($"[ModDisabler] EnsureDir failed for '{dir}': {ex.Message}");
            }
        }
        
        private static void DeleteDirIfOnlyDirectories(string dir, DetailScope scope)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                var anyFile = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any();
                if (!anyFile)
                    Directory.Delete(dir, true);
            }
            catch (Exception ex)
            {
                scope.Detail($"DeleteDirIfOnlyDirectories warn '{dir}': {ex.Message}");
            }
        }

        private static void TryDeleteEmptyParentsQuiet(string start, string stopAt, DetailScope scope)
        {
            try
            {
                var stop = NormalizeDir(stopAt);
                var cur = NormalizeDir(start);
                while (!string.IsNullOrWhiteSpace(cur) && !string.Equals(cur, stop, StringComparison.OrdinalIgnoreCase))
                {
                    if (!Directory.Exists(cur)) break;
                    if (IsProtectedDir(stop, cur)) break;
                    if (Directory.GetFileSystemEntries(cur).Length != 0) break;
                    Directory.Delete(cur);
                    var next = Path.GetDirectoryName(cur);
                    if (string.IsNullOrWhiteSpace(next)) break;
                    cur = NormalizeDir(next);
                }
            }
            catch (Exception ex)
            {
                scope.Detail($"cleanup warn: {ex.Message}");
            }
        }

        private static string NormalizeDir(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return "";
            try
            {
                return Path.GetFullPath(p).TrimEnd(Path.DirectorySeparatorChar);
            }
            catch
            {
                return p.TrimEnd(Path.DirectorySeparatorChar); 
            }
        }
        
        private static bool IsProtectedDir(string baseRoot, string dirFull)
        {
            try
            {
                var rel = Path.GetRelativePath(baseRoot, dirFull).Replace('\\', '/').Trim('/');
                if (string.Equals(rel, "BepInEx/plugins", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(rel, "BepInEx/patchers", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(rel, "SPT/user/mods", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(rel, "user/mods", StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch { }
            return false;
        }

        private sealed class DetailScope : IDisposable
        {
            private readonly string _name;
            private readonly List<string> _lines = new();
            private bool _failed;
            private readonly bool _alwaysDump = false;

            public DetailScope(string name, bool alwaysDump = false)
            {
                _name = name;
                _alwaysDump = alwaysDump;
            }

            public void Detail(string msg) => _lines.Add(msg);
            public void Fail(string msg) { _failed = true; _lines.Add(msg); }

            public void Dispose()
            {
                if ((_failed || _alwaysDump) && _lines.Count > 0)
                {
                    Logger.Error($"[{_name}] details:");
                    foreach (var l in _lines) Logger.Error("  " + l);
                }
            }
        }
    }
}
