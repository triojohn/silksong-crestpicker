using System;
using System.Collections.Generic;
using System.Linq;
using CrestPicker.Pure;
using Xunit;

namespace CrestPicker.Tests;

/// <summary>
/// Composition tests for <see cref="CrestPoolRules.Select"/>: the picker must show the
/// whole crest list (all base, non-hidden crests) when unlock state is ignored, keep the
/// equipped crest when asked, and never leak hidden or non-base entries.
/// </summary>
public class CrestPoolSelectTests
{
    /// <summary>
    /// Crests in crest-list order, shaped like the game: 7 base visible crests (one of
    /// them equipped, two still locked), 3 non-base upgrade variants and 2 hidden crests.
    /// </summary>
    private static readonly CrestFacts[] GameCrests =
    {
        new("Hunter", IsBase: true, IsHidden: false, IsUnlocked: true, IsEquipped: true),
        new("Reaper", IsBase: true, IsHidden: false, IsUnlocked: true, IsEquipped: false),
        new("Upgrade1", IsBase: false, IsHidden: false, IsUnlocked: true, IsEquipped: false),
        new("Wanderer", IsBase: true, IsHidden: false, IsUnlocked: true, IsEquipped: false),
        new("Hidden1", IsBase: true, IsHidden: true, IsUnlocked: true, IsEquipped: false),
        new("Beast", IsBase: true, IsHidden: false, IsUnlocked: true, IsEquipped: false),
        new("Upgrade2", IsBase: false, IsHidden: false, IsUnlocked: true, IsEquipped: false),
        new("Architect", IsBase: true, IsHidden: false, IsUnlocked: false, IsEquipped: false),
        new("Hidden2", IsBase: true, IsHidden: true, IsUnlocked: false, IsEquipped: false),
        new("Witch", IsBase: true, IsHidden: false, IsUnlocked: true, IsEquipped: false),
        new("Upgrade3", IsBase: false, IsHidden: false, IsUnlocked: false, IsEquipped: false),
        new("Shaman", IsBase: true, IsHidden: false, IsUnlocked: false, IsEquipped: false),
    };

    /// <summary>Names of the 7 base visible crests, in crest-list order.</summary>
    private static readonly string[] VisibleBaseNames =
    {
        "Hunter", "Reaper", "Wanderer", "Beast", "Architect", "Witch", "Shaman",
    };

    private static string[] Names(List<CrestFacts> crests)
    {
        return crests.Select(crest => crest.Name).ToArray();
    }

    [Fact]
    public void IgnoreUnlock_IncludeEquipped_KeepsWholeBaseListInOrder()
    {
        // Mode is Unlocked on purpose: ignoreUnlock must override it, otherwise the two
        // locked base crests (Architect, Shaman) would be dropped and the count would be 5.
        var selected = CrestPoolRules.Select(
            GameCrests, CrestPoolMode.Unlocked, ignoreUnlock: true, includeEquipped: true);

        Assert.Equal(7, selected.Count);
        Assert.Equal(VisibleBaseNames, Names(selected));
    }

    [Fact]
    public void IgnoreUnlock_ExcludeEquipped_DropsOnlyEquippedCrest()
    {
        var selected = CrestPoolRules.Select(
            GameCrests, CrestPoolMode.Unlocked, ignoreUnlock: true, includeEquipped: false);

        Assert.Equal(6, selected.Count);
        Assert.Equal(
            new[] { "Reaper", "Wanderer", "Beast", "Architect", "Witch", "Shaman" },
            Names(selected));
    }

    [Fact]
    public void UnlockRespected_KeepsOnlyUnlockedBaseVisibleCrests()
    {
        var selected = CrestPoolRules.Select(
            GameCrests, CrestPoolMode.Unlocked, ignoreUnlock: false, includeEquipped: true);

        Assert.Equal(5, selected.Count);
        Assert.Equal(
            new[] { "Hunter", "Reaper", "Wanderer", "Beast", "Witch" },
            Names(selected));
    }

    [Fact]
    public void HiddenAndNonBaseCrests_NeverAppearUnderAnyFlags()
    {
        foreach (CrestPoolMode mode in new[] { CrestPoolMode.All, CrestPoolMode.Unlocked })
        {
            foreach (bool ignoreUnlock in new[] { false, true })
            {
                foreach (bool includeEquipped in new[] { false, true })
                {
                    var selected = Names(CrestPoolRules.Select(
                        GameCrests, mode, ignoreUnlock, includeEquipped));
                    string context =
                        $"mode={mode} ignoreUnlock={ignoreUnlock} includeEquipped={includeEquipped}";

                    Assert.True(
                        selected.All(name => VisibleBaseNames.Contains(name)),
                        $"unexpected crest in {context}: {string.Join(",", selected)}");
                    Assert.DoesNotContain("Hidden1", selected);
                    Assert.DoesNotContain("Hidden2", selected);
                    Assert.DoesNotContain("Upgrade1", selected);
                    Assert.DoesNotContain("Upgrade2", selected);
                    Assert.DoesNotContain("Upgrade3", selected);
                }
            }
        }
    }

    [Fact]
    public void EmptyInput_ReturnsEmptyList()
    {
        var selected = CrestPoolRules.Select(
            Array.Empty<CrestFacts>(), CrestPoolMode.All, ignoreUnlock: true, includeEquipped: true);

        Assert.Empty(selected);
    }

    [Fact]
    public void DuplicateNames_AreNotCollapsed()
    {
        var duplicates = new[]
        {
            new CrestFacts("Hunter", IsBase: true, IsHidden: false, IsUnlocked: true, IsEquipped: false),
            new CrestFacts("Reaper", IsBase: true, IsHidden: false, IsUnlocked: true, IsEquipped: false),
            new CrestFacts("Hunter", IsBase: true, IsHidden: false, IsUnlocked: true, IsEquipped: false),
        };

        var selected = CrestPoolRules.Select(
            duplicates, CrestPoolMode.All, ignoreUnlock: true, includeEquipped: true);

        Assert.Equal(3, selected.Count);
        Assert.Equal(new[] { "Hunter", "Reaper", "Hunter" }, Names(selected));
    }
}
