using Workspacey.Core.Models;

namespace Workspacey.Tests;

public class KeyChordTests
{
    [Fact]
    public void MetaShiftW_EncodesLikeQt()
    {
        var chord = new KeyChord(KeyModifiers.Meta | KeyModifiers.Shift, 'W');

        Assert.Equal(0x10000000 | 0x02000000 | 'W', chord.ToQtKeyCombination());
        Assert.Equal("Meta+Shift+W", chord.ToString());
    }

    [Fact]
    public void QtCombination_RoundTrips()
    {
        var chord = new KeyChord(KeyModifiers.Ctrl | KeyModifiers.Alt, 0x01000030 + 4); // F5

        Assert.True(KeyChord.TryFromQtKeyCombination(chord.ToQtKeyCombination(), out var parsed));
        Assert.Equal(chord, parsed);
        Assert.Equal("Ctrl+Alt+F5", parsed.ToString());
    }

    [Theory]
    [InlineData(KeyModifiers.None, 'A', false)]
    [InlineData(KeyModifiers.Meta, 'A', true)]
    [InlineData(KeyModifiers.None, 0x01000030 + 11, true)] // bare F12 is fine
    public void IsValidGlobalShortcut(KeyModifiers modifiers, int key, bool expected) =>
        Assert.Equal(expected, new KeyChord(modifiers, key).IsValidGlobalShortcut);
}
