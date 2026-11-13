namespace CrestPicker.Pure;

/// <summary>
/// Decision whether the chapel maid greets the hero now, free of game and Unity types.
/// </summary>
internal static class GreetingPolicy
{
    /// <summary>
    /// Next arming state of the greeting edge: armed outside <paramref name="exitRadius"/>,
    /// disarmed inside <paramref name="enterRadius"/>, unchanged in the band between them.
    /// </summary>
    /// <param name="armed">The edge was ready to fire on the previous frame.</param>
    /// <param name="distance">Distance from the maid to the hero.</param>
    /// <param name="enterRadius">Radius that fires the greeting.</param>
    /// <param name="exitRadius">Radius the hero must leave by to re-arm the greeting.</param>
    internal static bool ShouldArm(bool armed, float distance, float enterRadius, float exitRadius)
    {
        if (distance > exitRadius)
        {
            return true;
        }

        return armed && distance > enterRadius;
    }

    /// <summary>
    /// True when the hero has just entered the greeting radius, the first talk is over,
    /// no dialogue runs and the cooldown since the previous greeting has passed.
    /// </summary>
    /// <param name="armed">The edge was ready to fire on the previous frame.</param>
    /// <param name="distance">Distance from the maid to the hero.</param>
    /// <param name="enterRadius">Radius that fires the greeting.</param>
    /// <param name="introDone">The first conversation with the maid has happened.</param>
    /// <param name="dialogueRunning">A dialogue is running right now.</param>
    /// <param name="secondsSinceLastGreet">Seconds since the previous greeting.</param>
    /// <param name="cooldown">Minimum seconds between two greetings.</param>
    internal static bool ShouldGreet(bool armed, float distance, float enterRadius, bool introDone,
        bool dialogueRunning, float secondsSinceLastGreet, float cooldown)
    {
        if (!introDone || dialogueRunning || !armed || distance > enterRadius)
        {
            return false;
        }

        return secondsSinceLastGreet >= cooldown;
    }

    /// <summary>True when a target with this horizontal offset from the maid is on her left.</summary>
    /// <param name="dx">Target X minus maid X.</param>
    internal static bool FacingLeft(float dx)
    {
        return dx < 0f;
    }
}
