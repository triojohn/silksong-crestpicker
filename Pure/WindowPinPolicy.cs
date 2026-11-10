namespace CrestPicker.Pure;

/// <summary>
/// When the watcher has to write the fade alphas the vanilla shop leaves behind: the
/// close timer drops the root <c>AlphaSelf</c> to zero and the open chain never raises it,
/// and the open chain only raises <c>Window</c>, not the panels.
/// The open flag comes from the <c>shop_control</c> FSM state, not the window alpha: the
/// fade alpha of a closed window stays at one, so a threshold on it is true all the time.
/// Free of Unity types so it can be unit tested.
/// </summary>
public static class WindowPinPolicy
{
    /// <summary>
    /// States of <c>shop_control</c> where the window is on screen. Anything else
    /// (<c>Idle</c>, <c>Down</c>, <c>Close*</c>, no FSM at all) means hands off.
    /// </summary>
    public static readonly string[] OpenStates = { "Open Window", "Item List Up", "Open" };

    /// <summary>True when the state is one of <see cref="OpenStates"/>, matched exactly.</summary>
    /// <param name="fsmState">Active state name of <c>shop_control</c>.</param>
    public static bool IsOpenState(string? fsmState)
    {
        if (string.IsNullOrEmpty(fsmState))
        {
            return false;
        }

        foreach (string name in OpenStates)
        {
            if (string.Equals(name, fsmState, System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True while the window is open: корень обязан быть 1; окно закрывается, панели и корень
    /// не трогаем, иначе пин спорит с ванильным таймером закрытия и фейд не доходит до нуля.
    /// </summary>
    /// <param name="fsmState">Active state name of <c>shop_control</c>.</param>
    public static bool ShouldPinRoot(string? fsmState)
    {
        return IsOpenState(fsmState);
    }

    /// <summary>
    /// True while the panels have to be raised: the window is open and its panels were
    /// not restored yet in this session. The session flag is passed in, not held here.
    /// </summary>
    /// <param name="fsmState">Active state name of <c>shop_control</c>.</param>
    /// <param name="alreadyRestored">The panels were already raised in this window session.</param>
    public static bool ShouldRestorePanels(string? fsmState, bool alreadyRestored)
    {
        return IsOpenState(fsmState) && !alreadyRestored;
    }
}
