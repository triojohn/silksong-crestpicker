namespace CrestPicker.Pure;

/// <summary>
/// Decision whether the chapel maid greets the hero now, free of game and Unity types.
/// </summary>
internal static class GreetingPolicy
{
    /// <summary>
    /// True when the hero has just entered the greeting radius, the first talk is over,
    /// no dialogue runs and the cooldown since the previous greeting has passed.
    /// </summary>
    /// <param name="inRange">Hero is within the greeting radius now.</param>
    /// <param name="wasInRange">Hero was within the radius on the previous frame.</param>
    /// <param name="introDone">The first conversation with the maid has happened.</param>
    /// <param name="dialogueRunning">A dialogue is running right now.</param>
    /// <param name="secondsSinceLastGreet">Seconds since the previous greeting.</param>
    /// <param name="cooldown">Minimum seconds between two greetings.</param>
    internal static bool ShouldGreet(bool inRange, bool wasInRange, bool introDone,
        bool dialogueRunning, float secondsSinceLastGreet, float cooldown)
    {
        if (!introDone || dialogueRunning || !inRange || wasInRange)
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
