using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using DragonDen.ModManager.Storage;

namespace DragonDen.ModManager.Services;

public sealed class InstallQueue
{
    private readonly Db db;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly SevenZip sevenZip;

    private readonly ConcurrentQueue<Func<Task>> work = new();
    private bool running;

    public InstallQueue(SevenZip sevenZip, Db db)
    {
        this.sevenZip = sevenZip;
        this.db = db;
    }

    private bool IsPaused { get; set; }

    public ObservableCollection<InstallJob> Jobs { get; } = new();

    public bool TogglePause()
    {
        IsPaused = !IsPaused;
        return IsPaused;
    }

    private static void UI(Action a)
    {
        Dispatcher.UIThread.Post(a);
    }

    public InstallJob EnqueueLocal(string archivePath)
    {
        var job = new InstallJob { Title = Path.GetFileName(archivePath), Source = "Local", StartedAt = DateTimeOffset.Now, Cts = new CancellationTokenSource() };
        Jobs.Add(job);
        work.Enqueue(() => RunLocal(job, archivePath, job.Cts!.Token));
        _ = Pump();
        return job;
    }

    public InstallJob EnqueueRemote(string name, string url, string version, string guid, string? fixedModId = null)
    {
        var job = new InstallJob { Title = name, Source = url, StartedAt = DateTimeOffset.Now, Cts = new CancellationTokenSource() };
        Jobs.Add(job);
        work.Enqueue(() => RunRemote(job, name, url, version, guid, fixedModId, job.Cts!.Token));
        _ = Pump();
        return job;
    }
    
    public InstallJob EnqueueEnable(string modName, List<string> modIds)
    {
        var job = new InstallJob { Title = $"Enable: {modName}", Source = "Enable", StartedAt = DateTimeOffset.Now, Cts = new CancellationTokenSource() };
        Jobs.Add(job);
        work.Enqueue(() => RunEnable(job, modName, modIds, job.Cts!.Token));
        _ = Pump();
        return job;
    }
    
    public InstallJob EnqueueDisable(string modName, List<string> modIds)
    {
        var job = new InstallJob { Title = $"Disable: {modName}", Source = "Disable", StartedAt = DateTimeOffset.Now, Cts = new CancellationTokenSource() };
        Jobs.Add(job);
        work.Enqueue(() => RunDisable(job, modName, modIds, job.Cts!.Token));
        _ = Pump();
        return job;
    }

    public InstallJob EnqueueUninstall(string modName, List<string> modIds)
    {
        var job = new InstallJob { Title = $"Uninstall: {modName}", Source = "Uninstall", StartedAt = DateTimeOffset.Now, Cts = new CancellationTokenSource() };
        Jobs.Add(job);
        work.Enqueue(() => RunUninstall(job, modName, modIds, job.Cts!.Token));
        _ = Pump();
        return job;
    }

    private async Task Pump()
    {
        if (running) return;
        running = true;
        await gate.WaitAsync().ConfigureAwait(false);
        try
        {
            while (work.TryDequeue(out var fn))
            {
                while (IsPaused)
                    await Task.Delay(150).ConfigureAwait(false);

                await fn().ConfigureAwait(false);
            }
        }
        finally
        {
            running = false;
            gate.Release();
        }
    }

