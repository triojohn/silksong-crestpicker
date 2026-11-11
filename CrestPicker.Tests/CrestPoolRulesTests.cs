using CrestPicker.Pure;
using Xunit;

namespace CrestPicker.Tests;

public class CrestPoolRulesTests
{
    [Fact]
    public void AllMode_IncludesLockedBaseCrest()
    {
        Assert.True(CrestPoolRules.IsSelectable(
            isBase: true, isHidden: false, isUnlocked: false, CrestPoolMode.All));
    }

    [Fact]
    public void UnlockedMode_ExcludesLockedCrest()
    {
        Assert.False(CrestPoolRules.IsSelectable(
            isBase: true, isHidden: false, isUnlocked: false, CrestPoolMode.Unlocked));
    }

    [Fact]
    public void UnlockedMode_IncludesUnlockedCrest()
    {
        Assert.True(CrestPoolRules.IsSelectable(
            isBase: true, isHidden: false, isUnlocked: true, CrestPoolMode.Unlocked));
    }

    [Fact]
    public void HiddenCrest_IsNeverSelectable()
    {
        Assert.False(CrestPoolRules.IsSelectable(
            isBase: true, isHidden: true, isUnlocked: true, CrestPoolMode.All));
        Assert.False(CrestPoolRules.IsSelectable(
            isBase: true, isHidden: true, isUnlocked: true, CrestPoolMode.Unlocked));
    }

    [Fact]
    public void NonBaseCrest_IsNeverSelectable()
    {
        Assert.False(CrestPoolRules.IsSelectable(
            isBase: false, isHidden: false, isUnlocked: true, CrestPoolMode.All));
        Assert.False(CrestPoolRules.IsSelectable(
            isBase: false, isHidden: false, isUnlocked: true, CrestPoolMode.Unlocked));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void IsNormal_OnlyBaseVisibleCrests(bool isBase, bool isHidden, bool expected)
    {
        Assert.Equal(expected, CrestPoolRules.IsNormal(isBase, isHidden));
    }

    [Fact]
    public void EquippedCrest_IsExcluded()
    {
        Assert.True(CrestPoolRules.IsEquippedExcluded(true));
        Assert.False(CrestPoolRules.IsEquippedExcluded(false));
    }
}
