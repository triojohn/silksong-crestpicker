using CrestPicker.Pure;
using Xunit;

namespace CrestPicker.Tests;

/// <summary>
/// Contract of <see cref="CloneFilter.Evaluate"/> for the nodes of a cloned crest card.
/// The silhouette and the slot widgets always stay; the vanilla effects go off, because
/// their glow and white flash draw over the silhouette (bf08df8).
/// </summary>
public class CloneFilterTests
{
    // The bug from bf08df8: the vanilla effects holder glowed over the silhouette.
    [Theory]
    [InlineData("Crest Submit Effects")]
    [InlineData("crest submit effects")]
    [InlineData("CREST SUBMIT EFFECTS")]
    public void SubmitEffects_WithoutSilhouetteOrSlot_Disable(string name)
    {
        Assert.Equal(CloneNode.Disable, CloneFilter.Evaluate(name, isSilhouette: false, inSlot: false));
    }

    // Crest Glow is switched off by its parent, not by this rule.
    [Theory]
    [InlineData("Crest Glow")]
    [InlineData("Crest Sprite")]
    public void OtherCrestNodes_WithoutSilhouetteOrSlot_Keep(string name)
    {
        Assert.Equal(CloneNode.Keep, CloneFilter.Evaluate(name, isSilhouette: false, inSlot: false));
    }

    [Theory]
    [InlineData("Equip Burst Effect")]
    [InlineData("burst effect")]
    [InlineData("White Flash R")]
    [InlineData("White Flash")]
    [InlineData("white flash")]
    public void BurstAndFlashEffects_WithoutSilhouetteOrSlot_Disable(string name)
    {
        Assert.Equal(CloneNode.Disable, CloneFilter.Evaluate(name, isSilhouette: false, inSlot: false));
    }

    [Fact]
    public void SilhouetteNode_Keep()
    {
        Assert.Equal(CloneNode.Keep, CloneFilter.Evaluate("Crest Silhouette", isSilhouette: true, inSlot: false));
    }

    // The silhouette rule wins over the effect rule.
    [Fact]
    public void SubmitEffects_InsideSilhouette_Keep()
    {
        Assert.Equal(CloneNode.Keep, CloneFilter.Evaluate("Crest Submit Effects", isSilhouette: true, inSlot: false));
    }

    // The slot rule wins over the effect rule.
    [Theory]
    [InlineData("Defend Slot")]
    [InlineData("Crest Submit Effects")]
    public void NodesInsideSlot_Keep(string name)
    {
        Assert.Equal(CloneNode.Keep, CloneFilter.Evaluate(name, isSilhouette: false, inSlot: true));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmptyOrNullName_KeepWithoutException(string? name)
    {
        Assert.Equal(CloneNode.Keep, CloneFilter.Evaluate(name!, isSilhouette: false, inSlot: false));
    }
}
