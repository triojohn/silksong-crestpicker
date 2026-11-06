using CrestPicker.Pure;
using Xunit;

namespace CrestPicker.Tests;

public class GeometryTests
{
    [Fact]
    public void IsSane_RejectsNaNInfinityAndHugeValues()
    {
        Assert.False(Geometry.IsSane(float.NaN));
        Assert.False(Geometry.IsSane(float.PositiveInfinity));
        Assert.False(Geometry.IsSane(float.NegativeInfinity));
        Assert.False(Geometry.IsSane(1000f));
        Assert.False(Geometry.IsSane(-1000f));
    }

    [Fact]
    public void IsSane_AcceptsOrdinaryValues()
    {
        Assert.True(Geometry.IsSane(0f));
        Assert.True(Geometry.IsSane(-12.5f));
        Assert.True(Geometry.IsSane(999f));
    }

    [Fact]
    public void ClampAxis_KeepsSegmentInsideBounds()
    {
        Assert.Equal(9f, Geometry.ClampAxis(centre: 12f, half: 1f, min: 0f, max: 10f));
        Assert.Equal(1f, Geometry.ClampAxis(centre: -5f, half: 1f, min: 0f, max: 10f));
        Assert.Equal(5f, Geometry.ClampAxis(centre: 5f, half: 1f, min: 0f, max: 10f));
    }

    [Fact]
    public void ClampAxis_SegmentWiderThanSpan_IsCentred()
    {
        Assert.Equal(5f, Geometry.ClampAxis(centre: 0f, half: 8f, min: 0f, max: 10f));
    }

    [Fact]
    public void ClampAxis_InvertedBounds_ReturnMin()
    {
        Assert.Equal(3f, Geometry.ClampAxis(centre: 5f, half: 1f, min: 3f, max: 1f));
    }
}
