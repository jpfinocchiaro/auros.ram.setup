using Auros.Ram.App.Resources;
using Auros.Ram.App.Services;
using Auros.Ram.App.ViewModels;
using Auros.Ram.App.Views;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Auros.Ram.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            var ui = new UiService(window);
            var vm = new MainWindowViewModel(ui, AppSettings.Load());
            window.DataContext = vm;
            desktop.MainWindow = window;

            // RNF-29: los errores no controlados se registran y se informan sin cerrar la aplicación.
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                Log.Error(e.Exception, "UI");
                e.Handled = true;
                _ = ui.AlertAsync(Strings.Error_UnexpectedTitle, Strings.Error_UnexpectedMessage);
            };

            window.Opened += async (_, _) =>
            {
                foreach (var file in Program.StartupFiles)
                {
                    await vm.OpenPathAsync(file);
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
