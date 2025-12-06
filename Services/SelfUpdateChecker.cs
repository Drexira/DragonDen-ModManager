using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using DragonDen.ModManager.Views;

namespace DragonDen.ModManager.Services;

public static class SelfUpdateChecker
{
    private const int ModManagerForgeID = 2396;

    public static async Task CheckOnStartupAsync(Window owner, System.Threading.CancellationToken ct = default)
    {
        try
        {
            var versions = await ForgeClient.GetAllVersionsAsync(ModManagerForgeID, ct).ConfigureAwait(false);
            var latest = OrderVersionsDesc(versions).FirstOrDefault();
            if (latest == null || string.IsNullOrWhiteSpace(latest.Version))
                return;

            var current = GetCurrentAppVersion();
            if (string.IsNullOrWhiteSpace(current))
                return;

            if (!IsUpdate(current, latest.Version!))
            {
                Logger.Info($"[SelfUpdateChecker] No update available. Current: {current}, Latest: {latest.Version}");
                return;
            }

            var mod = await ForgeClient.GetModAsync(ModManagerForgeID, includeCategory: false, includeVersions: false,
                includeSourceLinks: false, ct: ct).ConfigureAwait(false);

            var pageUrl = mod?.detail_url ?? "";

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var dlg = new UpdateAvailableDialog(latest.Version!, latest.Description ?? "", pageUrl)
                {
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    ShowInTaskbar = false
                };
                await dlg.ShowDialog(owner);
            });
        }
        catch (Exception ex)
        {
            Logger.Error($"[SelfUpdateChecker] CheckOnStartupAsync: {ex}");
        }
    }

    public static string GetCurrentAppVersion()
    {
        try
        {
            static string Normalize(string? v)
            {
                if (string.IsNullOrWhiteSpace(v)) return "";
                var s = v.Trim();
                var plus = s.IndexOf('+');
                if (plus > 0) s = s[..plus];
                var parts = s.Split('.', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 4) s = string.Join('.', parts[0], parts[1], parts[2]);
                parts = s.Split('.', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1) s += ".0.0";
                else if (parts.Length == 2) s += ".0";
                return s;
            }

            var entry = Assembly.GetEntryAssembly();
            var appAsm = typeof(App).Assembly;
            var exec = Assembly.GetExecutingAssembly();
            var asm = entry ?? appAsm ?? exec;

            var info = asm.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                .OfType<AssemblyInformationalVersionAttribute>()
                .FirstOrDefault()?.InformationalVersion;
            var vInfo = Normalize(info);
            if (!string.IsNullOrWhiteSpace(vInfo) && !vInfo.StartsWith("0.0.0", StringComparison.Ordinal))
                return vInfo;

            var fileAttr = asm.GetCustomAttributes(typeof(AssemblyFileVersionAttribute), false)
                .OfType<AssemblyFileVersionAttribute>()
                .FirstOrDefault()?.Version;
            var vFileAttr = Normalize(fileAttr);
            if (!string.IsNullOrWhiteSpace(vFileAttr) && !vFileAttr.StartsWith("0.0.0", StringComparison.Ordinal))
                return vFileAttr;

            try
            {
                if (!string.IsNullOrWhiteSpace(asm.Location) && File.Exists(asm.Location))
                {
                    var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(asm.Location);
                    var prod = Normalize(fvi.ProductVersion ?? fvi.FileVersion);
                    if (!string.IsNullOrWhiteSpace(prod) && !prod.StartsWith("0.0.0", StringComparison.Ordinal))
                        return prod;
                }
            }
            catch
            {
                // good girl action
            }

            try
            {
                var mm = System.Diagnostics.Process.GetCurrentProcess().MainModule;
                if (mm != null)
                {
                    var fvi2 = System.Diagnostics.FileVersionInfo.GetVersionInfo(mm.FileName);
                    var prod2 = Normalize(fvi2.ProductVersion ?? fvi2.FileVersion);
                    if (!string.IsNullOrWhiteSpace(prod2) && !prod2.StartsWith("0.0.0", StringComparison.Ordinal))
                        return prod2;
                }
            }
            catch
            {
                // good girl action
            }

            var nameVer = Normalize(asm.GetName()?.Version?.ToString());
            if (!string.IsNullOrWhiteSpace(nameVer)) return nameVer;

            return "0.0.0";
        }
        catch
        {
            return "0.0.0";
        }
    }

    private static bool IsUpdate(string installed, string latest)
    {
        var okI = SemverUtil.TryParseStrict(installed, out var vi);
        var okL = SemverUtil.TryParseStrict(latest, out var vl);
        if (okI && okL) return vl.CompareSortOrderTo(vi) > 0;
        return !string.Equals(latest, installed, StringComparison.OrdinalIgnoreCase);
    }

    private static System.Collections.Generic.List<ForgeClient.ModVersion> OrderVersionsDesc(System.Collections.Generic.IEnumerable<ForgeClient.ModVersion> versions)
    {
        var list = versions?.ToList() ?? new System.Collections.Generic.List<ForgeClient.ModVersion>();
        list.Sort((a, b) =>
        {
            var va = a?.Version ?? "";
            var vb = b?.Version ?? "";
            var okA = SemverUtil.TryParseStrict(va, out var sa);
            var okB = SemverUtil.TryParseStrict(vb, out var sb);
            if (okA && okB) return sb.CompareSortOrderTo(sa);
            return string.Compare(vb, va, StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }
}