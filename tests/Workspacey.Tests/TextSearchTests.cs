using Workspacey.Core.Services;

namespace Workspacey.Tests;

public class TextSearchTests
{
    [Theory]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("corsa", true)]
    [InlineData("CORSA evo", true)]          // every term, any order, any case
    [InlineData("evo steam_app", true)]      // terms may come from different fields
    [InlineData("gaming", true)]             // workspace name
    [InlineData("rally", false)]
    public void Matches_TermsAcrossFields(string query, bool expected) =>
        Assert.Equal(expected, TextSearch.Matches(query, "Assetto Corsa EVO", "steam_app_3058630", "2: Gaming"));

    [Fact]
    public void Matches_IgnoresAccents() =>
        Assert.True(TextSearch.Matches("ubersicht", "Übersicht — Systemmonitor"));

    [Fact]
    public void Matches_IgnoresZeroWidthCharacters() =>
        Assert.True(TextSearch.Matches("arc raiders", "​A​﻿RC R​ai﻿ders"));

    [Fact]
    public void Matches_SkipsNullFields() =>
        Assert.False(TextSearch.Matches("x", null, ""));
}
