namespace Workspacey.Core.Abstractions;

/// <summary>Opens the desktop environment's own settings pages.</summary>
public interface ISystemSettingsLauncher
{
    /// <summary>Opens the settings page for managing workspaces (virtual desktops).</summary>
    void OpenWorkspaceSettings();
}
