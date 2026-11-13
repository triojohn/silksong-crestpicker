namespace CrestPicker.Pure;

/// <summary>Why the chapel maid chain runs or stays off.</summary>
internal enum OfferGateReason
{
    /// <summary>There is something to offer; the chain may run.</summary>
    Ok,

    /// <summary>No normal crest was found in the save slots.</summary>
    NoSaveCrests,

    /// <summary>Every crest the saves hold is already unlocked in this playthrough.</summary>
    NothingNew,

    /// <summary>The first conversation with the maid has not happened yet.</summary>
    IntroNotDone,

    /// <summary>The offer was already used in this save slot.</summary>
    AlreadyOffered,
}

/// <summary>
/// Decision whether the chapel maid chain (wave and call) may run: it needs crests to
/// offer, which means more normal crests in the save slots than unlocked here. Free of
/// game and Unity types so it can be unit tested.
/// </summary>
internal static class OfferGate
{
    /// <summary>
    /// True when the chain may run: there are more crests in the saves than unlocked
    /// here, the first talk is over and the offer was not used in this slot.
    /// </summary>
    /// <param name="saveCrests">Normal crests found in the save slots.</param>
    /// <param name="unlockedHere">Normal crests unlocked in this playthrough.</param>
    /// <param name="introDone">The first conversation with the maid has happened.</param>
    /// <param name="alreadyOfferedInSlot">The offer was already used in this save slot.</param>
    internal static bool ShouldRunChain(int saveCrests, int unlockedHere, bool introDone,
        bool alreadyOfferedInSlot)
    {
        return Reason(saveCrests, unlockedHere, introDone, alreadyOfferedInSlot) == OfferGateReason.Ok;
    }

    /// <summary>
    /// <summary>
    /// Reason the chain runs or stays off, in the order the conditions are checked;
    /// <see cref="OfferGateReason.Ok"/> means the chain runs.
    /// </summary>
    /// <param name="saveCrests">Normal crests found in the save slots.</param>
    /// <param name="unlockedHere">Normal crests unlocked in this playthrough.</param>
    /// <param name="introDone">The first conversation with the maid has happened.</param>
    /// <param name="alreadyOfferedInSlot">The offer was already used in this save slot.</param>
    internal static OfferGateReason Reason(int saveCrests, int unlockedHere, bool introDone,
        bool alreadyOfferedInSlot)
    {
        if (saveCrests <= 0)
        {
            return OfferGateReason.NoSaveCrests;
        }

        if (saveCrests <= unlockedHere)
        {
            return OfferGateReason.NothingNew;
        }

        if (!introDone)
        {
            return OfferGateReason.IntroNotDone;
        }

        return alreadyOfferedInSlot ? OfferGateReason.AlreadyOffered : OfferGateReason.Ok;
    }
}
