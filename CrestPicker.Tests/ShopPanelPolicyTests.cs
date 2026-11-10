using System.Linq;
using CrestPicker.Pure;
using Xunit;

namespace CrestPicker.Tests;

public class ShopPanelPolicyTests
{
    [Fact]
    public void PanelsHiddenOnClose_ListsFiveVanillaNodesInOrder()
    {
        Assert.Equal(
            new[]
            {
                "Item List Group",
                "Item Details",
                "Item Confirm Group",
                "Action Cancel",
                "Action Confirm",
            },
            ShopPanelPolicy.PanelsHiddenOnClose);
    }

    [Fact]
    public void PanelsHiddenOnClose_HasNoDuplicatesOrBlanks()
    {
        string[] panels = ShopPanelPolicy.PanelsHiddenOnClose;

        Assert.Equal(panels.Length, panels.Distinct().Count());
        Assert.All(panels, name => Assert.False(string.IsNullOrWhiteSpace(name)));
    }

    [Fact]
    public void ShouldHidePanels_HidesOnlyOnRealClose()
    {
        Assert.True(ShopPanelPolicy.ShouldHidePanels(opening: false, alreadyHidden: false));
        Assert.False(ShopPanelPolicy.ShouldHidePanels(opening: false, alreadyHidden: true));
        Assert.False(ShopPanelPolicy.ShouldHidePanels(opening: true, alreadyHidden: false));
        Assert.False(ShopPanelPolicy.ShouldHidePanels(opening: true, alreadyHidden: true));
    }

    [Fact]
    public void ShouldWriteRootAlpha_IsFalseForEveryPhase()
    {
        int[] phases = { int.MinValue, -1, 0, 1, 2, 3, 5, int.MaxValue };

        Assert.All(phases, phase => Assert.False(ShopPanelPolicy.ShouldWriteRootAlpha(phase)));
    }
}
