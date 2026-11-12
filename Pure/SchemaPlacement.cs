using System;

namespace CrestPicker.Pure;

/// <summary>Vertical placement of the crest schema, free of Unity types so it can be unit tested.</summary>
internal static class SchemaPlacement
{
    /// <summary>Smallest move in world units that counts as a clamp.</summary>
    private const float Tolerance = 0.0001f;

    /// <summary>Gap between the description text and the schema top, as a fraction of the panel height.</summary>
    internal const float GapFraction = 0.02f;

    /// <summary>Top edge of the schema: the bottom of the description rect pulled down by the gap.</summary>
    /// <param name="rectBottomY">World Y of the lowest corner of the description rect.</param>
    /// <param name="gapFraction">Gap as a fraction of the panel height.</param>
    /// <param name="panelHeight">World height of the details panel.</param>
    /// <returns>World Y the schema top should take.</returns>
    internal static float TopFromRectBottom(float rectBottomY, float gapFraction, float panelHeight)
    {
        return rectBottomY - (gapFraction * panelHeight);
    }

    /// <summary>Centre that puts a silhouette of <paramref name="drawnHeight"/> under the top.</summary>
    /// <param name="topY">World Y the schema top should take.</param>
    /// <param name="drawnHeight">World height the silhouette actually draws.</param>
    /// <returns>World Y the schema centre should take.</returns>
    internal static float CentreYFromTop(float topY, float drawnHeight)
    {
        return topY - (drawnHeight / 2f);
    }

    /// <summary>
    /// Centre the schema aims at: the panel centre shifted by a fraction of the panel height.
    /// </summary>
    /// <param name="panelMinY">Lower edge of the details panel.</param>
    /// <param name="panelCenterY">Centre of the details panel.</param>
    /// <param name="panelHeight">Height of the details panel.</param>
    /// <param name="offsetFraction">Shift as a fraction of the panel height; negative goes down.</param>
    /// <returns>World Y the schema centre should take.</returns>
    internal static float TargetY(float panelMinY, float panelCenterY, float panelHeight,
        float offsetFraction)
    {
        return panelCenterY + (offsetFraction * panelHeight);
    }

    /// <summary>
    /// Pulls a schema centre inside the panel, keeping half the schema height clear of both
    /// edges. A schema taller than the panel cannot be kept inside, so the centre is
    /// returned unchanged instead of overflowing to an edge.
    /// </summary>
    /// <param name="centreY">Desired world Y of the schema centre.</param>
    /// <param name="halfHeight">Half the drawn schema height.</param>
    /// <param name="panelMinY">Lower edge of the details panel.</param>
    /// <param name="panelMaxY">Upper edge of the details panel.</param>
    /// <returns>Centre that keeps the schema inside the panel.</returns>
    internal static float ClampCentreY(float centreY, float halfHeight, float panelMinY, float panelMaxY)
    {
        float low = panelMinY + halfHeight;
        float high = panelMaxY - halfHeight;
        if (low > high)
        {
            return centreY;
        }

        return Math.Clamp(centreY, low, high);
    }

    /// <summary>True when <see cref="ClampCentreY"/> would move the centre.</summary>
    /// <param name="centreY">Desired world Y of the schema centre.</param>
    /// <param name="halfHeight">Half the drawn schema height.</param>
    /// <param name="panelMinY">Lower edge of the details panel.</param>
    /// <param name="panelMaxY">Upper edge of the details panel.</param>
    internal static bool IsClamped(float centreY, float halfHeight, float panelMinY, float panelMaxY)
    {
        return Math.Abs(ClampCentreY(centreY, halfHeight, panelMinY, panelMaxY) - centreY) > Tolerance;
    }
}
