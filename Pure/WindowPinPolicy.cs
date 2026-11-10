namespace CrestPicker.Pure;

/// <summary>
/// Which alpha the shared shop window root must hold, per FSM state.
/// Vanilla drives the root <c>AlphaSelf</c>; we only write it in settled states,
/// so its own fades stay untouched.
/// Free of Unity types so it can be unit tested.
/// </summary>
public static class WindowPinPolicy
{
    /// <summary>
    /// Target root alpha: <c>1</c> in the settled <c>Open</c> state, <c>0</c> in
    /// <c>Idle</c>, <c>null</c> anywhere else. Every transitional state
    /// (<c>Open Window</c>, <c>Item List Up</c>, <c>Down</c>, <c>Close*</c>,
    /// <c>Special Close</c>) returns <c>null</c> and is left alone.
    /// </summary>
    public static float? RootTarget(string? fsmState)
    {
        if (fsmState == "Open")
        {
            return 1f;
        }

        if (fsmState == "Idle")
        {
            return 0f;
        }

        return null;
    }
}