    private async Task RunLocal(InstallJob job, string archive, CancellationToken ct)
    {
        try
        {
            UI(() => Notifications.Current.BindInstall(job));

            UI(() =>
            {
                job.Phase = "Extracting";
                job.SubTask = "Inspecting archive";
                job.IsIndeterminate = true;
                job.Progress = 0;
            });

            var lastReport = DateTimeOffset.MinValue;

            var result = await Installer.InstallAuto(
                archive,
                sevenZip,
                new Progress<(string phase, int pct)>(p =>
                {
                    var now = DateTimeOffset.Now;
                    if (now - lastReport < TimeSpan.FromMilliseconds(60) && p.pct is > 0 and < 100)
                        return;

                    lastReport = now;

                    UI(() =>
                    {
                        job.SubTask = p.phase switch
                        {
                            "inspect" => "Inspecting archive",
                            "extract" => "Extracting files",
                            "install" => "Placing files",
                            "move" => "Placing files",
                            _ => p.phase
                        };
                        job.SubPercent = p.pct < 0 ? 0 : p.pct;
                        job.IsIndeterminate = p.pct < 0;

                        var overall = p.phase is "inspect" or "extract"
                            ? Math.Clamp(p.pct, 0, 100) * 60 / 100
                            : 60 + Math.Clamp(p.pct, 0, 100) * 40 / 100;

                        job.Progress = Math.Clamp(overall, 0, 100);
                        job.Eta = ComputeEta(job.StartedAt, job.Progress);
                    });
                }),
                null,
                ct
            ).ConfigureAwait(false);

            UI(() =>
            {
                job.Phase = "Done";
                job.Status = result.ToHuman();
                job.IsIndeterminate = false;
                job.Progress = 100;
                job.SubPercent = 100;
                job.Eta = "";
                job.IsCancellable = false;
                job.IsCompleted = true;
                job.CompletedAt = DateTimeOffset.Now;
            });
            App.NotifyInstallsChanged();
            Notifications.Current.UnbindInstall(job);
            Notifications.Current.ShowSuccess("Install Done", $"{job.Title}: {result.ToHuman()}");
            Logger.Info($"[InstallQueue] Local install done: {job.Title} | {result.ToHuman()}");
        }
        catch (OperationCanceledException)
        {
            UI(() =>
            {
                job.Phase = "Cancelled";
                job.Status = "cancelled";
                job.IsIndeterminate = false;
                job.IsCancellable = false;
                job.IsCompleted = true;
            });
            Notifications.Current.UnbindInstall(job);
            Notifications.Current.ShowWarning("Install Cancelled", job.Title);
            Logger.Info($"[InstallQueue] Local install cancelled: {job.Title}");
        }
        catch (Exception ex)
        {
            UI(() =>
            {
                job.Phase = "Failed";
                job.Status = "failed: " + ex.Message;
                job.IsIndeterminate = false;
                job.IsCancellable = false;
                job.IsCompleted = true;
            });
            Notifications.Current.UnbindInstall(job);
            if (ex.Message.Contains("7za list failed"))
            {
                Notifications.Current.ShowError("Install Failed", job.Title + "This mod does not have direct download.");
                Logger.Error($"[InstallQueue] Local install failed due to direct download missing: {job.Title} | {ex}");
            }
            else
            {
                Notifications.Current.ShowError("Install Failed", job.Title);
                Logger.Error($"[InstallQueue] Local install failed: {job.Title} | {ex}");
            }
            Logger.Error("[InstallQueue] Local install failed: " + job.Title + " | " + ex);
        }
    }

