using Workspacey.Core.Models;

namespace Workspacey.Core.Abstractions;

/// <summary>Outcome of <see cref="IPickShortcutService.SetAsync"/>.</summary>
/// <param name="Accepted">The shortcut is now active.</param>
/// <param name="ConflictingAction">If refused: the action that owns the combination, when known.</param>
public sealed record ShortcutResult(bool Accepted, string? ConflictingAction = null);

/// <summary>The global key combination that starts the window picker.</summary>
public interface IPickShortcutService
{
    Task<KeyChord?> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers <paramref name="chord"/> system-wide. A combination another action already uses is
    /// refused, and the previous shortcut stays active.
    /// </summary>
    Task<ShortcutResult> SetAsync(KeyChord chord, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read-only check whether <paramref name="chord"/> could be set, so a conflict can be shown right
    /// after recording. <see cref="ShortcutResult.Accepted"/> means "available".
    /// </summary>
    Task<ShortcutResult> CheckAvailabilityAsync(KeyChord chord, CancellationToken cancellationToken = default);

    /// <summary>Removes the shortcut and everything installed for it, so it no longer appears in the desktop's shortcut settings.</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses all global shortcuts while the user records a new one; otherwise combinations that are
    /// already taken never reach the app. Always resume afterwards: the pause is not tied to this process.
    /// </summary>
    Task SetGlobalShortcutsPausedAsync(bool paused, CancellationToken cancellationToken = default);
}
