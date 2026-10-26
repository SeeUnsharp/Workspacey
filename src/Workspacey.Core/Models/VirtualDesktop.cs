namespace Workspacey.Core.Models;

/// <summary>A virtual desktop (KDE calls them workspaces / virtual desktops).</summary>
/// <param name="Id">Stable desktop id (a uuid on Plasma 6).</param>
/// <param name="Position">Zero-based position in the desktop grid.</param>
/// <param name="Name">User visible name.</param>
public sealed record VirtualDesktop(string Id, int Position, string Name)
{
    public string DisplayName => $"{Position + 1}: {Name}";
}