    private async Task RunRemote(InstallJob job, string name, string url, string version, string guid, string? fixedModId, CancellationToken ct)
    {
        try
        {
            UI(() => Notifications.Current.BindInstall(job));

            UI(() =>
            {
                job.Phase = "Queued";
                job.SubTask = "Waiting";
                job.IsIndeterminate = true;
                job.Progress = 0;
                job.SubPercent = 0;
            });

            var cachePath = GetCachedArchivePath(name, url, string.IsNullOrWhiteSpace(version) ? "Custom Install" : version, guid ?? "");
            string archivePath;
            if (File.Exists(cachePath))
            {
                UI(() =>
                {
                    job.TotalBytes = new FileInfo(cachePath).Length;
                    job.DoneBytes = job.TotalBytes;
                    job.SubPercent = 100;
                    job.Progress = 40;
                    job.Eta = ComputeEta(job.StartedAt, job.Progress);
                });
                archivePath = cachePath;
            }
            else
            {
                var lastReport = DateTimeOffset.MinValue;
                var temp = await ForgeClient.DownloadToTempAsync(
                    url,
                    new Progress<int>(p =>
                    {
                        UI(() =>
                        {
                            job.SubPercent = p;
                            job.Progress = Math.Clamp(p * 40 / 100, 0, 40);
                            var now = DateTimeOffset.Now;
                            if (now - lastReport > TimeSpan.FromMilliseconds(300))
                            {
                                job.Eta = ComputeEta(job.StartedAt, job.Progress);
                                lastReport = now;
                            }
                        });
                    }),
                    ct
                ).ConfigureAwait(false);

                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

                try
                {
                    File.Copy(temp, cachePath, true);
                    archivePath = cachePath;
                }
                catch (Exception ex)
                {
                    Logger.Error("[InstallQueue] Failed to copy mod archive to cache: " + ex);
                    archivePath = temp;
                }

                try
                {
                    if (archivePath == cachePath && File.Exists(temp)) File.Delete(temp);
                }
                catch (Exception ex)
                {
                    Logger.Error("[InstallQueue] Failed to delete temp file: " + ex);
                }
            }

            if (!File.Exists(archivePath) || new FileInfo(archivePath).Length == 0)
                throw new IOException("Download failed: " + url);

            UI(() =>
            {
                job.Phase = "Extracting";
                job.SubTask = "Extracting files";
                job.IsIndeterminate = true;
                job.SubPercent = 0;
                job.Progress = 55;
                job.Eta = ComputeEta(job.StartedAt, job.Progress);
            });

            Installer.Target? preferred = null;
            if (!string.IsNullOrWhiteSpace(fixedModId))
                preferred = fixedModId.EndsWith("-server", StringComparison.OrdinalIgnoreCase)
                    ? Installer.Target.Server
                    : Installer.Target.Client;

            var ctx = new Installer.InstallContext
            {
                Name = name,
                Version = string.IsNullOrWhiteSpace(version) ? "Custom Install" : version,
                Guid = guid ?? "",
                SourceUrl = url,
                PreferredTarget = preferred,
                FixedModId = fixedModId
            };

            var result = await Installer.InstallAuto(
                archivePath,
                sevenZip,
                new Progress<(string phase, int pct)>(p =>
                {
                    UI(() =>
                    {
                        job.SubTask = p.phase switch
                        {
                            "inspect" => "Inspecting archive",
                            "extract" => "Extracting files",
                            "install" => "Placing files",
                            "move" => "Placing files",
                            _ => p.phase
                        };
                        job.SubPercent = p.pct < 0 ? 0 : p.pct;
                        job.IsIndeterminate = p.pct < 0;
                        var phaseBase = p.phase is "inspect" or "extract" ? 40 : 70;
                        var phaseSpan = p.phase is "inspect" or "extract" ? 30 : 30;
                        job.Progress = phaseBase + Math.Clamp(p.pct, 0, 100) * phaseSpan / 100;
                        job.Eta = ComputeEta(job.StartedAt, job.Progress);
                    });
                }),
                ctx,
                ct
            ).ConfigureAwait(false);

            UI(() =>
            {
                job.Phase = "Done";
                job.Status = result.ToHuman();
                job.IsIndeterminate = false;
                job.Progress = 100;
                job.SubPercent = 100;
                job.Eta = "";
                job.IsCancellable = false;
                job.IsCompleted = true;
                job.CompletedAt = DateTimeOffset.Now;
            });

            App.NotifyInstallsChanged();
            Notifications.Current.UnbindInstall(job);
            Notifications.Current.ShowSuccess("Install Done", $"{name}: {result.ToHuman()}");
            Logger.Info($"[InstallQueue] Remote install done: {name} | {result.ToHuman()}");
        }
        catch (OperationCanceledException)
        {
            UI(() =>
            {
                job.Phase = "Cancelled";
                job.Status = "cancelled";
                job.IsIndeterminate = false;
                job.IsCancellable = false;
                job.IsCompleted = true;
            });
            Notifications.Current.UnbindInstall(job);
            Notifications.Current.ShowWarning("Install Cancelled", name);
            Logger.Info("[InstallQueue] Remote install cancelled: " + name);
        }
        catch (Exception ex)
        {
            UI(() =>
            {
                job.Phase = "Failed";
                job.Status = "failed: " + ex.Message;
                job.IsIndeterminate = false;
                job.IsCancellable = false;
                job.IsCompleted = true;
            });
            Notifications.Current.UnbindInstall(job);
            if (ex.Message.Contains("7za list failed"))
            {
                Notifications.Current.ShowError("Install Failed", job.Title + "This mod does not have direct download.");
                Logger.Error($"[InstallQueue] Local install failed due to direct download missing: {job.Title} | {ex}");
            }
            else
            {
                Notifications.Current.ShowError("Install Failed", job.Title);
                Logger.Error($"[InstallQueue] Local install failed: {job.Title} | {ex}");
            }
            Logger.Error("[InstallQueue] Local install failed: " + job.Title + " | " + ex);
        }
    }

