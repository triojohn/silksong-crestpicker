namespace CrestPicker.Pure;

/// <summary>
/// Decision whether the chapel maid greets the hero now, free of game and Unity types.
/// </summary>
internal static class GreetingPolicy
{
    /// <summary>
    /// True when the hero stands inside the greeting radius and the cooldown since the
    /// previous cycle has passed, so the maid repeats her gesture.
    /// </summary>
    /// <param name="distance">Distance from the maid to the hero.</param>
    /// <param name="radius">Radius that keeps the greeting cycle running.</param>
    /// <param name="elapsed">Seconds since the previous cycle started.</param>
    /// <param name="cooldown">Minimum seconds between two cycles.</param>
    internal static bool ShouldRepeat(float distance, float radius, float elapsed, float cooldown)
    {
        if (distance > radius)
        {
            return false;
        }

        return elapsed >= cooldown;
    }

    /// <summary>True when a target with this horizontal offset from the maid is on her left.</summary>
    /// <param name="dx">Target X minus maid X.</param>
    internal static bool FacingLeft(float dx)
    {
        return dx < 0f;
    }
}
