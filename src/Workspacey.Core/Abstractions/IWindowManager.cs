namespace Workspacey.Core.Abstractions;

/// <summary>Live window manager operations.</summary>
public interface IWindowManager
{
    /// <summary>Makes the window manager re-read its rules after the rule store changed.</summary>
    Task ReloadRulesAsync(CancellationToken cancellationToken = default);

    /// <summary>Moves an already open window to a desktop.</summary>
    Task MoveWindowToDesktopAsync(string windowId, string desktopId, CancellationToken cancellationToken = default);
}