    private async Task RunEnable(InstallJob job, string modName, List<string> modIds, CancellationToken ct)
    {
        try
        {
            UI(() => Notifications.Current.BindInstall(job));
            UI(() =>
            {
                job.Phase = "Queued";
                job.SubTask = "Waiting";
                job.IsIndeterminate = true;
                job.Progress = 0;
                job.SubPercent = 0;
            });
            
            var total = Math.Max(1, modIds.Count);
            var done = 0;
            foreach (var id in modIds)
            {
                ct.ThrowIfCancellationRequested();
                UI(() =>
                {
                    job.Phase = "Enabling";
                    job.SubTask = $"Restoring files for {modName}";
                    job.IsIndeterminate = false;
                });
                
                await ModDisabler.EnableAsync(id, modName);
                done++;
                var pct = Math.Clamp((int)(done * 100.0 / total), 0, 100);
                UI(() =>
                {
                    job.SubPercent = pct;
                    job.Progress = pct;
                    job.Eta = ComputeEta(job.StartedAt, job.Progress);
                });
            }

            UI(() =>
            {
                job.Phase = "Done";
                job.Status = "enabled";
                job.IsIndeterminate = false;
                job.Progress = 100;
                job.SubPercent = 100;
                job.Eta = "";
                job.IsCancellable = false;
                job.IsCompleted = true;
                job.CompletedAt = DateTimeOffset.Now;
            });
            
            App.NotifyInstallsChanged();
            Notifications.Current.UnbindInstall(job);
            Notifications.Current.ShowSuccess("Enable Complete", modName);
            Logger.Info($"[InstallQueue] Enable done: {modName}");
        }
        catch (OperationCanceledException)
        {
            UI(() =>
            {
                job.Phase = "Cancelled";
                job.Status = "cancelled";
                job.IsIndeterminate = false;
                job.IsCancellable = false;
                job.IsCompleted = true;
            });
            
            Notifications.Current.UnbindInstall(job);
            Notifications.Current.ShowWarning("Enable Cancelled", modName);
            Logger.Info($"[InstallQueue] Enable cancelled: {modName}");
        }
        catch (Exception ex)
        {
            UI(() =>
            {
                job.Phase = "Failed";
                job.Status = "failed: " + ex.Message;
                job.IsIndeterminate = false;
                job.IsCancellable = false;
                job.IsCompleted = true;
            });
            Notifications.Current.UnbindInstall(job);
            Notifications.Current.ShowError("Enable Failed", modName);
            Logger.Error($"[InstallQueue] Enable failed: {modName} | {ex}");
        }
    }

