using UiPilot.Interaction;
using UiPilot.Tools;
using Xunit;

namespace UiPilot.Tests;

public class KeyChordTests
{
    [Theory]
    [InlineData("ctrl+s", KeyModifier.Control, "S")]
    [InlineData("Control+Shift+Enter", KeyModifier.Control | KeyModifier.Shift, "ENTER")]
    [InlineData("alt+f4", KeyModifier.Alt, "F4")]
    [InlineData("win+e", KeyModifier.Meta, "E")]
    [InlineData("Enter", KeyModifier.None, "ENTER")]
    [InlineData("pgdn", KeyModifier.None, "PAGEDOWN")]
    public void Parse_CanonicalizesChords(string keys, KeyModifier modifiers, string token)
    {
        var chord = KeyChord.Parse(keys);
        Assert.False(chord.IsPlainText);
        Assert.Equal(modifiers, chord.Modifiers);
        Assert.Equal(token, chord.KeyToken);
    }

    [Fact]
    public void Parse_TreatsLiteralPlusTextAsPlain()
    {
        var chord = KeyChord.Parse("2+2");
        Assert.True(chord.IsPlainText);
        Assert.Equal("2+2", chord.KeyToken);
    }

    [Fact]
    public void Parse_Empty_Throws()
    {
        var ex = Assert.Throws<PilotToolException>(() => KeyChord.Parse(""));
        Assert.Equal(PilotErrorCodes.InvalidArgs, ex.Code);
    }

    [Fact]
    public void StartsWithModifier_IsSharedAcrossAdapters()
    {
        Assert.True(KeyChord.StartsWithModifier("ctrl+s"));
        Assert.False(KeyChord.StartsWithModifier("hello"));
    }
}
