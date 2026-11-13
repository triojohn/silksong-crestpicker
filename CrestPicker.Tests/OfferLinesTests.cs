using CrestPicker.Pure;
using Xunit;

namespace CrestPicker.Tests;

public class OfferLinesTests
{
    [Fact]
    public void Compose_KeepsOrderAndTrims()
    {
        string text = OfferLines.Compose(new string?[] { "  first  ", " second ", "third\t" });

        Assert.Equal("first<page>second<page>third", text);
    }

    [Fact]
    public void Compose_DropsBlankAndNull()
    {
        string text = OfferLines.Compose(new string?[] { null, "one", "   ", "", null, "two" });

        Assert.Equal("one<page>two", text);
    }

    [Fact]
    public void Compose_JoinsWithPageTag()
    {
        string text = OfferLines.Compose(new string?[] { "one", "two", "three" });

        Assert.Equal(2, CountOf(text, "<page>"));
        Assert.Equal("one<page>two<page>three", text);
    }

    [Fact]
    public void Compose_AllEmpty_IsEmpty()
    {
        string text = OfferLines.Compose(new string?[] { null, "", "  ", "\t" });

        Assert.Equal(string.Empty, text);
        Assert.True(OfferLines.IsEmpty(text));
        Assert.True(OfferLines.IsEmpty(null));
        Assert.False(OfferLines.IsEmpty("line"));
    }

    [Fact]
    public void Compose_DropsLineWithAngleBracket()
    {
        string text = OfferLines.Compose(
            new string?[] { "keep", "drop <b>this</b>", "drop <page>tag", "also keep", "a > b" });

        Assert.Equal("keep<page>also keep", text);
    }

    [Fact]
    public void Compose_SingleLine_HasNoPageTag()
    {
        string text = OfferLines.Compose(new string?[] { "  only line  " });

        Assert.Equal("only line", text);
        Assert.DoesNotContain("<page>", text);
    }

    private static int CountOf(string text, string token)
    {
        int count = 0;
        int index = text.IndexOf(token, System.StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(token, index + token.Length, System.StringComparison.Ordinal);
        }

        return count;
    }
}
