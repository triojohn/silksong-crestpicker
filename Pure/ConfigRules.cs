namespace CrestPicker.Pure;

/// <summary>Config value migrations, free of BepInEx types so they can be unit tested.</summary>
internal static class ConfigRules
{
    /// <summary>
    /// Schema scale stays as configured. Values above 2 are the old absolute
    /// multiplier from before the schema fitted the details panel, so they drop to 1.
    /// </summary>
    /// <param name="scale">Raw <c>CrestSchemaScale</c> value.</param>
    /// <returns>Scale to use.</returns>
    internal static float NormalizeSchemaScale(float scale)
    {
        return scale > 2f ? 1f : scale;
    }

    /// <summary>
    /// Dot size stays as configured. Values above 0.5 are the legacy height
    /// multiplier, so they are converted to the current fraction of row height.
    /// </summary>
    /// <param name="dotSize">Raw <c>CrestDotSize</c> value.</param>
    /// <returns>Dot size to use.</returns>
    internal static float NormalizeDotSize(float dotSize)
    {
        return dotSize > 0.5f ? dotSize * 0.11f : dotSize;
    }
}
