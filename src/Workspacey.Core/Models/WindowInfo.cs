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
    /// <summary>
    /// The value KWin compares a rule's <c>wmclass</c> with: the resource class only (the resource
    /// name is never consulted for class rules). Empty if the window has no class; then no class
    /// rule can match it.
    /// </summary>
    public string MatchClass => ResourceClass;
}
