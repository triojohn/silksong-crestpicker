using CrestPicker.Pure;
using Xunit;

namespace CrestPicker.Tests;

public class CrestLabelTests
{
    [Fact]
    public void DisplayName_IsUsedWhenPresent()
    {
        Assert.Equal("Reaper", CrestLabel.LabelOrName("Reaper", "Crest_Reaper"));
    }

    [Fact]
    public void CyrillicDisplayName_IsUnchanged()
    {
        Assert.Equal("Жнец", CrestLabel.LabelOrName("Жнец", "Crest_Reaper"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void BlankDisplayName_FallsBackToAssetName(string? displayName)
    {
        Assert.Equal("Crest_Reaper", CrestLabel.LabelOrName(displayName, "Crest_Reaper"));
    }

    [Fact]
    public void BothNull_GivesEmptyString()
    {
        Assert.Equal(string.Empty, CrestLabel.LabelOrName(null, null));
    }
}
