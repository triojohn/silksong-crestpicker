using CrestPicker.Pure;
using Xunit;

namespace CrestPicker.Tests;

public class LabelVisibilityTests
{
    private const float Width = 1f;
    private const float Alpha = 1f;

    [Fact]
    public void AllConditionsMet_IsVisible()
    {
        Assert.Equal(
            LabelHiddenReason.Visible,
            LabelVisibility.Evaluate(rowActive: true, insideList: true, meshWidth: Width, fadeAlpha: Alpha));
    }

    [Fact]
    public void InactiveRow_HidesAsInactive()
    {
        // Inactive wins over every other failing condition: it is checked first.
        Assert.Equal(
            LabelHiddenReason.Inactive,
            LabelVisibility.Evaluate(rowActive: false, insideList: true, meshWidth: Width, fadeAlpha: Alpha));
        Assert.Equal(
            LabelHiddenReason.Inactive,
            LabelVisibility.Evaluate(rowActive: false, insideList: false, meshWidth: 0f, fadeAlpha: 0f));
    }

    [Fact]
    public void RowOutsideList_HidesAsOutside()
    {
        // Outside wins over an empty mesh and over alpha.
        Assert.Equal(
            LabelHiddenReason.Outside,
            LabelVisibility.Evaluate(rowActive: true, insideList: false, meshWidth: Width, fadeAlpha: Alpha));
        Assert.Equal(
            LabelHiddenReason.Outside,
            LabelVisibility.Evaluate(rowActive: true, insideList: false, meshWidth: 0f, fadeAlpha: 0f));
    }

    [Fact]
    public void MeshAtThreshold_IsEmpty_AboveThreshold_IsVisible()
    {
        // The threshold is inclusive: width at or below MinMeshWidth counts as empty.
        Assert.Equal(
            LabelHiddenReason.Empty,
            LabelVisibility.Evaluate(true, true, LabelVisibility.MinMeshWidth, Alpha));
        Assert.Equal(
            LabelHiddenReason.Visible,
            LabelVisibility.Evaluate(true, true, 0.00011f, Alpha));
        Assert.Equal(
            LabelHiddenReason.Empty,
            LabelVisibility.Evaluate(true, true, -1f, Alpha));
    }

    [Fact]
    public void AlphaAtThreshold_IsHidden_AboveThreshold_IsVisible()
    {
        // The threshold is exclusive for alpha: at MinAlpha the label is already hidden.
        Assert.Equal(
            LabelHiddenReason.Alpha,
            LabelVisibility.Evaluate(true, true, Width, LabelVisibility.MinAlpha));
        Assert.Equal(
            LabelHiddenReason.Visible,
            LabelVisibility.Evaluate(true, true, Width, 0.02f));
        Assert.Equal(
            LabelHiddenReason.Alpha,
            LabelVisibility.Evaluate(true, true, Width, 0f));
        Assert.Equal(
            LabelHiddenReason.Alpha,
            LabelVisibility.Evaluate(true, true, Width, -0.5f));
    }

    [Fact]
    public void NonFiniteValues_AreNotVisible()
    {
        Assert.NotEqual(
            LabelHiddenReason.Visible,
            LabelVisibility.Evaluate(true, true, float.NaN, Alpha));
        Assert.NotEqual(
            LabelHiddenReason.Visible,
            LabelVisibility.Evaluate(true, true, Width, float.NaN));
    }
}
