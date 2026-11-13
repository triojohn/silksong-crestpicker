namespace CrestPicker.Pure;

/// <summary>
/// State of the chapel maid's talk before the crest offer, free of game and Unity types.
/// The game may report the end of a dialogue twice (the end and cancelled callbacks, or a
/// forced close), so the flow hands out the "finished" signal only once.
/// </summary>
internal sealed class OfferTalkFlow
{
    /// <summary>True while the lines are on screen.</summary>
    internal bool Talking { get; private set; }

    /// <summary>
    /// True once the talk ended or was aborted; blocks <see cref="Begin"/> until
    /// <see cref="Rearm"/>.
    /// </summary>
    internal bool Finished { get; private set; }

    /// <summary>
    /// Starts the talk. True only when idle: neither talking nor finished. A call while
    /// talking or after the end returns false.
    /// </summary>
    internal bool Begin()
    {
        if (Talking || Finished)
        {
            return false;
        }

        Talking = true;
        return true;
    }

    /// <summary>
    /// Ends the talk. True only on the first call after <see cref="Begin"/>; a repeated call
    /// from a second dialogue callback returns false, so the offer is requested once.
    /// A call without a started talk returns false.
    /// </summary>
    internal bool End()
    {
        if (!Talking)
        {
            return false;
        }

        Talking = false;
        Finished = true;
        return true;
    }

    /// <summary>
    /// Drops the talk without the "finished" signal: the dialogue window is gone and its
    /// callbacks will not come. Blocks <see cref="Begin"/> until <see cref="Rearm"/>.
    /// </summary>
    internal void Abort()
    {
        Talking = false;
        Finished = true;
    }

    /// <summary>
    /// True when the offer must be skipped because the crest offer was already made and the
    /// ignore-offered override is off. With the override on the talk and the window run
    /// anyway, so the lines and the picker can be tested on a slot whose flag is already set.
    /// Shared by the greeting and the offer request so the rule is one.
    /// </summary>
    internal static bool ShouldSkipOffer(bool alreadyOffered, bool ignoreAlreadyOffered)
    {
        return alreadyOffered && !ignoreAlreadyOffered;
    }

    /// <summary>Returns to the initial state so the lines may run on the next approach.</summary>
    internal void Rearm()
    {
        Talking = false;
        Finished = false;
    }
}
