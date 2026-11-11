using System.Collections.Generic;

namespace CrestPicker.Pure;

/// <summary>One crest's flags, independent of any game types.</summary>
internal readonly record struct CrestFacts(
    string Name,
    bool IsBase,
    bool IsHidden,
    bool IsUnlocked,
    bool IsEquipped);

/// <summary>Which crests the picker offers.</summary>
internal enum CrestPoolMode
{
    /// <summary>Every normal base crest, regardless of save state.</summary>
    All,

    /// <summary>Only crests already unlocked in the current save.</summary>
    Unlocked,

    /// <summary>
    /// Union of crests unlocked in every save slot, minus the ones unlocked here.
    /// Built by <see cref="CrossSavePool.Build"/>, not by <see cref="CrestPoolRules.Select"/>.
    /// </summary>
    FromSaves,
}

/// <summary>Selectable-crest rules, free of any game types so they can be unit tested.</summary>
internal static class CrestPoolRules
{
    /// <summary>
    /// Tells whether one crest counts as normal: a base version that is not hidden.
    /// The same filter the game uses in ToolItemManager.GetUnlockedCrestsCount.
    /// </summary>
    /// <param name="isBase">Crest is the base version, not an upgrade.</param>
    /// <param name="isHidden">Crest is hidden from the vanilla crest list.</param>
    /// <returns>True when the crest is a normal crest.</returns>
    internal static bool IsNormal(bool isBase, bool isHidden)
    {
        return isBase && !isHidden;
    }

    /// <summary>
    /// Tells whether one crest may appear in the picker: normal crests, and unlocked
    /// ones only in <see cref="CrestPoolMode.Unlocked"/>.
    /// </summary>
    /// <param name="isBase">Crest is the base version, not a tool variant.</param>
    /// <param name="isHidden">Crest is hidden from the vanilla crest list.</param>
    /// <param name="isUnlocked">Crest is already unlocked in this save.</param>
    /// <param name="mode">Configured pool source.</param>
    /// <returns>True when the crest may be offered.</returns>
    internal static bool IsSelectable(bool isBase, bool isHidden, bool isUnlocked, CrestPoolMode mode)
    {
        if (!IsNormal(isBase, isHidden))
        {
            return false;
        }

        return mode == CrestPoolMode.All || isUnlocked;
    }

    /// <summary>The equipped crest is never offered again.</summary>
    /// <param name="equipped">Crest is currently equipped.</param>
    /// <returns>True when the crest must be filtered out.</returns>
    internal static bool IsEquippedExcluded(bool equipped)
    {
        return equipped;
    }

    /// <summary>
    /// Composes the selectable crest list: drops non-base and hidden crests, honours
    /// unlock state unless <paramref name="ignoreUnlock"/> is set, and drops the
    /// equipped crest only when <paramref name="includeEquipped"/> is false.
    /// </summary>
    /// <param name="all">Crests in crest-list order.</param>
    /// <param name="mode">Configured pool source; <paramref name="ignoreUnlock"/> is skipped for
    /// <see cref="CrestPoolMode.FromSaves"/>.</param>
    /// <param name="ignoreUnlock">Offer crests regardless of unlock state.</param>
    /// <param name="includeEquipped">Keep the currently equipped crest in the pool.</param>
    /// <returns>Selectable crests in input order.</returns>
    internal static List<CrestFacts> Select(
        IEnumerable<CrestFacts> all,
        CrestPoolMode mode,
        bool ignoreUnlock,
        bool includeEquipped)
    {
        CrestPoolMode effectiveMode = ignoreUnlock && mode != CrestPoolMode.FromSaves
            ? CrestPoolMode.All
            : mode;
        var selected = new List<CrestFacts>();
        foreach (CrestFacts crest in all)
        {
            if (!IsSelectable(crest.IsBase, crest.IsHidden, crest.IsUnlocked, effectiveMode))
            {
                continue;
            }

            if (!includeEquipped && IsEquippedExcluded(crest.IsEquipped))
            {
                continue;
            }

            selected.Add(crest);
        }

        return selected;
    }
}
