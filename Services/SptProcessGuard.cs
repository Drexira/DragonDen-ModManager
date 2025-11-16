using System.Diagnostics;

namespace DragonDen.ModManager.Services;

public static class SptProcessGuard
{
    private static readonly string[] Names =
    {
        "SPT.Server",
        "EscapeFromTarkov"
    };

    public static bool IsAnySptProcessRunning()
    {
        try
        {
            foreach (var name in Names)
            {
                try
                {
                    if (Process.GetProcessesByName(name).Length > 0)
                        return true;
                }
                catch
                {
                    // good girl action
                }
            }
        }
        catch
        {
            // good girl action
        }

        return false;
    }

    public static bool BlockIfRunning(string actionDescription)
    {
        if (!IsAnySptProcessRunning())
            return false;

        var msg = string.IsNullOrWhiteSpace(actionDescription)
            ? "SPT.Server or EscapeFromTarkov is running. Close them before continuing."
            : $"{actionDescription} cannot run while SPT.Server or EscapeFromTarkov is running. Close them first.";

        Notifications.Current.ShowError("Action Blocked", msg);
        return true;
    }
}