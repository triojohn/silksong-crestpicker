using CrestPicker.Pure;
using Xunit;

namespace CrestPicker.Tests;

public class RowPickTests
{
    private static readonly float[] Rows = { 3f, 2f, 1f, 0f, -1f };

    [Fact]
    public void PickRowIndex_ExactRowY_ReturnsThatRow()
    {
        Assert.Equal(2, RowPick.PickRowIndex(1f, Rows, 0.5f));
        Assert.Equal(0, RowPick.PickRowIndex(3f, Rows, 0.5f));
        Assert.Equal(4, RowPick.PickRowIndex(-1f, Rows, 0.5f));
    }

    [Fact]
    public void PickRowIndex_BetweenRows_ReturnsNearest()
    {
        Assert.Equal(1, RowPick.PickRowIndex(1.6f, Rows, 0.5f));
        Assert.Equal(3, RowPick.PickRowIndex(0.4f, Rows, 0.5f));
    }

    [Fact]
    public void PickRowIndex_AtTolerance_IsAccepted_OneStepFurtherIsNot()
    {
        Assert.Equal(0, RowPick.PickRowIndex(1.5f, new[] { 1f, 3f }, 0.5f));
        Assert.Equal(-1, RowPick.PickRowIndex(1.5f, new[] { 1f, 3f }, 0.499f));
        Assert.Equal(-1, RowPick.PickRowIndex(1.5f, new[] { 1f, 2f }, 0.49f));
    }

    [Fact]
    public void PickRowIndex_BeyondTolerance_ReturnsMinusOne()
    {
        Assert.Equal(-1, RowPick.PickRowIndex(10f, Rows, 0.5f));
        Assert.Equal(-1, RowPick.PickRowIndex(-5f, Rows, 0.5f));
        Assert.Equal(-1, RowPick.PickRowIndex(1.5f, Rows, -0.1f));
    }

    [Fact]
    public void PickRowIndex_EmptyOrNullRows_ReturnsMinusOne()
    {
        Assert.Equal(-1, RowPick.PickRowIndex(1f, new float[0], 0.5f));
        Assert.Equal(-1, RowPick.PickRowIndex(1f, null!, 0.5f));
    }

    [Fact]
    public void PickRowIndex_NaNAndInfinitySelector_ReturnsMinusOne()
    {
        Assert.Equal(-1, RowPick.PickRowIndex(float.NaN, Rows, float.PositiveInfinity));
        Assert.Equal(-1, RowPick.PickRowIndex(float.PositiveInfinity, Rows, float.PositiveInfinity));
        Assert.Equal(-1, RowPick.PickRowIndex(float.NegativeInfinity, Rows, float.PositiveInfinity));
    }

    [Fact]
    public void PickRowIndex_NaNAndInfinityRows_AreSkipped()
    {
        float[] rows = { float.NaN, 2f, float.PositiveInfinity, 1f, float.NegativeInfinity };

        Assert.Equal(3, RowPick.PickRowIndex(1.1f, rows, 0.5f));
        Assert.Equal(-1, RowPick.PickRowIndex(5f, rows, 0.5f));
        Assert.Equal(-1, RowPick.PickRowIndex(1f, new[] { float.NaN, float.PositiveInfinity }, 0.5f));
    }
}
