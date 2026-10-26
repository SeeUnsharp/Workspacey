using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Workspacey.App.Services;
using Workspacey.App.ViewModels;
using Workspacey.App.Views;
using Workspacey.Core.Services;
using Workspacey.Kde;
using Workspacey.Kde.Desktop;

namespace Workspacey.App;

public sealed class App : Application
{
    private ServiceProvider? _services;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _services = ConfigureServices();
            desktop.Exit += (_, _) => _services.Dispose();

            if (desktop.Args?.Contains(AppLauncher.PickArgument) == true)
            {
                StartPickMode(desktop, _services);
            }
            else
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = _services.GetRequiredService<MainWindowViewModel>(),
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Launched by the global shortcut: pick a window, show the popup, exit.</summary>
    private static void StartPickMode(IClassicDesktopStyleApplicationLifetime desktop, IServiceProvider services)
    {
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                await services.GetRequiredService<IPickWorkflow>().RunAsync();
            }
            catch (Exception e)
            {
                services.GetRequiredService<ILogger<App>>().LogError(e, "Picking a window failed");
            }
            finally
            {
                desktop.Shutdown();
            }
        });
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(logging => logging
            .AddSimpleConsole(options => options.SingleLine = true)
            .SetMinimumLevel(LogLevel.Information));

        services.AddKdeIntegration(CreateAppLauncher());
        services.AddSingleton<WorkspaceAssignmentService>();

        services.AddSingleton<IPickWorkflow, PickWorkflow>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<AssignWorkspaceViewModel>();
        services.AddSingleton<Func<AssignWorkspaceViewModel>>(sp => sp.GetRequiredService<AssignWorkspaceViewModel>);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    /// <summary>How installed launchers (shortcut, application menu) start this very program.</summary>
    private static AppLauncher CreateAppLauncher()
    {
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "workspacey.svg");
        var iconPath = File.Exists(icon) ? icon : null;

        // Inside an AppImage the binary lives in a mount point that changes on every start;
        // the AppImage runtime exports the path of the .AppImage file itself.
        if (Environment.GetEnvironmentVariable("APPIMAGE") is { Length: > 0 } appImage)
        {
            return new AppLauncher(appImage, [], iconPath);
        }

        var executable = Environment.ProcessPath ?? "workspacey";

        // Started as "dotnet workspacey.dll" (e.g. framework-dependent without apphost): pass the dll along.
        if (Path.GetFileNameWithoutExtension(executable) == "dotnet")
        {
            return new AppLauncher(executable, [Assembly.GetEntryAssembly()!.Location], iconPath);
        }

        return new AppLauncher(executable, [], iconPath);
    }
}
