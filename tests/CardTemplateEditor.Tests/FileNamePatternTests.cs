using CardTemplateEditor.Services;

namespace CardTemplateEditor.Tests;

public class FileNamePatternTests
{
    [Fact]
    public void Format_DefaultPattern_ReplacesAllPlaceholders()
    {
        var ctx = new FileNameContext("pokemon", "glurak", "vorderseite", 1);

        var result = FileNamePattern.Format(FileNamePattern.Default, ctx);

        Assert.Equal("pokemon_glurak_vorderseite", result);
    }

    [Fact]
    public void Format_AllPlaceholders_AreSubstituted()
    {
        var ctx = new FileNameContext("T", "S", "I", 7);

        var result = FileNamePattern.Format("{template}-{set}-{image}-{index}", ctx);

        Assert.Equal("T-S-I-7", result);
    }

    [Theory]
    [InlineData("{template}", "Mein/Template", "Mein_Template")]
    [InlineData("{set}", @"Set\1", "Set_1")]
    [InlineData("{image}", "front:back", "front_back")]
    [InlineData("{template}", "ok-name", "ok-name")]
    public void Format_SanitizesDangerousCharacters(string pattern, string value, string expected)
    {
        var ctx = new FileNameContext(value, value, value, 1);

        var result = FileNamePattern.Format(pattern, ctx);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Format_EmptyOrNullValues_BecomeUnderscore()
    {
        var ctx = new FileNameContext("", "", "", 1);

        var result = FileNamePattern.Format("{template}-{set}-{image}", ctx);

        Assert.Equal("_-_-_", result);
    }

    [Fact]
    public void Format_BlankPattern_FallsBackToDefault()
    {
        var ctx = new FileNameContext("a", "b", "c", 1);

        var result = FileNamePattern.Format("   ", ctx);

        Assert.Equal("a_b_c", result);
    }

    [Fact]
    public void Format_LiteralSlashesInPattern_AreSanitizedFromFinalResult()
    {
        var ctx = new FileNameContext("a", "b", "c", 1);

        var result = FileNamePattern.Format("{template}/{set}", ctx);

        // Slashes im Pattern selbst werden ebenfalls zu '_'
        Assert.Equal("a_b", result);
    }

    [Fact]
    public void Sanitize_AllInvalidChars_BecomeUnderscore()
    {
        Assert.Equal("a_b_c", FileNamePattern.Sanitize("a/b\\c"));
        Assert.Equal("a_b", FileNamePattern.Sanitize("a:b"));
        Assert.Equal("_", FileNamePattern.Sanitize(""));
    }
}
