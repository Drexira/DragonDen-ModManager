using System;
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
                return;

            var mod = await ForgeClient.GetModAsync(ModManagerForgeID, includeOwner: false, includeAuthors: false, includeCategory: false, includeVersions: false,
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
            Console.WriteLine($"[SelfUpdateChecker] CheckOnStartupAsync: {ex}");
        }
    }

    public static string GetCurrentAppVersion()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var fvi = System.Diagnostics.FileVersionInfo.GetVersionInfo(asm.Location);
            var prod = fvi.ProductVersion ?? fvi.FileVersion;
            if (!string.IsNullOrWhiteSpace(prod))
            {
                var plus = prod.IndexOf('+');
                return plus > 0 ? prod[..plus] : prod;
            }

            var infoAttr = asm.GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
                              .OfType<AssemblyInformationalVersionAttribute>()
                              .FirstOrDefault()?.InformationalVersion;
            if (string.IsNullOrWhiteSpace(infoAttr)) return asm.GetName()?.Version?.ToString() ?? "0.0.0";
            var plus2 = infoAttr.IndexOf('+');
            return plus2 > 0 ? infoAttr[..plus2] : infoAttr;

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