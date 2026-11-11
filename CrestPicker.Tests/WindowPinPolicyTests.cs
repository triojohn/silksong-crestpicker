using CrestPicker.Pure;
using Xunit;

namespace CrestPicker.Tests;

public class WindowPinPolicyTests
{
    [Theory]
    [InlineData("Open", 1f)]
    [InlineData("Idle", 0f)]
    public void RootTarget_PinsSettledStates(string state, float expected)
    {
        Assert.Equal(expected, WindowPinPolicy.RootTarget(state));
    }

    [Theory]
    [InlineData("Open Window")]
    [InlineData("Item List Up")]
    [InlineData("Init")]
    [InlineData("Down")]
    [InlineData("Close X")]
    [InlineData("Special Close")]
    [InlineData("")]
    [InlineData(null)]
    public void RootTarget_LeavesTransitionalStatesAlone(string? state)
    {
        Assert.Null(WindowPinPolicy.RootTarget(state));
    }

    [Fact]
    public void StepTowards_MovesUpAndDownOneFullTravelPerDuration()
    {
        Assert.Equal(0.5f, WindowPinPolicy.StepTowards(current: 0f, target: 1f, deltaTime: 0.1f), 5);
        Assert.Equal(0.5f, WindowPinPolicy.StepTowards(current: 1f, target: 0f, deltaTime: 0.1f), 5);
    }

    [Fact]
    public void StepTowards_DefaultDurationMatchesExplicitPointTwo()
    {
        Assert.Equal(
            WindowPinPolicy.StepTowards(current: 0f, target: 1f, deltaTime: 0.1f, duration: 0.2f),
            WindowPinPolicy.StepTowards(current: 0f, target: 1f, deltaTime: 0.1f));
    }

    [Fact]
    public void StepTowards_NeverOvershootsAndLandsExactlyOnTarget()
    {
        Assert.Equal(1f, WindowPinPolicy.StepTowards(current: 0.9f, target: 1f, deltaTime: 1f));
        Assert.Equal(0f, WindowPinPolicy.StepTowards(current: 0.1f, target: 0f, deltaTime: 1f));
        Assert.Equal(1f, WindowPinPolicy.StepTowards(current: 0.75f, target: 1f, deltaTime: 0.1f));
        Assert.Equal(0f, WindowPinPolicy.StepTowards(current: 0.25f, target: 0f, deltaTime: 0.1f));
    }

    [Fact]
    public void StepTowards_NonPositiveDurationSnapsToTarget()
    {
        Assert.Equal(1f, WindowPinPolicy.StepTowards(current: 0f, target: 1f, deltaTime: 0.1f, duration: 0f));
        Assert.Equal(0f, WindowPinPolicy.StepTowards(current: 1f, target: 0f, deltaTime: 0.1f, duration: -0.2f));
    }

    [Fact]
    public void StepTowards_SameValueStaysPut()
    {
        Assert.Equal(0.5f, WindowPinPolicy.StepTowards(current: 0.5f, target: 0.5f, deltaTime: 0.1f));
        Assert.Equal(0f, WindowPinPolicy.StepTowards(current: 0f, target: 0f, deltaTime: 0.1f));
    }

    [Fact]
    public void StepTowards_WorksWithNegativeAlphas()
    {
        Assert.Equal(-0.5f, WindowPinPolicy.StepTowards(current: 0f, target: -1f, deltaTime: 0.1f), 5);
        Assert.Equal(-0.5f, WindowPinPolicy.StepTowards(current: -1f, target: 0f, deltaTime: 0.1f), 5);
        Assert.Equal(-1f, WindowPinPolicy.StepTowards(current: -0.9f, target: -1f, deltaTime: 1f));
    }
}
