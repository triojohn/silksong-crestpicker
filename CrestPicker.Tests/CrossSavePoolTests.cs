using System;
using System.Collections.Generic;
using System.Linq;
using CrestPicker.Pure;
using Xunit;

namespace CrestPicker.Tests;

/// <summary>
/// Tests for <see cref="CrossSavePool.Build"/>: the offer is the normal crests unlocked in
/// some other save slot and not unlocked here, in crest-list order; when that leaves nothing
/// the result names the reason and falls back to no crests or to every normal crest.
/// </summary>
public class CrossSavePoolTests
{
    /// <summary>
    /// Crests in crest-list order, shaped like the game: 7 base visible crests (Hunter and
    /// Reaper unlocked here, Hunter equipped), 2 non-base upgrades and 2 hidden crests.
    /// </summary>
    private static readonly CrestFacts[] GameCrests =
    {
        new("Hunter", IsBase: true, IsHidden: false, IsUnlocked: true, IsEquipped: true),
        new("Reaper", IsBase: true, IsHidden: false, IsUnlocked: true, IsEquipped: false),
        new("Upgrade1", IsBase: false, IsHidden: false, IsUnlocked: true, IsEquipped: false),
        new("Wanderer", IsBase: true, IsHidden: false, IsUnlocked: false, IsEquipped: false),
        new("Hidden1", IsBase: true, IsHidden: true, IsUnlocked: true, IsEquipped: false),
        new("Beast", IsBase: true, IsHidden: false, IsUnlocked: false, IsEquipped: false),
        new("Upgrade2", IsBase: false, IsHidden: false, IsUnlocked: false, IsEquipped: false),
        new("Architect", IsBase: true, IsHidden: false, IsUnlocked: false, IsEquipped: false),
        new("Hidden2", IsBase: true, IsHidden: true, IsUnlocked: false, IsEquipped: false),
        new("Witch", IsBase: true, IsHidden: false, IsUnlocked: false, IsEquipped: false),
        new("Shaman", IsBase: true, IsHidden: false, IsUnlocked: false, IsEquipped: false),
    };

    /// <summary>Names of the 7 normal crests in <see cref="GameCrests"/>, in crest-list order.</summary>
    private static readonly string[] NormalNames =
    {
        "Hunter", "Reaper", "Wanderer", "Beast", "Architect", "Witch", "Shaman",
    };

    private static string[] Names(IEnumerable<CrestFacts> crests)
    {
        return crests.Select(crest => crest.Name).ToArray();
    }

    private static CrestFacts[] WithUnlock(CrestFacts[] crests, bool unlocked)
    {
        return crests.Select(crest => crest with { IsUnlocked = unlocked }).ToArray();
    }

    [Fact]
    public void Build_SubtractsUnlockedHere()
    {
        // Hunter and Reaper are in the union but already unlocked in this save; only the two
        // locked normal crests owned elsewhere may be offered.
        var selection = CrossSavePool.Build(
            GameCrests,
            new[] { new[] { "Hunter", "Reaper", "Wanderer", "Beast" } },
            includeEquipped: true);

        Assert.Equal(CrestPoolEmpty.None, selection.Empty);
        Assert.Equal(new[] { "Wanderer", "Beast" }, Names(selection.Crests));
        Assert.Equal(7, selection.NormalTotal);
        Assert.Equal(2, selection.UnlockedHere);
    }

    [Fact]
    public void Build_KeepsOnlyNormal()
    {
        // Upgrade1 (non-base) and Hidden1 (hidden) are unlocked in another slot and locked
        // here, so they would be offered if the normal-crest filter were missing.
        var all = new[]
        {
            new CrestFacts("Hunter", IsBase: true, IsHidden: false, IsUnlocked: true, IsEquipped: false),
            new CrestFacts("Upgrade1", IsBase: false, IsHidden: false, IsUnlocked: false, IsEquipped: false),
            new CrestFacts("Hidden1", IsBase: true, IsHidden: true, IsUnlocked: false, IsEquipped: false),
            new CrestFacts("Wanderer", IsBase: true, IsHidden: false, IsUnlocked: false, IsEquipped: false),
        };

        var selection = CrossSavePool.Build(
            all, new[] { new[] { "Upgrade1", "Hidden1" } }, includeEquipped: true);

        // Nothing normal is new: the fallback lists every normal crest and nothing else.
        Assert.Equal(CrestPoolEmpty.NothingNewElsewhere, selection.Empty);
        Assert.Equal(new[] { "Hunter", "Wanderer" }, Names(selection.Crests));
        Assert.Equal(2, selection.NormalTotal);
    }

    [Fact]
    public void Build_PreservesListOrder()
    {
        // Slot order is Beast then Wanderer, the crest list is the other way round: the result
        // must follow the crest list.
        var selection = CrossSavePool.Build(
            GameCrests,
            new[] { new[] { "Beast" }, new[] { "Wanderer" } },
            includeEquipped: true);

        Assert.Equal(new[] { "Wanderer", "Beast" }, Names(selection.Crests));
    }

    [Fact]
    public void Union_DedupesAcrossSlots()
    {
        // Wanderer is unlocked in three places (twice in one slot): one candidate, not three.
        var selection = CrossSavePool.Build(
            GameCrests,
            new[] { new[] { "Wanderer", "Wanderer" }, new[] { "Wanderer" } },
            includeEquipped: true);

        Assert.Equal(CrestPoolEmpty.None, selection.Empty);
        Assert.Equal(new[] { "Wanderer" }, Names(selection.Crests));
    }

