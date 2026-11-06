namespace CrestPicker.Pure;

/// <summary>Which crests the picker offers.</summary>
internal enum CrestPoolMode
{
    /// <summary>Every normal base crest, regardless of save state.</summary>
    All,

    /// <summary>Only crests already unlocked in the current save.</summary>
    Unlocked,
}

/// <summary>Selectable-crest rules, free of any game types so they can be unit tested.</summary>
internal static class CrestPoolRules
{
    /// <summary>
    /// Tells whether one crest may appear in the picker: base versions that are not
    /// hidden, and unlocked ones only in <see cref="CrestPoolMode.Unlocked"/>.
    /// </summary>
    /// <param name="isBase">Crest is the base version, not a tool variant.</param>
    /// <param name="isHidden">Crest is hidden from the vanilla crest list.</param>
    /// <param name="isUnlocked">Crest is already unlocked in this save.</param>
    /// <param name="mode">Configured pool source.</param>
    /// <returns>True when the crest may be offered.</returns>
    internal static bool IsSelectable(bool isBase, bool isHidden, bool isUnlocked, CrestPoolMode mode)
    {
        if (!isBase || isHidden)
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
}
