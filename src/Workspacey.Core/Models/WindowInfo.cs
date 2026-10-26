namespace Workspacey.Core.Models;

/// <summary>A window the user picked on screen, as reported by the window manager.</summary>
/// <param name="Id">Window manager internal id (KWin: uuid), used to move the live window.</param>
/// <param name="Caption">Window title.</param>
/// <param name="ResourceClass">WM_CLASS class part / Wayland app id, e.g. <c>steam_app_1234</c>.</param>
/// <param name="ResourceName">WM_CLASS name part.</param>
/// <param name="DesktopFile">Associated .desktop file name, if any.</param>
public sealed record WindowInfo(
    string Id,
    string Caption,
    string ResourceClass,
    string ResourceName,
    string DesktopFile)
{
    /// <summary>The identifier a window rule should match on: resource class, falling back to resource name.</summary>
    public string MatchClass =>
        !string.IsNullOrWhiteSpace(ResourceClass) ? ResourceClass : ResourceName;
}
