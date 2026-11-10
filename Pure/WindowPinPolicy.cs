namespace CrestPicker.Pure;

/// <summary>
/// When the watcher has to write the fade alphas the vanilla shop leaves behind: the
/// close timer drops the root <c>AlphaSelf</c> to zero and the open chain never raises it,
/// and the open chain only raises <c>Window</c>, not the panels.
/// Free of Unity types so it can be unit tested.
/// </summary>
public static class WindowPinPolicy
{
    /// <summary>FSM state the window rests in while closed.</summary>
    private const string IdleState = "Idle";

    /// <summary>
    /// True while the window is live: any real state but the closed rest. A missing state
    /// (no FSM, empty name) is not live.
    /// </summary>
    /// <param name="fsmState">Active state name of <c>shop_control</c>.</param>
    public static bool ShouldPinRoot(string? fsmState)
    {
        return !string.IsNullOrEmpty(fsmState) && fsmState != IdleState;
    }

    /// <summary>
    /// True while the panels have to be raised: the window is live and its panels were not
    /// restored yet in this session. The session flag is passed in, not held here.
    /// </summary>
    /// <param name="fsmState">Active state name of <c>shop_control</c>.</param>
    /// <param name="shown">The window was already shown in this session.</param>
    public static bool ShouldRestorePanels(string? fsmState, bool shown)
    {
        return !string.IsNullOrEmpty(fsmState) && fsmState != IdleState && !shown;
    }
}
