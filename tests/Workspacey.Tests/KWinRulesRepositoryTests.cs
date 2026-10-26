using Workspacey.Core.Models;
using Workspacey.Kde.Config;
using Workspacey.Kde.Rules;

namespace Workspacey.Tests;

public sealed class KWinRulesRepositoryTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("workspacey-tests").FullName;

    private string RulesFile => Path.Combine(_directory, "kwinrulesrc");

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task GetAll_ReturnsOnlyDesktopRules()
    {
        await File.WriteAllTextAsync(RulesFile, """
            [General]
            Order=game,other

            [game]
            Description=Some game
            desktops=desk-2
            desktopsrule=3
            wmclass=steam_app_1
            wmclassmatch=1

            [other]
            Description=Not about desktops
            above=true
            aboverule=2
            wmclass=firefox
            wmclassmatch=1
            """);
        using var repository = new KWinRulesRepository(RulesFile);

        var rule = Assert.Single(await repository.GetAllAsync());

        Assert.Equal("game", rule.Id);
        Assert.Equal("Some game", rule.Description);
        Assert.Equal("steam_app_1", rule.WindowClass);
        Assert.Equal(ClassMatchMode.Exact, rule.MatchMode);
        Assert.Equal(RulePolicy.ApplyInitially, rule.Policy);
        Assert.Equal(["desk-2"], rule.DesktopIds);
    }

    [Fact]
    public async Task Save_NewRule_CreatesFileAndOrder()
    {
        using var repository = new KWinRulesRepository(RulesFile);

        await repository.SaveAsync(NewRule("r1"));

        var file = KConfigFile.Parse(await File.ReadAllTextAsync(RulesFile));
        Assert.Equal(["r1"], KWinRulesRepository.GetRuleIds(file));
        Assert.Equal("steam_app_42", file.FindGroup("r1")!.Get("wmclass"));
        Assert.Equal("3", file.FindGroup("r1")!.Get("desktopsrule"));
    }

    [Fact]
    public async Task Save_ExistingRule_KeepsUnrelatedKeysAndOrder()
    {
        await File.WriteAllTextAsync(RulesFile, """
            [General]
            Order=r1,r2

            [r1]
            desktops=old
            desktopsrule=3
            wmclass=steam_app_42
            wmclassmatch=1
            noborder=true
            noborderrule=2

            [r2]
            wmclass=x
            """);
        using var repository = new KWinRulesRepository(RulesFile);

        await repository.SaveAsync(NewRule("r1") with { DesktopIds = ["new"] });

        var file = KConfigFile.Parse(await File.ReadAllTextAsync(RulesFile));
        Assert.Equal(["r1", "r2"], KWinRulesRepository.GetRuleIds(file));
        Assert.Equal("new", file.FindGroup("r1")!.Get("desktops"));
        Assert.Equal("true", file.FindGroup("r1")!.Get("noborder"));
    }

    [Fact]
    public async Task LegacyIndexKeys_AreKeptInSync()
    {
        await File.WriteAllTextAsync(RulesFile, """
            [General]
            count=1
            rules=old

            [old]
            wmclass=x
            """);
        using var repository = new KWinRulesRepository(RulesFile);

        await repository.SaveAsync(NewRule("r1"));
        await repository.DeleteAsync("old");

        var general = KConfigFile.Parse(await File.ReadAllTextAsync(RulesFile)).FindGroup("General")!;
        Assert.Equal(["r1"], general.GetList("Order"));
        Assert.Equal(["r1"], general.GetList("rules"));
        Assert.Equal(1, general.GetInt("count"));
    }

    [Fact]
    public async Task Delete_RemovesGroupAndIndexEntry()
    {
        using var repository = new KWinRulesRepository(RulesFile);
        await repository.SaveAsync(NewRule("r1"));
        await repository.SaveAsync(NewRule("r2"));

        await repository.DeleteAsync("r1");

        var file = KConfigFile.Parse(await File.ReadAllTextAsync(RulesFile));
        Assert.Null(file.FindGroup("r1"));
        Assert.Equal(["r2"], KWinRulesRepository.GetRuleIds(file));
    }

    [Fact]
    public async Task Save_ThroughSymlink_UpdatesTargetAndKeepsLink()
    {
        var target = Path.Combine(_directory, "real-kwinrulesrc");
        await File.WriteAllTextAsync(target, "");
        File.CreateSymbolicLink(RulesFile, target);
        using var repository = new KWinRulesRepository(RulesFile);

        await repository.SaveAsync(NewRule("r1"));

        Assert.NotNull(new FileInfo(RulesFile).LinkTarget);
        Assert.Contains("[r1]", await File.ReadAllTextAsync(target), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("title=Picture-in-Picture\ntitlematch=1", true)]
    [InlineData("title=ignored\ntitlematch=0", false)] // 0 = "unimportant" in KWin
    [InlineData("windowrole=pip\nwindowrolematch=2", true)]
    [InlineData("clientmachine=host\nclientmachinematch=1", true)]
    [InlineData("wmclasscomplete=true", true)]
    [InlineData("types=1", true)]
    [InlineData("types=65535", false)] // all window types
    public async Task GetAll_FlagsRulesWithOtherConditions(string extraKeys, bool expected)
    {
        await File.WriteAllTextAsync(RulesFile, $"""
            [General]
            Order=r

            [r]
            desktops=d
            desktopsrule=3
            wmclass=firefox
            wmclassmatch=1
            {extraKeys}
            """);
        using var repository = new KWinRulesRepository(RulesFile);

        Assert.Equal(expected, Assert.Single(await repository.GetAllAsync()).HasOtherConditions);
    }

    [Fact]
    public async Task SetDesktops_ChangesOnlyTheDesktops()
    {
        await File.WriteAllTextAsync(RulesFile, """
            [General]
            Order=r

            [r]
            Description=Edited elsewhere
            desktops=old
            desktopsrule=2
            wmclass=steam_app_\d+
            wmclassmatch=3
            """);
        using var repository = new KWinRulesRepository(RulesFile);

        Assert.True(await repository.SetDesktopsAsync("r", ["new"]));
        Assert.False(await repository.SetDesktopsAsync("missing", ["new"]));

        var rule = Assert.Single(await repository.GetAllAsync());
        Assert.Equal(["new"], rule.DesktopIds);
        Assert.Equal(("Edited elsewhere", RulePolicy.Force), (rule.Description, rule.Policy));
        Assert.Equal(@"steam_app_\d+", rule.WindowClass); // unknown escape survives the rewrite
    }

    [Fact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task Save_KeepsFilePermissionsAndLeavesNoTempFile()
    {
        await File.WriteAllTextAsync(RulesFile, "");
        File.SetUnixFileMode(RulesFile, UnixFileMode.UserRead | UnixFileMode.UserWrite); // 0600
        using var repository = new KWinRulesRepository(RulesFile);

        await repository.SaveAsync(NewRule("r1"));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(RulesFile));
        Assert.Equal(["kwinrulesrc"], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Save_RenameFails_LeavesNoTempFile()
    {
        Directory.CreateDirectory(RulesFile); // a directory in the way makes the final rename fail
        using var repository = new KWinRulesRepository(RulesFile);

        await Assert.ThrowsAnyAsync<IOException>(() => repository.SaveAsync(NewRule("r1")));

        Assert.Empty(Directory.GetFiles(_directory));
    }

    private static WorkspaceRule NewRule(string id) =>
        new(id, "A game", "steam_app_42", ClassMatchMode.Exact, ["desk-1"], RulePolicy.ApplyInitially);
}
