using Microsoft.Extensions.DependencyInjection;
using Tmds.DBus.Protocol;
using Workspacey.Core.Abstractions;
using Workspacey.Kde.DBus;
using Workspacey.Kde.Desktop;
using Workspacey.Kde.Rules;
using Workspacey.Kde.Shortcuts;

namespace Workspacey.Kde;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the KDE Plasma 6 / KWin implementations of the Core abstractions.</summary>
    public static IServiceCollection AddKdeIntegration(this IServiceCollection services, AppLauncher launcher)
    {
        // Shared session bus connection; connects lazily on first use and is owned by Tmds.DBus.
        services.AddSingleton(_ => DBusConnection.Session);
        services.AddSingleton(launcher);

        services.AddSingleton<IWindowPicker, KWinWindowPicker>();
        services.AddSingleton<IVirtualDesktopService, KWinVirtualDesktopService>();
        services.AddSingleton<IWindowManager, KWinWindowManager>();
        services.AddSingleton<IWorkspaceRuleRepository>(_ => new KWinRulesRepository());
        services.AddSingleton<IPickShortcutService, KGlobalAccelPickShortcutService>();
        services.AddSingleton<ISystemSettingsLauncher, KdeSystemSettingsLauncher>();
        services.AddSingleton<KdeDesktopIntegration>();
        services.AddSingleton<IDesktopIntegration>(sp => sp.GetRequiredService<KdeDesktopIntegration>());
        return services;
    }
}