    private async Task RunDisable(InstallJob job, string modName, List<string> modIds, CancellationToken ct)
    {
        try
        {
            UI(() => Notifications.Current.BindInstall(job));
            UI(() =>
            {
                job.Phase = "Queued";
                job.SubTask = "Waiting";
                job.IsIndeterminate = true;
                job.Progress = 0;
                job.SubPercent = 0;
            });
            
            var total = Math.Max(1, modIds.Count);
            var done = 0;
            foreach (var id in modIds)
            {
                ct.ThrowIfCancellationRequested();
                UI(() =>
                {
                    job.Phase = "Disabling";
                    job.SubTask = $"Moving files for {modName}";
                    job.IsIndeterminate = false;
                });
                await ModDisabler.DisableAsync(id, modName);
                done++;
                var pct = Math.Clamp((int)(done * 100.0 / total), 0, 100);
                UI(() =>
                {
                    job.SubPercent = pct;
                    job.Progress = pct;
                    job.Eta = ComputeEta(job.StartedAt, job.Progress);
                });
            }

            var sptRoot = App.Config.Paths.SptRoot;
            if (!string.IsNullOrWhiteSpace(sptRoot) && Directory.Exists(sptRoot))
            {
                var pruned = 0;
                pruned += PruneEmptySubdirsSafe(Path.Combine(sptRoot, "BepInEx", "plugins"));
                pruned += PruneEmptySubdirsSafe(Path.Combine(sptRoot, "SPT", "user", "mods"));
                pruned += PruneEmptySubdirsSafe(Path.Combine(sptRoot, "user", "mods"));
                if (pruned > 0) Logger.Info($"[InstallQueue] Pruned empty directories: {pruned}");
            }

            UI(() =>
            {
                job.Phase = "Done";
                job.Status = "disabled";
                job.IsIndeterminate = false;
                job.Progress = 100;
                job.SubPercent = 100;
                job.Eta = "";
                job.IsCancellable = false;
                job.IsCompleted = true;
                job.CompletedAt = DateTimeOffset.Now;
            });
            
            App.NotifyInstallsChanged();
            Notifications.Current.UnbindInstall(job);
            Notifications.Current.ShowSuccess("Disable Complete", modName);
            Logger.Info($"[InstallQueue] Disable done: {modName}");
        }
        catch (OperationCanceledException)
        {
            UI(() =>
            {
                job.Phase = "Cancelled";
                job.Status = "cancelled";
                job.IsIndeterminate = false;
                job.IsCancellable = false;
                job.IsCompleted = true;
            });
            
            Notifications.Current.UnbindInstall(job);
            Notifications.Current.ShowWarning("Disable Cancelled", modName);
            Logger.Info($"[InstallQueue] Disable cancelled: {modName}");
        }
        catch (Exception ex)
        {
            UI(() =>
            {
                job.Phase = "Failed";
                job.Status = "failed: " + ex.Message;
                job.IsIndeterminate = false;
                job.IsCancellable = false;
                job.IsCompleted = true;
            });
            
            Notifications.Current.UnbindInstall(job);
            Notifications.Current.ShowError("Disable Failed", modName);
            Logger.Error($"[InstallQueue] Disable failed: {modName} | {ex}");
        }
    }

    private async Task RunUninstall(InstallJob job, string modName, List<string> modIds, CancellationToken ct)
    {
        try
        {
            UI(() => Notifications.Current.BindInstall(job));
            UI(() =>
            {
                job.Phase = "Queued";
                job.SubTask = "Waiting";
                job.IsIndeterminate = true;
                job.Progress = 0;
                job.SubPercent = 0;
            });
            
            var total = Math.Max(1, modIds.Count);
            var done = 0;
            foreach (var id in modIds)
            {
                ct.ThrowIfCancellationRequested();
                UI(() =>
                {
                    job.Phase = "Uninstalling";
                    job.SubTask = $"Removing {modName}";
                    job.IsIndeterminate = false;
                });
                
                App.Db.UninstallByModIds(new List<string> { id });
                done++;
                var pct = Math.Clamp((int)(done * 100.0 / total), 0, 100);
                UI(() =>
                {
                    job.SubPercent = pct;
                    job.Progress = pct;
                    job.Eta = ComputeEta(job.StartedAt, job.Progress);
                });
            }

            var sptRoot = App.Config.Paths.SptRoot;
            if (!string.IsNullOrWhiteSpace(sptRoot) && Directory.Exists(sptRoot))
            {
                var pruned = 0;
                pruned += PruneEmptySubdirsSafe(Path.Combine(sptRoot, "BepInEx", "plugins"));
                pruned += PruneEmptySubdirsSafe(Path.Combine(sptRoot, "SPT", "user", "mods"));
                pruned += PruneEmptySubdirsSafe(Path.Combine(sptRoot, "user", "mods"));
                if (pruned > 0) Logger.Info($"[InstallQueue] Pruned empty directories: {pruned}");
            }

            UI(() =>
            {
                job.Phase = "Done";
                job.Status = "uninstalled";
                job.IsIndeterminate = false;
                job.Progress = 100;
                job.SubPercent = 100;
                job.Eta = "";
                job.IsCancellable = false;
                job.IsCompleted = true;
                job.CompletedAt = DateTimeOffset.Now;
            });
            
            App.NotifyInstallsChanged();
            Notifications.Current.UnbindInstall(job);
            Notifications.Current.ShowSuccess("Uninstall Complete", modName);
            Logger.Info($"[InstallQueue] Uninstall done: {modName}");
        }
        catch (OperationCanceledException)
        {
            UI(() =>
            {
                job.Phase = "Cancelled";
                job.Status = "cancelled";
                job.IsIndeterminate = false;
                job.IsCancellable = false;
                job.IsCompleted = true;
            });
            
            Notifications.Current.UnbindInstall(job);
            Notifications.Current.ShowWarning("Uninstall Cancelled", modName);
            Logger.Info($"[InstallQueue] Uninstall cancelled: {modName}");
        }
        catch (Exception ex)
        {
            UI(() =>
            {
                job.Phase = "Failed";
                job.Status = "failed: " + ex.Message;
                job.IsIndeterminate = false;
                job.IsCancellable = false;
                job.IsCompleted = true;
            });
            
            Notifications.Current.UnbindInstall(job);
            Notifications.Current.ShowError("Uninstall Failed", modName);
            Logger.Error($"[InstallQueue] Uninstall failed: {modName} | {ex}");
        }
    }

