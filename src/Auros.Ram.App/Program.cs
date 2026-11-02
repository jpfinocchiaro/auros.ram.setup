using Auros.Ram.App.Services;
using Auros.Ram.Persistence;
using Avalonia;

namespace Auros.Ram.App;

internal static class Program
{
    /// <summary>Archivos recibidos por línea de comandos (doble clic en el explorador, RNF-43).</summary>
    public static IReadOnlyList<string> StartupFiles { get; private set; } = [];

    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                Log.Error(ex, "AppDomain");
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Error(e.Exception, "Task");
            e.SetObserved();
        };

        AppPaths.CleanWorkDirectory();

        // El idioma elegido en Preferencias, antes de crear cualquier ventana.
        Languages.Startup(AppSettings.Load().Language);
        StartupFiles = [.. args.Where(File.Exists)];
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithPlexFont()
            .LogToTrace();
}
