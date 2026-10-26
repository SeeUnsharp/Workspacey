namespace Workspacey.Core.Models;

/// <summary>How the window class of a rule is compared (values match KWin's <c>wmclassmatch</c>).</summary>
public enum ClassMatchMode
{
    Exact = 1,
    Substring = 2,
    Regex = 3,
}

/// <summary>When the desktop assignment is enforced (values match KWin's rule policies).</summary>
public enum RulePolicy
{
    /// <summary>Force: the window can never leave the desktop.</summary>
    Force = 2,

    /// <summary>Apply initially: the window opens on the desktop but may be moved afterwards.</summary>
    ApplyInitially = 3,
}

/// <summary>A window rule that places windows of a class on a virtual desktop.</summary>
/// <param name="Id">Rule id (the kwinrulesrc group name).</param>
/// <param name="Description">Human readable description shown in KDE's window rules KCM.</param>
/// <param name="WindowClass">Window class pattern.</param>
/// <param name="MatchMode">How <paramref name="WindowClass"/> is compared.</param>
/// <param name="DesktopIds">Target desktops (usually exactly one).</param>
/// <param name="Policy">When the rule is enforced.</param>
/// <param name="HasOtherConditions">
/// The rule also matches on something Workspacey does not evaluate (title, window role, machine,
/// window types, or the complete "name class" string). Such rules are listed and can be edited, but
/// are never assumed to match a picked window.
/// </param>
public sealed record WorkspaceRule(
    string Id,
    string Description,
    string WindowClass,
    ClassMatchMode MatchMode,
    IReadOnlyList<string> DesktopIds,
    RulePolicy Policy,
    bool HasOtherConditions = false);
