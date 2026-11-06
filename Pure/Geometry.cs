using System;

namespace CrestPicker.Pure;

/// <summary>Numeric guards for UI geometry, free of Unity types so they can be unit tested.</summary>
internal static class Geometry
{
    /// <summary>Largest world coordinate accepted from a half-built UI.</summary>
    internal const float SaneLimit = 1000f;

    /// <summary>Rejects NaN, infinity and absurd world values from a half-built UI.</summary>
    /// <param name="value">Coordinate to check.</param>
    /// <returns>True when the value looks usable.</returns>
    internal static bool IsSane(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value) && Math.Abs(value) < SaneLimit;
    }

    /// <summary>
    /// Clamps an axis centre so the segment of half-width <paramref name="half"/>
    /// stays inside [<paramref name="min"/>, <paramref name="max"/>].
    /// </summary>
    /// <param name="centre">Desired centre.</param>
    /// <param name="half">Half size along the axis.</param>
    /// <param name="min">Lower bound.</param>
    /// <param name="max">Upper bound.</param>
    /// <returns>Clamped centre; <paramref name="min"/> when the bounds are inverted.</returns>
    internal static float ClampAxis(float centre, float half, float min, float max)
    {
        if (min > max)
        {
            return min;
        }

        float low = min + half;
        float high = max - half;
        if (low > high)
        {
            // The segment is bigger than the span: centre it instead of overflowing.
            return (min + max) * 0.5f;
        }

        return Math.Clamp(centre, low, high);
    }
}
