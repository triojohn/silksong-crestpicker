namespace CrestPicker.Pure;

/// <summary>Row label choice, free of game types so it can be unit tested.</summary>
internal static class CrestLabel
{
    /// <summary>
    /// Returns the localised display name when the game provides one, otherwise the
    /// asset name. A blank display name falls back to the asset name.
    /// </summary>
    /// <param name="displayName">Localised name; may be null or blank.</param>
    /// <param name="assetName">Unity asset name; may be null.</param>
    /// <returns>Non-empty label when the asset name is known.</returns>
    internal static string LabelOrName(string? displayName, string? assetName)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName;
        }

        return assetName ?? string.Empty;
    }
}
