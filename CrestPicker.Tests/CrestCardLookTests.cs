using CrestPicker.Pure;
using Xunit;

namespace CrestPicker.Tests;

public class CrestCardLookTests
{
    /// <summary>Float tolerance for the scale comparisons.</summary>
    private const float Tolerance = 1e-6f;

    /// <summary>Vanilla <c>InventoryToolCrest._slotScaleOther</c>, decompiled <c>InventoryToolCrest.cs:15</c>.</summary>
    private const float VanillaSlotScaleOther = 0.7f;

    /// <summary>Vanilla locked-slot multiplier of the slot widget, mirrored by <c>ShopCrestSchema.LockedSlotScale</c>.</summary>
    private const float VanillaLockedSlotScale = 0.8f;

    [Fact]
    public void SlotScale_CurrentCardIsFullSize_OtherCardIsSmaller()
    {
        Assert.Equal(1f, CrestCardLook.SlotScale(true), Tolerance);
        Assert.Equal(0.7f, CrestCardLook.SlotScale(false), Tolerance);
    }

    [Fact]
    public void UseSilhouette_OnlyForUnselectedCard()
    {
        Assert.True(CrestCardLook.UseSilhouette(false));
        Assert.False(CrestCardLook.UseSilhouette(true));
    }

    [Fact]
    public void SlotScale_OtherCard_MatchesVanillaSlotScaleOther()
    {
        Assert.Equal(VanillaSlotScaleOther, CrestCardLook.SlotScale(false), Tolerance);
    }

    [Fact]
    public void CardRuleAndLockedRule_AreSeparateMultipliers()
    {
        // ShopCrestSchema.AddDots: world = SlotScale(false) * DotSize * reference,
        // then a locked slot is world * LockedSlotScale. The two rules multiply,
        // neither replaces the other.
        float lockedOnOtherCard = CrestCardLook.SlotScale(false) * VanillaLockedSlotScale;
        float lockedOnCurrentCard = CrestCardLook.SlotScale(true) * VanillaLockedSlotScale;
        Assert.Equal(0.56f, lockedOnOtherCard, Tolerance);
        Assert.Equal(0.8f, lockedOnCurrentCard, Tolerance);
    }
}
