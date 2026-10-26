using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Workspacey.Core.Abstractions;
using Workspacey.Core.Models;

namespace Workspacey.Core.Services;

/// <summary>What the user chose in the assignment dialog.</summary>
/// <param name="ExistingRuleId">
/// The rule the dialog said it would update (from <see cref="WorkspaceAssignmentService.FindRuleForAsync"/>).
/// It is updated even if the user changed its pattern, instead of adding a second rule that the
/// first one would shadow.
/// </param>
public sealed record AssignmentRequest(
    WindowInfo Window,
    string DesktopId,
    string WindowClass,
    ClassMatchMode MatchMode,
    RulePolicy Policy,
    bool MoveWindowNow,
    string? ExistingRuleId = null);

/// <summary>Use case: "windows of this kind always open on that desktop".</summary>
public sealed class WorkspaceAssignmentService(
    IWorkspaceRuleRepository rules,
    IWindowManager windowManager,
    ILogger<WorkspaceAssignmentService> logger)
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// The first existing rule that would place <paramref name="window"/>, if any. Rules with
    /// conditions Workspacey can't evaluate (e.g. a title) are skipped rather than guessed.
    /// </summary>
    public async Task<WorkspaceRule?> FindRuleForAsync(WindowInfo window, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(window.MatchClass)) return null;

        var all = await rules.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(rule => !rule.HasOtherConditions && Matches(rule, window.MatchClass));
    }

    public async Task<WorkspaceRule> AssignAsync(AssignmentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WindowClass);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DesktopId);

        // Update the rule the dialog announced; otherwise reuse one with the identical pattern so repeated
        // assignments don't pile up duplicates.
        var all = await rules.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var existing =
            all.FirstOrDefault(rule => rule.Id == request.ExistingRuleId) ??
            all.FirstOrDefault(rule =>
                !rule.HasOtherConditions &&
                rule.MatchMode == request.MatchMode &&
                string.Equals(rule.WindowClass, request.WindowClass, StringComparison.Ordinal));

        var rule = new WorkspaceRule(
            Id: existing?.Id ?? Guid.NewGuid().ToString(),
            // Keep names given in KDE's Window Rules page; only new rules are named after the window.
            Description: string.IsNullOrWhiteSpace(existing?.Description) ? BuildDescription(request.Window) : existing.Description,
            WindowClass: request.WindowClass,
            MatchMode: request.MatchMode,
            DesktopIds: [request.DesktopId],
            Policy: request.Policy);

        await rules.SaveAsync(rule, cancellationToken).ConfigureAwait(false);
        await windowManager.ReloadRulesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("{Action} rule {RuleId}: '{WindowClass}' -> desktop {DesktopId}",
            existing is null ? "Created" : "Updated", rule.Id, rule.WindowClass, request.DesktopId);

        if (request.MoveWindowNow && !string.IsNullOrEmpty(request.Window.Id))
        {
            await windowManager.MoveWindowToDesktopAsync(request.Window.Id, request.DesktopId, cancellationToken)
                .ConfigureAwait(false);
        }

        return rule;
    }

    /// <summary>
    /// Points an existing rule at a different desktop. Only the desktop is written, so edits made to the
    /// rule elsewhere (e.g. in KDE's Window Rules page) since it was loaded are kept.
    /// </summary>
    public async Task<WorkspaceRule> ChangeDesktopAsync(WorkspaceRule rule, string desktopId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desktopId);

        if (!await rules.SetDesktopsAsync(rule.Id, [desktopId], cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"The rule \"{rule.Description}\" no longer exists. Refresh the list.");
        }

        await windowManager.ReloadRulesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Moved rule {RuleId} ('{WindowClass}') to desktop {DesktopId}", rule.Id, rule.WindowClass, desktopId);
        return rule with { DesktopIds = [desktopId] };
    }

    public async Task DeleteAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        await rules.DeleteAsync(ruleId, cancellationToken).ConfigureAwait(false);
        await windowManager.ReloadRulesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Deleted rule {RuleId}", ruleId);
    }

    /// <summary>Mirrors KWin's <c>wmclass</c> matching (case-sensitive).</summary>
    internal static bool Matches(WorkspaceRule rule, string windowClass) => rule.MatchMode switch
    {
        ClassMatchMode.Exact => string.Equals(rule.WindowClass, windowClass, StringComparison.Ordinal),
        ClassMatchMode.Substring => windowClass.Contains(rule.WindowClass, StringComparison.Ordinal),
        ClassMatchMode.Regex => SafeRegexMatch(rule.WindowClass, windowClass),
        _ => false,
    };

    private static bool SafeRegexMatch(string pattern, string input)
    {
        try
        {
            return Regex.IsMatch(input, pattern, RegexOptions.None, RegexTimeout);
        }
        catch (Exception e) when (e is ArgumentException or RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static string BuildDescription(WindowInfo window)
    {
        var caption = window.Caption.Trim();
        return string.IsNullOrEmpty(caption) ? $"Workspacey: {window.MatchClass}" : caption;
    }
}
