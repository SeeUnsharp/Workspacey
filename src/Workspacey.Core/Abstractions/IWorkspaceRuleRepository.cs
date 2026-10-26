using Workspacey.Core.Models;

namespace Workspacey.Core.Abstractions;

/// <summary>Persistent store of desktop placement rules (KDE: <c>kwinrulesrc</c>).</summary>
public interface IWorkspaceRuleRepository
{
    /// <summary>All rules that assign windows to a desktop. Other rule kinds are not returned.</summary>
    Task<IReadOnlyList<WorkspaceRule>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Inserts the rule, or replaces the desktop settings of the rule with the same id.</summary>
    Task SaveAsync(WorkspaceRule rule, CancellationToken cancellationToken = default);

    Task DeleteAsync(string ruleId, CancellationToken cancellationToken = default);
}
