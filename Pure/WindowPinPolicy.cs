namespace CrestPicker.Pure;

/// <summary>
/// Whether the shared shop window root alpha must be written to zero.
/// Vanilla only drives the root <c>AlphaSelf</c>, and only one direction is ours:
/// we push it down after idle, vanilla fades it up when it opens the window.
/// Free of Unity types so it can be unit tested.
/// </summary>
public static class WindowPinPolicy
{
    /// <summary>True only in the exact <c>Idle</c> state.</summary>
    public static bool ShouldPinToZero(string? state)
    {
        return state == "Idle";
    }
}
