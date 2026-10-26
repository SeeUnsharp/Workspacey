using Workspacey.Core.Abstractions;
using Workspacey.Core.Models;
using Workspacey.Kde.Config;

namespace Workspacey.Kde.Rules;

/// <summary>
/// Stores desktop rules in <c>kwinrulesrc</c>, the same file KDE's "Window Rules" settings page edits,
/// so rules created here are visible and editable there too.
/// </summary>
/// <remarks>
/// Layout: one group per rule (named by rule id) plus <c>[General]</c>, whose <c>Order</c> key lists the
/// rule ids in evaluation order. Older Plasma versions used <c>rules</c> + <c>count</c> instead; those keys
/// are kept in sync when present.
/// </remarks>
public sealed class KWinRulesRepository(string filePath) : IWorkspaceRuleRepository, IDisposable
{
    internal const string GeneralGroup = "General";
    internal const string OrderKey = "Order";
    internal const string LegacyRulesKey = "rules";
    internal const string LegacyCountKey = "count";

    private const string DescriptionKey = "Description";
    private const string WmClassKey = "wmclass";
    private const string WmClassMatchKey = "wmclassmatch";
    private const string DesktopsKey = "desktops";
    private const string DesktopsRuleKey = "desktopsrule";

    private readonly SemaphoreSlim _lock = new(1, 1);

    public KWinRulesRepository() : this(Path.Combine(XdgPaths.ConfigHome, "kwinrulesrc"))
    {
    }

    public async Task<IReadOnlyList<WorkspaceRule>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await LoadAsync(cancellationToken).ConfigureAwait(false);
            return [.. GetRuleIds(file)
                .Select(file.FindGroup)
                .OfType<KConfigGroup>()
                .Select(TryMapRule)
                .OfType<WorkspaceRule>()];
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task SaveAsync(WorkspaceRule rule, CancellationToken cancellationToken = default) =>
        ModifyAsync(file =>
        {
            var group = file.GetOrAddGroup(rule.Id);
            group.Set(DescriptionKey, rule.Description);
            group.Set(WmClassKey, rule.WindowClass);
            group.Set(WmClassMatchKey, (int)rule.MatchMode);
            group.SetList(DesktopsKey, rule.DesktopIds);
            group.Set(DesktopsRuleKey, (int)rule.Policy);

            var ids = GetRuleIds(file);
            if (!ids.Contains(rule.Id)) WriteRuleIds(file, [.. ids, rule.Id]);
        }, cancellationToken);

    public Task DeleteAsync(string ruleId, CancellationToken cancellationToken = default) =>
        ModifyAsync(file =>
        {
            file.RemoveGroup(ruleId);
            WriteRuleIds(file, [.. GetRuleIds(file).Where(id => id != ruleId)]);
        }, cancellationToken);

    public void Dispose() => _lock.Dispose();

    internal static IReadOnlyList<string> GetRuleIds(KConfigFile file)
    {
        var general = file.FindGroup(GeneralGroup);
        if (general is null) return [];

        return general.ContainsKey(OrderKey) ? general.GetList(OrderKey) : general.GetList(LegacyRulesKey);
    }

    private static void WriteRuleIds(KConfigFile file, IReadOnlyList<string> ids)
    {
        var general = file.GetOrAddGroup(GeneralGroup);
        general.SetList(OrderKey, ids);

        if (general.ContainsKey(LegacyRulesKey))
        {
            general.SetList(LegacyRulesKey, ids);
            general.Set(LegacyCountKey, ids.Count);
        }
    }

    private static WorkspaceRule? TryMapRule(KConfigGroup group)
    {
        var desktops = group.GetList(DesktopsKey);
        var windowClass = group.Get(WmClassKey);

        if (desktops.Count == 0 || string.IsNullOrEmpty(windowClass)) return null;
        if (group.GetInt(DesktopsRuleKey) is not { } policy || !Enum.IsDefined((RulePolicy)policy)) return null;
        if (group.GetInt(WmClassMatchKey) is not { } match || !Enum.IsDefined((ClassMatchMode)match)) return null;

        return new WorkspaceRule(
            group.Name,
            group.Get(DescriptionKey) ?? string.Empty,
            windowClass,
            (ClassMatchMode)match,
            desktops,
            (RulePolicy)policy);
    }

    private async Task ModifyAsync(Action<KConfigFile> modify, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = await LoadAsync(cancellationToken).ConfigureAwait(false);
            modify(file);
            await SaveAtomicallyAsync(file, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<KConfigFile> LoadAsync(CancellationToken cancellationToken) =>
        File.Exists(filePath)
            ? KConfigFile.Parse(await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false))
            : new KConfigFile();

    private async Task SaveAtomicallyAsync(KConfigFile file, CancellationToken cancellationToken)
    {
        // Write next to the real file (it may be a symlink into a dotfiles repo), then rename over it.
        var info = new FileInfo(filePath);
        var target = info.LinkTarget is null ? filePath : info.ResolveLinkTarget(returnFinalTarget: true)!.FullName;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var temp = $"{target}.{Environment.ProcessId}.tmp";
        await File.WriteAllTextAsync(temp, file.ToString(), cancellationToken).ConfigureAwait(false);
        File.Move(temp, target, overwrite: true);
    }
}
