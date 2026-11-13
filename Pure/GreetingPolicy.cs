namespace CrestPicker.Pure;

/// <summary>
/// Decision whether the chapel maid greets the hero now, free of game and Unity types.
/// </summary>
internal static class GreetingPolicy
{
    /// <summary>True when a target with this horizontal offset from the maid is on her left.</summary>
    /// <param name="dx">Target X minus maid X.</param>
    internal static bool FacingLeft(float dx)
    {
        return dx < 0f;
    }
}
