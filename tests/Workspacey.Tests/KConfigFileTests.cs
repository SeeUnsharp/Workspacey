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

    [Fact]
    public void ValuesMayContainEqualsSigns()
    {
        var file = KConfigFile.Parse("[g]\ntitle=a=b\n");

        Assert.Equal("a=b", file.FindGroup("g")!.Get("title"));
    }
}
