using System;
using System.Threading;
using Avalonia;

namespace DragonDen.ModManager;

internal static class Program
{
    private const string AppKey = "Local\\DragonDen.ModManager";
    private static Mutex? _instanceMutex;

    [STAThread]
    private static void Main(string[] args)
    {
        var createdNew = false;
        _instanceMutex = new Mutex(initiallyOwned: true, name: AppKey + ":MUTEX", createdNew: out createdNew);

        if (!createdNew)
        {
            try
            {
                using var ev = EventWaitHandle.OpenExisting(AppKey + ":ACTIVATE");
                ev.Set();
            }
            catch
            {
                // good girl action
            }
            return;
        }

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            _instanceMutex?.ReleaseMutex();
            _instanceMutex?.Dispose();
        }
    }

    private static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();
}