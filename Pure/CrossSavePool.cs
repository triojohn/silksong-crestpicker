using System.Collections.Generic;

namespace CrestPicker.Pure;

/// <summary>Why the cross-save pool came out empty or fell back to every normal crest.</summary>
internal enum CrestPoolEmpty
{
    /// <summary>The pool has crests; no fallback happened.</summary>
    None,

    /// <summary>Every normal crest is already unlocked in this save: nothing to offer.</summary>
    AllNormalsUnlockedHere,

    /// <summary>No normal crest is unlocked in any save slot.</summary>
    NothingAnywhere,

    /// <summary>Crests are unlocked elsewhere, but not ones missing here.</summary>
    NothingNewElsewhere,
}

/// <summary>Result of <see cref="CrossSavePool.Build"/>.</summary>
/// <param name="Crests">Crests to offer, in crest-list order.</param>
/// <param name="Empty">Why the fallback branch was taken, if it was.</param>
/// <param name="NormalTotal">Number of normal crests known to the game.</param>
/// <param name="UnlockedHere">Number of normal crests unlocked in this save.</param>
internal readonly record struct CrestPoolSelection(
    List<CrestFacts> Crests,
    CrestPoolEmpty Empty,
    int NormalTotal,
    int UnlockedHere);

/// <summary>
/// Builds the picker pool from crests unlocked in other save slots: normal crests
/// missing here but owned somewhere else. Free of game types so it can be tested.
/// </summary>
internal static class CrossSavePool
{
    /// <summary>
    /// Offers normal crests unlocked in any other slot and not unlocked here. When that
    /// leaves nothing, reports why and returns either no crests (everything already owned
    /// here) or every normal crest (fallback).
    /// </summary>
    /// <param name="all">Crests in crest-list order.</param>
    /// <param name="otherSlots">Unlocked crest names per other save slot.</param>
    /// <param name="includeEquipped">Keep the currently equipped crest in the pool.</param>
    /// <returns>Offer list plus the reason for any fallback.</returns>
    internal static CrestPoolSelection Build(
        IEnumerable<CrestFacts> all,
        IEnumerable<IEnumerable<string>> otherSlots,
        bool includeEquipped)
    {
        var elsewhere = new HashSet<string>();
        foreach (IEnumerable<string> slot in otherSlots)
        {
            if (slot == null)
            {
                continue;
            }

            foreach (string name in slot)
            {
                elsewhere.Add(name);
            }
        }

        var normals = new List<CrestFacts>();
        var offered = new List<CrestFacts>();
        int unlockedHere = 0;
        bool everyNormalUnlockedHere = true;

        foreach (CrestFacts crest in all)
        {
            if (!CrestPoolRules.IsNormal(crest.IsBase, crest.IsHidden))
            {
                continue;
            }

            normals.Add(crest);

            if (crest.IsUnlocked)
            {
                unlockedHere++;
                continue;
            }

            everyNormalUnlockedHere = false;

            if (!elsewhere.Contains(crest.Name))
            {
                continue;
            }

            if (!includeEquipped && CrestPoolRules.IsEquippedExcluded(crest.IsEquipped))
            {
                continue;
            }

            offered.Add(crest);
        }

        if (offered.Count > 0)
        {
            return new CrestPoolSelection(offered, CrestPoolEmpty.None, normals.Count, unlockedHere);
        }

        if (everyNormalUnlockedHere)
        {
            return new CrestPoolSelection(new List<CrestFacts>(), CrestPoolEmpty.AllNormalsUnlockedHere,
                normals.Count, unlockedHere);
        }

        CrestPoolEmpty empty = elsewhere.Count == 0
            ? CrestPoolEmpty.NothingAnywhere
            : CrestPoolEmpty.NothingNewElsewhere;

        return new CrestPoolSelection(FilterEquipped(normals, includeEquipped), empty,
            normals.Count, unlockedHere);
    }

    /// <summary>Drops the equipped crest from the fallback list unless it is asked for.</summary>
    /// <param name="normals">Normal crests in crest-list order.</param>
    /// <param name="includeEquipped">Keep the currently equipped crest.</param>
    /// <returns>Fallback list.</returns>
    private static List<CrestFacts> FilterEquipped(List<CrestFacts> normals, bool includeEquipped)
    {
        if (includeEquipped)
        {
            return new List<CrestFacts>(normals);
        }

        var fallback = new List<CrestFacts>(normals.Count);
        foreach (CrestFacts crest in normals)
        {
            if (!CrestPoolRules.IsEquippedExcluded(crest.IsEquipped))
            {
                fallback.Add(crest);
            }
        }

        return fallback;
    }
}
