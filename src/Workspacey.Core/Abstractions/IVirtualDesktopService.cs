using Workspacey.Core.Models;

namespace Workspacey.Core.Abstractions;

public interface IVirtualDesktopService
{
    /// <summary>All virtual desktops ordered by position.</summary>
    Task<IReadOnlyList<VirtualDesktop>> GetDesktopsAsync(CancellationToken cancellationToken = default);

    /// <summary>Id of the desktop that is currently shown.</summary>
    Task<string> GetCurrentDesktopIdAsync(CancellationToken cancellationToken = default);

    /// <summary>Appends a new desktop after the existing ones and returns it. <paramref name="name"/> is required.</summary>
    Task<VirtualDesktop> CreateDesktopAsync(string name, CancellationToken cancellationToken = default);
}
