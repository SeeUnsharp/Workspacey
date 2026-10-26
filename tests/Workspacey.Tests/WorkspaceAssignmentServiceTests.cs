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

    [Fact]
    public async Task ChangeDesktop_WithStaleSnapshot_KeepsEditsMadeElsewhere()
    {
        var service = CreateService();
        var snapshot = await service.AssignAsync(Request("desk-1") with { MoveWindowNow = false });
        // Meanwhile the rule is edited in KDE's Window Rules page.
        _rules.Rules[0] = _rules.Rules[0] with { Description = "Edited in KCM", WindowClass = "steam_app_8", Policy = RulePolicy.Force };

        await service.ChangeDesktopAsync(snapshot, "desk-2");

        var stored = Assert.Single(_rules.Rules);
        Assert.Equal(("Edited in KCM", "steam_app_8", RulePolicy.Force), (stored.Description, stored.WindowClass, stored.Policy));
        Assert.Equal(["desk-2"], stored.DesktopIds);
    }

    [Fact]
    public async Task ChangeDesktop_RuleDeletedMeanwhile_Throws()
    {
        var service = CreateService();
        var rule = await service.AssignAsync(Request("desk-1") with { MoveWindowNow = false });
        _rules.Rules.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ChangeDesktopAsync(rule, "desk-2"));
    }

    [Fact]
    public async Task Assign_WithAnnouncedRule_UpdatesItEvenIfPatternChanged()
    {
        // A broad rule matches the game; the user narrows it to the exact class in the dialog.
        _rules.Rules.Add(new WorkspaceRule("broad", "All Steam games", "steam_app", ClassMatchMode.Substring, ["desk-2"], RulePolicy.ApplyInitially));
        var service = CreateService();
        var announced = await service.FindRuleForAsync(Game);

        await service.AssignAsync(Request("desk-3") with { ExistingRuleId = announced!.Id });

        var stored = Assert.Single(_rules.Rules); // no second rule that the first would shadow
        Assert.Equal(("broad", "steam_app_7", ClassMatchMode.Exact), (stored.Id, stored.WindowClass, stored.MatchMode));
        Assert.Equal(["desk-3"], stored.DesktopIds);
    }

    [Fact]
    public async Task Assign_UpdatingExistingRule_KeepsItsDescription()
    {
        _rules.Rules.Add(new WorkspaceRule("r", "All Steam games → Gaming", "steam_app_7", ClassMatchMode.Exact, ["desk-1"], RulePolicy.ApplyInitially));

        var rule = await CreateService().AssignAsync(Request("desk-2"));

        Assert.Equal("All Steam games → Gaming", rule.Description);
    }

    [Fact]
    public async Task FindRuleFor_SkipsRulesWithConditionsItCannotEvaluate()
    {
        // e.g. wmclass=steam_app_7 plus a title condition: may not apply to this window at all.
        _rules.Rules.Add(new WorkspaceRule("title", "PiP", "steam_app_7", ClassMatchMode.Exact, ["desk-1"], RulePolicy.ApplyInitially, HasOtherConditions: true));
        var service = CreateService();

        Assert.Null(await service.FindRuleForAsync(Game));

        // ...and saving creates a separate class-only rule instead of rewriting the title rule.
        await service.AssignAsync(Request("desk-2"));
        Assert.Equal(2, _rules.Rules.Count);
        Assert.Equal(["desk-1"], _rules.Rules.Single(r => r.Id == "title").DesktopIds);
    }

    [Fact]
    public async Task FindRuleFor_WindowWithoutClass_FindsNothing()
    {
        _rules.Rules.Add(new WorkspaceRule("r", "", "game.exe", ClassMatchMode.Exact, ["desk-1"], RulePolicy.ApplyInitially));
        var classless = Game with { ResourceClass = "" }; // only the resource name is "game.exe"

        Assert.Equal("", classless.MatchClass); // KWin never matches wmclass against the resource name
        Assert.Null(await CreateService().FindRuleForAsync(classless));
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
            var index = Rules.FindIndex(r => r.Id == rule.Id);
            if (index >= 0) Rules[index] = rule; // keeps the evaluation order, like kwinrulesrc
            else Rules.Add(rule);
            return Task.CompletedTask;
        }

        public Task<bool> SetDesktopsAsync(string ruleId, IReadOnlyList<string> desktopIds, CancellationToken cancellationToken = default)
        {
            var index = Rules.FindIndex(r => r.Id == ruleId);
            if (index < 0) return Task.FromResult(false);
            Rules[index] = Rules[index] with { DesktopIds = desktopIds };
            return Task.FromResult(true);
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
