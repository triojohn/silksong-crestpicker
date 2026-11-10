namespace CrestPicker.Pure;

/// <summary>
/// Which root fade alpha the shared shop window needs, by FSM state.
/// Vanilla only drives the root <c>AlphaSelf</c>: the child panels are not faded
/// by us at all. Free of Unity types so it can be unit tested.
/// </summary>
public static class WindowPinPolicy
{
    /// <summary>States in which the window must be visible.</summary>
    private static readonly string[] OpenStates =
    {
        "Open Window",
        "Item List Up",
        "Open",
    };

    /// <summary>True when the window is up in this state, exact comparison.</summary>
    public static bool IsOpenState(string? state)
    {
        if (state == null)
        {
            return false;
        }

        foreach (string open in OpenStates)
        {
            if (state == open)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Root alpha to force: <c>1</c> in an open state, <c>0</c> in idle,
    /// <c>null</c> when the state is not ours to write.
    /// </summary>
    public static int? RootTarget(string? state)
    {
        if (IsOpenState(state))
        {
            return 1;
        }

        return state == "Idle" ? 0 : null;
    }
}
