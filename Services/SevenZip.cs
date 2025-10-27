using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace DragonDen.ModManager.Services;

public sealed class SevenZip
{
    private readonly string exePath;

    public SevenZip(string exePath)
    {
        this.exePath = exePath;
    }

    public async Task ExtractAsync(string archivePath, string destDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("x");
        psi.ArgumentList.Add(archivePath);
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-aoa");
        psi.ArgumentList.Add("-spf");
        psi.ArgumentList.Add("-o" + destDir);
        psi.ArgumentList.Add("-bso0");
        psi.ArgumentList.Add("-bse1");

        var args = string.Join(" ", psi.ArgumentList.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a));

        using var p = Process.Start(psi);
        if (p is null) throw new InvalidOperationException("Failed to start 7za");

        var err = await p.StandardError.ReadToEndAsync().ConfigureAwait(false);
        await p.WaitForExitAsync().ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(err)) Logger.Error($"[7za] extract stderr: {TrimLong(err, 1200)}");

        if (p.ExitCode > 1)
            throw new InvalidOperationException($"7za failed (code {p.ExitCode}){(string.IsNullOrWhiteSpace(err) ? "" : ": " + err.Trim())}");

        static string TrimLong(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
    }

    public void Extract(string archivePath, string destDir)
    {
        ExtractAsync(archivePath, destDir).GetAwaiter().GetResult();
    }

    public List<string> ListEntries(string archivePath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("l");
        psi.ArgumentList.Add("-slt");
        psi.ArgumentList.Add(archivePath);

        var args = string.Join(" ", psi.ArgumentList.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a));

        using var p = Process.Start(psi);
        if (p == null) throw new InvalidOperationException("Failed to start 7za");
        var output = p.StandardOutput.ReadToEnd();
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();

        if (!string.IsNullOrWhiteSpace(err)) Logger.Error($"[7za] list stderr: {TrimLong(err, 1200)}");

        if (p.ExitCode > 1)
            throw new InvalidOperationException($"7za list failed (code {p.ExitCode}){(string.IsNullOrWhiteSpace(err) ? "" : ": " + err.Trim())}");

        var list = new List<string>();
        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            if (line.StartsWith("Path = ", StringComparison.OrdinalIgnoreCase))
                list.Add(line.Substring(7).Trim().Replace('\\', '/'));

        return list;

        static string TrimLong(string s, int max) => s.Length <= max ? s : s.Substring(0, max);
    }
}