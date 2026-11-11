using CrestPicker.Pure;
using Xunit;

namespace CrestPicker.Tests;

public class OwnWindowPolicyTests
{
    [Fact]
    public void IsNotReadyYet_TrueForMissingOrInit()
    {
        Assert.True(OwnWindowPolicy.IsNotReadyYet(null));
        Assert.True(OwnWindowPolicy.IsNotReadyYet(string.Empty));
        Assert.True(OwnWindowPolicy.IsNotReadyYet("Init"));
    }

    [Fact]
    public void IsNotReadyYet_FalseForEverySettledState()
    {
        // `Init Shop` is a real state, not a clone that has yet to tick: a prefix match on
        // `Init` would defer the open until the deadline instead of falling back. Regression
        // for 03fd419 (`fix: retry open while clone fsm is not ready`).
        Assert.False(OwnWindowPolicy.IsNotReadyYet("Idle"));
        Assert.False(OwnWindowPolicy.IsNotReadyYet("Open"));
        Assert.False(OwnWindowPolicy.IsNotReadyYet("Open Window"));
        Assert.False(OwnWindowPolicy.IsNotReadyYet("Init Shop"));
        Assert.False(OwnWindowPolicy.IsNotReadyYet("init"));
    }

    [Fact]
    public void RootName_IsExactAndDoesNotCollideWithVanillaWindows()
    {
        Assert.Equal("CrestPicker Shop Menu", OwnWindowPolicy.RootName);
        Assert.NotEqual("Shop Menu", OwnWindowPolicy.RootName);
        Assert.NotEqual("Shop Menu(Clone)", OwnWindowPolicy.RootName);
    }

    [Theory]
    [InlineData(7, 7, 42, 42, false)] // same scene, owner still alive
    [InlineData(7, 8, 42, 42, true)] // scene changed
    [InlineData(7, 8, 42, 43, true)] // scene and owner changed
    [InlineData(7, 7, 0, 42, true)] // no owner recorded at spawn time
    [InlineData(7, 7, 0, 0, true)] // no owner recorded, no owner now
    [InlineData(7, 7, 42, 0, true)] // owner died
    [InlineData(7, 7, 42, 43, true)] // another owner took its place
    [InlineData(0, 0, -1, -1, false)] // negative instance ids, same owner
    [InlineData(0, 0, -1, -2, true)] // negative instance ids, different owner
    public void ShouldRebuild_StaleOnSceneOrOwnerChange(
        int cachedScene, int scene, int cachedOwnerId, int ownerId, bool expected)
    {
        Assert.Equal(expected, OwnWindowPolicy.ShouldRebuild(cachedScene, scene, cachedOwnerId, ownerId));
    }

    [Fact]
    public void ShouldPause_FollowsVanillaWindow()
    {
        Assert.True(OwnWindowPolicy.ShouldPause(vanillaWindowActive: true));
        Assert.False(OwnWindowPolicy.ShouldPause(vanillaWindowActive: false));
    }
}
