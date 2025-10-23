using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            RedirectStandardError = false
        };
        psi.ArgumentList.Add("x");
        psi.ArgumentList.Add(archivePath);
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-aoa");
        psi.ArgumentList.Add("-o" + destDir);
        psi.ArgumentList.Add("-bso0");
        psi.ArgumentList.Add("-bse0");

        using var p = Process.Start(psi);
        if (p is null) throw new InvalidOperationException("Failed to start 7za");
        
        await p.WaitForExitAsync().ConfigureAwait(false);
        
        if (p.ExitCode != 0) throw new InvalidOperationException("7za failed");
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

        using var p = Process.Start(psi);
        if (p == null) throw new InvalidOperationException("Failed to start 7za");
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException("7za list failed");

        var list = new List<string>();
        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            if (line.StartsWith("Path = ", StringComparison.OrdinalIgnoreCase))
                list.Add(line.Substring(7).Trim().Replace('\\', '/'));
        return list;
    }
}