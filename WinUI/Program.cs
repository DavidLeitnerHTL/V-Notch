using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace VNotch.WinUI;

public static class Program
{
    [STAThread]
    private static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
    }
}

public sealed class App : Application
{
    private NotchWindow? _window;

    public App()
    {
        UnhandledException += (_, e) => Log(e.Exception);
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new NotchWindow();
        _window.ShowWithoutFocus();
    }

    internal static void Log(Exception error)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "V-Notch-WinUI");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"), $"{DateTime.Now:O} {error}\n\n");
        }
        catch (IOException)
        {
            // Nothing sensible to do when the log itself cannot be written.
        }
    }
}
