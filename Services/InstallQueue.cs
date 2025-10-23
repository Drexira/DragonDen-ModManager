using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
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

    public ObservableCollection<InstallJob> Jobs { get; } = new();

    private static void UI(Action a)
    {
        Dispatcher.UIThread.Post(a);
    }

    public InstallJob EnqueueLocal(string archivePath)
    {
        var job = new InstallJob { Title = Path.GetFileName(archivePath), Source = "Local" };
        Jobs.Add(job);
        App.Toasts.ShowProgress(job);
        work.Enqueue(() => RunLocal(job, archivePath));
        _ = Pump();
        return job;
    }

    public InstallJob EnqueueRemote(string name, string url, string version, string guid, string? fixedModId = null)
    {
        var job = new InstallJob { Title = name, Source = url };
        Jobs.Add(job);
        App.Toasts.ShowProgress(job);
        work.Enqueue(() => RunRemote(job, name, url, version, guid, fixedModId));
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
            while (work.TryDequeue(out var fn)) await fn().ConfigureAwait(false);
        }
        finally
        {
            running = false;
            gate.Release();
        }
    }

    private async Task RunLocal(InstallJob job, string archive)
    {
        try
        {
            UI(() =>
            {
                job.Phase = "extract";
                job.IsIndeterminate = true;
            });

            var result = await Installer.InstallAuto(
                archive,
                sevenZip,
                new Progress<(string phase, int pct)>(p =>
                {
                    UI(() =>
                    {
                        job.Phase = p.phase;
                        if (p.pct >= 0)
                        {
                            job.IsIndeterminate = false;
                            job.Progress = p.pct;
                        }
                    });
                })
            ).ConfigureAwait(false);

            UI(() =>
            {
                job.Status = result.ToHuman();
                App.Toasts.Show($"Installed {job.Title}: {result.ToHuman()}");
            });

            App.NotifyInstallsChanged();
        }
        catch (Exception ex)
        {
            UI(() =>
            {
                job.Status = "failed: " + ex.Message;
                App.Toasts.Show($"Install failed: {job.Title}");
            });
        }
        finally
        {
            UI(() =>
            {
                job.Progress = 100;
                job.IsIndeterminate = false;
            });
        }
    }

    private async Task RunRemote(InstallJob job, string name, string url, string version, string guid, string? fixedModId)
    {
        try
        {
            UI(() =>
            {
                job.Phase = "download";
                job.IsIndeterminate = false;
                job.Progress = 0;
            });

            var cachePath = GetCachedArchivePath(name, url, string.IsNullOrWhiteSpace(version) ? "0.0.0" : version, guid ?? "");
            string archivePath;

            if (File.Exists(cachePath))
            {
                UI(() => job.Progress = 100);
                archivePath = cachePath;
            }
            else
            {
                var temp = await ForgeClient.DownloadToTempAsync(
                    url,
                    new Progress<int>(p => UI(() => job.Progress = p))
                ).ConfigureAwait(false);

                Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);

                try
                {
                    File.Copy(temp, cachePath, true);
                    archivePath = cachePath;
                }
                catch
                {
                    // good girl action
                    archivePath = temp;
                }

                try
                {
                    if (archivePath == cachePath && File.Exists(temp)) File.Delete(temp);
                }
                catch
                {
                    // good girl action
                }
            }

            if (!File.Exists(archivePath) || new FileInfo(archivePath).Length == 0)
                throw new IOException("Download failed: " + url);

            UI(() =>
            {
                job.Progress = 100;
                job.Phase = "install";
                job.IsIndeterminate = true;
            });

            Installer.Target? preferred = null;
            if (!string.IsNullOrWhiteSpace(fixedModId))
                preferred = fixedModId.EndsWith("-server", StringComparison.OrdinalIgnoreCase)
                    ? Installer.Target.Server
                    : Installer.Target.Client;

            var ctx = new Installer.InstallContext
            {
                Name = name,
                Version = string.IsNullOrWhiteSpace(version) ? "0.0.0" : version,
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
                        job.Phase = p.phase;
                        if (p.pct >= 0)
                        {
                            job.IsIndeterminate = false;
                            job.Progress = p.pct;
                        }
                    });
                }),
                ctx
            ).ConfigureAwait(false);

            UI(() =>
            {
                job.Status = result.ToHuman();
                App.Toasts.Show($"Installed {name}: {result.ToHuman()}");
            });

            App.NotifyInstallsChanged();
        }
        catch (Exception ex)
        {
            UI(() =>
            {
                job.Status = "failed: " + ex.Message;
                App.Toasts.Show($"Install failed: {name}");
            });
        }
        finally
        {
            UI(() =>
            {
                job.Progress = 100;
                job.IsIndeterminate = false;
            });
        }
    }

    private static string CleanFs(string s)
    {
        var sb = new StringBuilder(s?.Length ?? 16);
        foreach (var ch in s ?? "")
            if (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_' || ch == '.')
                sb.Append(ch);
        return sb.ToString();
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
        catch
        {
            // good girl action
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
        catch
        {
            // good girl action
        }

        return dir;
    }

    private static string GetCachedArchivePath(string name, string url, string version, string guid)
    {
        return Path.Combine(GetDownloadsDir(), ComputeStableCacheName(name, url, version ?? "0.0.0", guid ?? ""));
    }
}