using Workspacey.Core.Models;

namespace Workspacey.Core.Abstractions;

/// <summary>Persistent store of desktop placement rules (KDE: <c>kwinrulesrc</c>).</summary>
public interface IWorkspaceRuleRepository
{
    /// <summary>All rules that assign windows to a desktop. Other rule kinds are not returned.</summary>
    Task<IReadOnlyList<WorkspaceRule>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Inserts the rule, or overwrites the class matching, desktop and description of the rule with
    /// the same id. Other settings of an existing rule are kept.
    /// </summary>
    Task SaveAsync(WorkspaceRule rule, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes only the target desktops of a rule, based on the current stored state, so edits made
    /// elsewhere in the meantime survive. Returns <c>false</c> if the rule no longer exists.
    /// </summary>
    Task<bool> SetDesktopsAsync(string ruleId, IReadOnlyList<string> desktopIds, CancellationToken cancellationToken = default);

    Task DeleteAsync(string ruleId, CancellationToken cancellationToken = default);
}
