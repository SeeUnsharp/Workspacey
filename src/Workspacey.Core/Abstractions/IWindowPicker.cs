using Workspacey.Core.Models;

namespace Workspacey.Core.Abstractions;

/// <summary>Lets the user interactively select a window with the mouse.</summary>
public interface IWindowPicker
{
    /// <summary>
    /// Switches the cursor to a selection cursor and waits for the user to click a window.
    /// Returns <c>null</c> if the user cancelled (Esc / right click).
    /// </summary>
    Task<WindowInfo?> PickWindowAsync(CancellationToken cancellationToken = default);
}
