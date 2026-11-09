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
    /// Dot size stays as configured when it is a plausible fraction of the row
    /// height. Anything outside 0.15..0.5 is a legacy value (an old absolute
    /// multiplier or the old 0.11 fraction) and falls back to the current default.
    /// </summary>
    /// <param name="dotSize">Raw <c>CrestDotSize</c> value.</param>
    /// <returns>Dot size to use.</returns>
    internal static float NormalizeDotSize(float dotSize)
    {
        return (dotSize < 0.15f || dotSize >= 0.5f) ? 0.373f : dotSize;
    }

    /// <summary>
    /// Locked dot size follows the same rule as <see cref="NormalizeDotSize"/>
    /// but falls back to its own default.
    /// </summary>
    /// <param name="value">Raw <c>CrestLockedDotSize</c> value.</param>
    /// <returns>Locked dot size to use.</returns>
    internal static float NormalizeLockedDotSize(float value)
    {
        return (value < 0.15f || value >= 0.5f) ? 0.174f : value;
    }
}
