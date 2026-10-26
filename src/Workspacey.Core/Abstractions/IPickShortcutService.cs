using Workspacey.Core.Models;

namespace Workspacey.Core.Abstractions;

/// <summary>The global key combination that starts the window picker.</summary>
public interface IPickShortcutService
{
    Task<KeyChord?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers <paramref name="chord"/> system-wide. Returns <c>false</c> if the desktop refused it,
    /// typically because another action already uses the same combination.
    /// </summary>
    Task<bool> SetAsync(KeyChord chord, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses all global shortcuts while the user records a new one; otherwise combinations that are
    /// already taken never reach the app. Always resume afterwards: the pause is not tied to this process.
    /// </summary>
    Task SetGlobalShortcutsPausedAsync(bool paused, CancellationToken cancellationToken = default);
}
