using Workspacey.Kde.Config;

namespace Workspacey.Tests;

public class KConfigFileTests
{
    [Fact]
    public void RoundTrip_PreservesGroupsKeysAndOrder()
    {
        const string text = """
            [General]
            Order=b,a

            [a]
            Description=First
            wmclass=steam_app_1

            [b]
            unknownkey=kept
            """;

        var file = KConfigFile.Parse(text);

        Assert.Equal(["General", "a", "b"], file.Groups.Select(g => g.Name));
        Assert.Equal(text.ReplaceLineEndings("\n") + "\n", file.ToString());
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData(" leading", @"\sleading")]
    [InlineData("trailing ", @"trailing\s")]
    [InlineData("multi\nline", @"multi\nline")]
    [InlineData(@"back\slash", @"back\\slash")]
    public void Escaping_RoundTrips(string value, string raw)
    {
        Assert.Equal(raw, KConfigFile.Escape(value));
        Assert.Equal(value, KConfigFile.Unescape(raw));
    }

    [Fact]
    public void Lists_EscapeCommas()
    {
        var group = KConfigFile.Parse("[g]").GetOrAddGroup("g");

        group.SetList("list", ["a", "b,c", @"d\e"]);

        Assert.Equal(["a", "b,c", @"d\e"], group.GetList("list"));
    }

    [Theory]
    [InlineData(@"steam_app_\d+", @"steam_app_\d+")] // unknown escape: KConfig keeps the backslash
    [InlineData(@"\x41\x62", "Ab")]
    [InlineData(@"\xc3\xbcber", "über")]             // \xNN are UTF-8 bytes
    [InlineData(@"bad\xZZ", @"bad\xZZ")]
    [InlineData(@"trailing\", @"trailing\")]
    public void Unescape_FollowsKConfig(string raw, string expected) =>
        Assert.Equal(expected, KConfigFile.Unescape(raw));

    [Fact]
    public void Value_WithUnknownEscape_SurvivesReadWriteRead()
    {
        var group = KConfigFile.Parse("[g]\nwmclass=steam_app_\\d+\n").FindGroup("g")!;

        group.Set("wmclass", group.Get("wmclass")!);

        Assert.Equal(@"steam_app_\d+", group.Get("wmclass"));
    }

    [Fact]
    public void Escape_EncodesControlCharactersAsHex() =>
        Assert.Equal(@"a\x01b", KConfigFile.Escape("a\u0001b"));

    [Fact]
    public void Comments_InsideGroups_ArePreserved()
    {
        const string text = "[a]\n# keep this for game X\nkey=1\n";
        var file = KConfigFile.Parse(text);

        file.FindGroup("a")!.Set("key", "2");

        Assert.Equal("[a]\n# keep this for game X\nkey=2\n", file.ToString());
    }

    [Fact]
    public void DuplicateGroups_AreMergedWithLaterValuesWinning()
    {
        var file = KConfigFile.Parse("[a]\nx=1\ny=1\n\n[b]\n\n[a]\ny=2\n");

        var group = Assert.Single(file.Groups, g => g.Name == "a");
        Assert.Equal(("1", "2"), (group.Get("x"), group.Get("y")));
        Assert.Equal(["a", "b"], file.Groups.Select(g => g.Name));
    }

    [Fact]
    public void ValuesMayContainEqualsSigns()
    {
        var file = KConfigFile.Parse("[g]\ntitle=a=b\n");

        Assert.Equal("a=b", file.FindGroup("g")!.Get("title"));
    }
}