    [Fact]
    public void Build_AllNormalsUnlockedHere_ReturnsAllNormalsUnlockedHere()
    {
        var selection = CrossSavePool.Build(
            WithUnlock(GameCrests, unlocked: true),
            new[] { new[] { "Upgrade1", "Hidden1", "Wanderer" } },
            includeEquipped: true);

        Assert.Equal(CrestPoolEmpty.AllNormalsUnlockedHere, selection.Empty);
        Assert.Empty(selection.Crests);
        Assert.Equal(7, selection.NormalTotal);
        Assert.Equal(7, selection.UnlockedHere);
    }

    [Fact]
    public void Build_NothingAnywhere_ReturnsAllNormalWithReason()
    {
        // No normal crest is unlocked in this save and no other slot has anything.
        var selection = CrossSavePool.Build(
            WithUnlock(GameCrests, unlocked: false),
            Array.Empty<string[]>(),
            includeEquipped: true);

        Assert.Equal(CrestPoolEmpty.NothingAnywhere, selection.Empty);
        Assert.Equal(NormalNames, Names(selection.Crests));
        Assert.Equal(0, selection.UnlockedHere);
    }

    [Fact]
    public void Build_NothingNewElsewhere_ReturnsAllNormalWithReason()
    {
        // Other slots only hold crests already unlocked here: nothing new, but not everything
        // here is unlocked either.
        var selection = CrossSavePool.Build(
            GameCrests,
            new[] { new[] { "Hunter", "Reaper", "Hidden1" } },
            includeEquipped: true);

        Assert.Equal(CrestPoolEmpty.NothingNewElsewhere, selection.Empty);
        Assert.Equal(NormalNames, Names(selection.Crests));
        Assert.Equal(7, selection.NormalTotal);
        Assert.Equal(2, selection.UnlockedHere);
    }

    [Fact]
    public void Build_ExcludesEquipped_OnlyWhenAsked()
    {
        var all = new[]
        {
            new CrestFacts("Hunter", IsBase: true, IsHidden: false, IsUnlocked: true, IsEquipped: false),
            new CrestFacts("Wanderer", IsBase: true, IsHidden: false, IsUnlocked: false, IsEquipped: false),
            new CrestFacts("Beast", IsBase: true, IsHidden: false, IsUnlocked: false, IsEquipped: true),
        };
        var slots = new[] { new[] { "Wanderer", "Beast" } };

        var withoutEquipped = CrossSavePool.Build(all, slots, includeEquipped: false);
        var withEquipped = CrossSavePool.Build(all, slots, includeEquipped: true);

        Assert.Equal(new[] { "Wanderer" }, Names(withoutEquipped.Crests));
        Assert.Equal(new[] { "Wanderer", "Beast" }, Names(withEquipped.Crests));

        // The fallback list drops the equipped crest by the same rule.
        var fallback = CrossSavePool.Build(all, Array.Empty<string[]>(), includeEquipped: false);

        Assert.Equal(CrestPoolEmpty.NothingAnywhere, fallback.Empty);
        Assert.Equal(new[] { "Hunter", "Wanderer" }, Names(fallback.Crests));
    }

    [Fact]
    public void Build_MissingSlot_IsJustEmpty()
    {
        // A null slot and an empty slot must not throw and must not change the result.
        string[] notReadableSlot = null!;
        var slots = new[] { notReadableSlot, Array.Empty<string>(), new[] { "Beast" } };

        var withEmptySlots = CrossSavePool.Build(GameCrests, slots, includeEquipped: true);
        var onlyBeast = CrossSavePool.Build(GameCrests, new[] { new[] { "Beast" } }, includeEquipped: true);

        Assert.Equal(onlyBeast.Empty, withEmptySlots.Empty);
        Assert.Equal(Names(onlyBeast.Crests), Names(withEmptySlots.Crests));
        Assert.Equal(new[] { "Beast" }, Names(withEmptySlots.Crests));
    }

    [Fact]
    public void Build_UnlockedHere_ButNotInAnySave_NotInPool()
    {
        // Hunter is unlocked here and absent from every other slot: it stays out of the pool
        // even though it is normal and not equipped.
        var selection = CrossSavePool.Build(
            new[]
            {
                new CrestFacts("Hunter", IsBase: true, IsHidden: false, IsUnlocked: true, IsEquipped: false),
                new CrestFacts("Reaper", IsBase: true, IsHidden: false, IsUnlocked: true, IsEquipped: false),
                new CrestFacts("Beast", IsBase: true, IsHidden: false, IsUnlocked: false, IsEquipped: false),
            },
            new[] { new[] { "Beast" } },
            includeEquipped: true);

        Assert.Equal(CrestPoolEmpty.None, selection.Empty);
        Assert.Equal(new[] { "Beast" }, Names(selection.Crests));
    }

    [Fact]
    public void Build_EmptyInput_IsEmptyWithAllNormalsUnlockedHere()
    {
        // No crests known at all: "every normal crest is unlocked here" holds vacuously, and
        // either fallback branch returns an empty offer list, so the plugin shows no menu.
        var selection = CrossSavePool.Build(
            Array.Empty<CrestFacts>(), Array.Empty<string[]>(), includeEquipped: true);

        Assert.Equal(CrestPoolEmpty.AllNormalsUnlockedHere, selection.Empty);
        Assert.Empty(selection.Crests);
        Assert.Equal(0, selection.NormalTotal);
        Assert.Equal(0, selection.UnlockedHere);
    }
}
