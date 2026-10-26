namespace Workspacey.Core.Abstractions;

/// <summary>Launchers and icons Workspacey installs into the user's desktop environment.</summary>
public interface IDesktopIntegration
{
    bool IsMenuEntryInstalled { get; }

    /// <summary>Adds Workspacey to the application menu.</summary>
    Task InstallMenuEntryAsync(CancellationToken cancellationToken = default);

    Task RemoveMenuEntryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rewrites already installed launchers so they start the current executable, e.g. after an
    /// AppImage was moved or updated. Does nothing for launchers that were never installed.
    /// </summary>
    Task RefreshInstalledLaunchersAsync(CancellationToken cancellationToken = default);
}
