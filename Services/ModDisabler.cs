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

        private sealed record TargetCtx(string TargetLabel, string LiveRoot, string DisabledRoot, List<string> Files);

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
                        var dis  = Path.Combine(t.DisabledRoot, t.TargetLabel, modId, rel.Replace('/', Path.DirectorySeparatorChar));

                        if (!anyLive && File.Exists(live)) anyLive = true;
                        if (!anyDisabled && File.Exists(dis)) anyDisabled = true;

                        if (anyLive && anyDisabled) break;
                    }
                }

                return !anyLive && anyDisabled;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[ModDisabler] IsDisabled failed: " + ex.Message);
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

                var moved = 0;
                using var scope = new DetailScope($"ModDisabler.Disable({modId})");

                foreach (var t in targets)
                {
                    foreach (var rel in t.Files)
                    {
                        var src = Path.Combine(t.LiveRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                        var dst = Path.Combine(t.DisabledRoot, t.TargetLabel, modId, rel.Replace('/', Path.DirectorySeparatorChar));

                        EnsureDir(Path.GetDirectoryName(dst) ?? "");
                        if (MoveFileIfExistsQuiet(src, dst, scope))
                        {
                            moved++;
                            TryDeleteEmptyParentsQuiet(Path.GetDirectoryName(src) ?? "", t.LiveRoot, scope);
                        }
                    }
                }

                if (moved > 0)
                {
                    try { App.Db.SetDisabled(modId, true); } catch (Exception ex) { scope.Detail($"DB flag set warn: {ex.Message}"); }
                    Console.WriteLine($"[ModDisabler] Disabled '{modName}' ({modId}) – moved {moved} file(s).");
                }
                else
                {
                    Console.WriteLine($"[ModDisabler] No files moved – DB flag not changed for {modId}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ModDisabler] Disable failed: {ex.Message}");
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

                var moved = 0;
                using var scope = new DetailScope($"ModDisabler.Enable({modId})");

                foreach (var t in targets)
                {
                    foreach (var rel in t.Files)
                    {
                        var src = Path.Combine(t.DisabledRoot, t.TargetLabel, modId, rel.Replace('/', Path.DirectorySeparatorChar));
                        var dst = Path.Combine(t.LiveRoot, rel.Replace('/', Path.DirectorySeparatorChar));

                        EnsureDir(Path.GetDirectoryName(dst) ?? "");
                        if (MoveFileIfExistsQuiet(src, dst, scope))
                        {
                            moved++;
                            TryDeleteEmptyParentsQuiet(Path.GetDirectoryName(src) ?? "", Path.Combine(t.DisabledRoot, t.TargetLabel, modId), scope);
                        }
                    }

                    TryDeleteEmptyParentsQuiet(Path.Combine(t.DisabledRoot, t.TargetLabel, modId), t.DisabledRoot, scope);
                }

                if (moved > 0)
                {
                    try { App.Db.SetDisabled(modId, false); } catch (Exception ex) { scope.Detail($"DB flag clear warn: {ex.Message}"); }
                    Console.WriteLine($"[ModDisabler] Enabled '{modName}' ({modId}) – moved {moved} file(s).");
                }
                else
                {
                    Console.WriteLine($"[ModDisabler] No files restored for '{modName}' ({modId}); DB unchanged.");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ModDisabler] Enable failed: {ex.Message}");
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
                Console.Error.WriteLine("[ModDisabler] DB error: " + ex.Message);
                return list;
            }

            if (files.Count == 0) return list;

            foreach (var grp in files.GroupBy(f => (f.target ?? "client").ToLowerInvariant()))
            {
                var label = grp.Key == "server" ? "server" : "client";
                var liveRoot = label == "server" ? Spt.ServerModsPath : Spt.ClientModsPath;
                if (string.IsNullOrWhiteSpace(liveRoot)) continue;

                var disabledRoot = Path.Combine(Paths.DataDir, "Disabled Mods");
                var rels = grp.Select(x => (x.path ?? "").Replace('\\', '/'))
                              .Where(p => !string.IsNullOrWhiteSpace(p))
                              .Distinct(StringComparer.OrdinalIgnoreCase)
                              .ToList();

                if (rels.Count == 0) continue;

                list.Add(new TargetCtx(label, liveRoot, disabledRoot, rels));
            }

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
                Console.WriteLine($"[ModDisabler] EnsureDir failed for '{dir}': {ex.Message}");
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
                    Console.Error.WriteLine($"[{_name}] details:");
                    foreach (var l in _lines) Console.Error.WriteLine("  " + l);
                }
            }
        }
    }
}