    private static string ComputeEta(DateTimeOffset started, int overallPercent)
    {
        var pct = Math.Clamp(overallPercent, 1, 99);
        var elapsed = DateTimeOffset.Now - started;
        if (elapsed.TotalSeconds <= 0.5) return "";
        var rate = elapsed.TotalSeconds / pct;
        var remain = (int)Math.Ceiling(rate * (100 - pct));
        return $"~{remain}s remaining";
    }

    private static string CleanFs(string s)
    {
        var sb = new StringBuilder(s?.Length ?? 16);
        foreach (var ch in s ?? "")
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.')
                sb.Append(ch);
        return sb.ToString();
    }

    private static int PruneEmptySubdirsSafe(string root)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return 0;
            return PruneEmptySubdirs(root, root);
        }
        catch (Exception ex)
        {
            Logger.Error($"[InstallQueue] Prune failed for '{root}': {ex}");
            return 0;
        }
    }

    private static int PruneEmptySubdirs(string root, string protectRoot)
    {
        var removed = 0;
        foreach (var dir in Directory.EnumerateDirectories(root))
            removed += PruneEmptySubdirs(dir, protectRoot);
        if (root.Equals(protectRoot, StringComparison.OrdinalIgnoreCase)) return removed;
        if (!Directory.EnumerateFileSystemEntries(root).Any())
        {
            try
            {
                Directory.Delete(root, false);
                removed++;
            }
            catch (Exception ex)
            {
                Logger.Error($"[InstallQueue] Failed to delete empty dir '{root}': {ex}");
            }
        }
        return removed;
    }
    
    private static string ComputeStableCacheName(string name, string url, string version, string guid)
    {
        var baseKey = !string.IsNullOrWhiteSpace(guid) ? $"{guid}_{version}" : !string.IsNullOrWhiteSpace(name) ? $"{name}__{version}" : url;

        var clean = CleanFs(baseKey);
        if (string.IsNullOrWhiteSpace(clean)) clean = "mod";

        var ext = ".7z";
        try
        {
            var u = new Uri(url, UriKind.RelativeOrAbsolute);
            var tryExt = u.IsAbsoluteUri ? Path.GetExtension(u.AbsolutePath) : Path.GetExtension(url);
            if (!string.IsNullOrWhiteSpace(tryExt) && tryExt.Length <= 5) ext = tryExt;
        }
        catch (Exception ex)
        {
            Logger.Error("[InstallQueue] Failed to parse archive URL: " + ex);
        }

        return clean.ToLowerInvariant() + ext.ToLowerInvariant();
    }

    private static string GetDownloadsDir()
    {
        var dir = Path.Combine(string.IsNullOrWhiteSpace(App.Config.Paths.DataFolder) ? Paths.DataDir : App.Config.Paths.DataFolder, "downloads");
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            Logger.Error("[InstallQueue] Failed to create downloads folder: " + ex);
        }

        return dir;
    }

    private static string GetCachedArchivePath(string name, string url, string version, string guid)
    {
        return Path.Combine(GetDownloadsDir(), ComputeStableCacheName(name, url, version ?? "Custom Install", guid ?? ""));
    }
}