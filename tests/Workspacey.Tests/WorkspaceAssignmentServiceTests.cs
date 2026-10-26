using Microsoft.Extensions.Logging.Abstractions;
using Workspacey.Core.Abstractions;
using Workspacey.Core.Models;
using Workspacey.Core.Services;

namespace Workspacey.Tests;

public class WorkspaceAssignmentServiceTests
{
    private static readonly WindowInfo Game = new("{abc}", "Cool Game", "steam_app_7", "game.exe", "");

    private readonly InMemoryRules _rules = new();
    private readonly RecordingWindowManager _windowManager = new();

    private WorkspaceAssignmentService CreateService() =>
        new(_rules, _windowManager, NullLogger<WorkspaceAssignmentService>.Instance);

    [Fact]
    public async Task Assign_CreatesRuleReloadsAndMovesWindow()
    {
        var rule = await CreateService().AssignAsync(Request("desk-2"));

        Assert.Equal("Cool Game", rule.Description);
        Assert.Equal(["desk-2"], Assert.Single(_rules.Rules).DesktopIds);
        Assert.Equal(1, _windowManager.Reloads);
        Assert.Equal(("{abc}", "desk-2"), Assert.Single(_windowManager.Moves));
    }

    [Fact]
    public async Task Assign_SamePatternTwice_UpdatesInsteadOfDuplicating()
    {
        var service = CreateService();
        var first = await service.AssignAsync(Request("desk-1"));

        var second = await service.AssignAsync(Request("desk-2") with { MoveWindowNow = false });

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(["desk-2"], Assert.Single(_rules.Rules).DesktopIds);
        Assert.Single(_windowManager.Moves);
    }

    [Fact]
    public async Task ChangeDesktop_UpdatesOnlyTheDesktopAndReloads()
    {
        var service = CreateService();
        var rule = await service.AssignAsync(Request("desk-1") with { Policy = RulePolicy.Force, MoveWindowNow = false });

        await service.ChangeDesktopAsync(rule, "desk-2");

        var stored = Assert.Single(_rules.Rules);
        Assert.Equal(["desk-2"], stored.DesktopIds);
        Assert.Equal(RulePolicy.Force, stored.Policy);
        Assert.Equal(2, _windowManager.Reloads);
    }

    [Theory]
    [InlineData(ClassMatchMode.Exact, "steam_app_7", true)]
    [InlineData(ClassMatchMode.Exact, "steam_app", false)]
    [InlineData(ClassMatchMode.Substring, "steam_app", true)]
    [InlineData(ClassMatchMode.Regex, "^steam_app_\\d+$", true)]
    [InlineData(ClassMatchMode.Regex, "([", false)] // invalid pattern never matches
    public void Matches_FollowsKWinSemantics(ClassMatchMode mode, string pattern, bool expected)
    {
        var rule = new WorkspaceRule("id", "", pattern, mode, ["d"], RulePolicy.ApplyInitially);

        Assert.Equal(expected, WorkspaceAssignmentService.Matches(rule, Game.MatchClass));
    }

    private static AssignmentRequest Request(string desktopId) =>
        new(Game, desktopId, Game.MatchClass, ClassMatchMode.Exact, RulePolicy.ApplyInitially, MoveWindowNow: true);

    private sealed class InMemoryRules : IWorkspaceRuleRepository
    {
        public List<WorkspaceRule> Rules { get; } = [];

        public Task<IReadOnlyList<WorkspaceRule>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<WorkspaceRule>>([.. Rules]);

        public Task SaveAsync(WorkspaceRule rule, CancellationToken cancellationToken = default)
        {
            Rules.RemoveAll(r => r.Id == rule.Id);
            Rules.Add(rule);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string ruleId, CancellationToken cancellationToken = default)
        {
            Rules.RemoveAll(r => r.Id == ruleId);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingWindowManager : IWindowManager
    {
        public int Reloads { get; private set; }

        public List<(string WindowId, string DesktopId)> Moves { get; } = [];

        public Task ReloadRulesAsync(CancellationToken cancellationToken = default)
        {
            Reloads++;
            return Task.CompletedTask;
        }

        public Task MoveWindowToDesktopAsync(string windowId, string desktopId, CancellationToken cancellationToken = default)
        {
            Moves.Add((windowId, desktopId));
            return Task.CompletedTask;
        }
    }
}
