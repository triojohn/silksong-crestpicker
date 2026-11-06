namespace CrestPicker.Pure;

/// <summary>What the post-purchase watcher should do on this tick.</summary>
internal enum CloseDecision
{
    /// <summary>Nothing to do yet.</summary>
    None,

    /// <summary>Send the close event to the window FSM.</summary>
    CloseWindow,

    /// <summary>Put the window FSM back to idle and restore the vanilla shop.</summary>
    ForceIdle,
}

/// <summary>Post-purchase close timing, free of Unity types so it can be unit tested.</summary>
internal static class PurchaseClose
{
    /// <summary>Grace after the vanilla purchase message before the close event.</summary>
    internal const float MessageGrace = 0.5f;

    /// <summary>Fallback deadline when the purchase message never shows.</summary>
    internal const float FallbackDeadline = 3f;

    /// <summary>Deadline that forces the FSM back to idle after the close event.</summary>
    internal const float ForceIdleDeadline = 5f;

    /// <summary>
    /// Decides what the watcher does: close 0.5 s after the purchase message,
    /// 3 s when the message never appears, then force idle 5 s after the close event.
    /// </summary>
    /// <param name="elapsed">Seconds since the purchase completed.</param>
    /// <param name="messageSeen">The vanilla purchase message has been shown.</param>
    /// <param name="messageShownAt">Elapsed seconds when the message appeared.</param>
    /// <param name="emergencySent">The close event was already sent by this watcher.</param>
    /// <returns>Action for this tick.</returns>
    internal static CloseDecision Decide(
        float elapsed, bool messageSeen, float messageShownAt, bool emergencySent)
    {
        if (!emergencySent)
        {
            float dueAt = messageSeen ? messageShownAt + MessageGrace : FallbackDeadline;
            return elapsed >= dueAt ? CloseDecision.CloseWindow : CloseDecision.None;
        }

        return elapsed >= ForceIdleDeadline ? CloseDecision.ForceIdle : CloseDecision.None;
    }
}
