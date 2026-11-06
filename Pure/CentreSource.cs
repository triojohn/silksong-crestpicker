using System;

namespace CrestPicker.Pure;

/// <summary>Config parsing for the label centring source, free of BepInEx types.</summary>
internal static class CentreSource
{
    /// <summary>
    /// Picks the centring source named by the config value, or <paramref name="fallback"/>
    /// when the value is missing or not one of <paramref name="allowed"/>.
    /// </summary>
    /// <param name="raw">Raw <c>TextCentreSource</c> value; may be null.</param>
    /// <param name="allowed">Source names the mod understands.</param>
    /// <param name="fallback">Source to use for an unknown value.</param>
    /// <param name="unknown">True when the raw value was not recognised.</param>
    /// <returns>Configured source name when known, otherwise the fallback.</returns>
    internal static string Resolve(string? raw, string[] allowed, string fallback, out bool unknown)
    {
        if (raw != null && Array.IndexOf(allowed, raw) >= 0)
        {
            unknown = false;
            return raw;
        }

        unknown = true;
        return fallback;
    }
}
