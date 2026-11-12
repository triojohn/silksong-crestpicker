using System;

namespace CrestPicker.Pure;

/// <summary>Vertical placement of the crest schema, free of Unity types so it can be unit tested.</summary>
internal static class SchemaPlacement
{
    /// <summary>Smallest move in world units that counts as a clamp.</summary>
    private const float Tolerance = 0.0001f;

    /// <summary>Gap between the description text and the schema top, as a fraction of the panel height.</summary>
    internal const float GapFraction = 0.02f;

    /// <summary>Fraction of the panel height the schema aims to fill.</summary>
    internal const float PanelFill = 0.8f;

    /// <summary>Smallest fraction of the panel height the schema keeps when the text leaves little room.</summary>
    internal const float MinFill = 0.4f;

    /// <summary>
    /// Top edge of the schema: the bottom of the description text pulled down by the gap.
    /// </summary>
    /// <param name="descBottomY">World Y of the lowest drawn description character.</param>
    /// <param name="gapFraction">Gap as a fraction of the panel height.</param>
    /// <param name="panelHeight">World height of the details panel.</param>
    /// <returns>World Y the schema top should take.</returns>
    internal static float TopFromText(float descBottomY, float gapFraction, float panelHeight)
    {
        return descBottomY - (gapFraction * panelHeight);
    }

    /// <summary>
    /// Height the drawn schema is fitted to: the space between the panel bottom and the
    /// schema top, capped at a fraction of the panel height, and never below the minimum.
    /// </summary>
    /// <param name="panelMinY">Lower edge of the details panel.</param>
    /// <param name="panelHeight">World height of the details panel.</param>
    /// <param name="topY">World Y the schema top should take.</param>
    /// <param name="fillFraction">Fraction of the panel height the schema aims to fill.</param>
    /// <param name="scale">User scale multiplier.</param>
    /// <param name="minFraction">Smallest fraction of the panel height the schema keeps.</param>
    /// <returns>World height the silhouette is fitted to.</returns>
    internal static float FitTarget(float panelMinY, float panelHeight, float topY, float fillFraction,
        float scale, float minFraction)
    {
        float avail = topY - panelMinY;
        float target = Math.Min(fillFraction * panelHeight * scale, avail);
        return Math.Max(target, minFraction * panelHeight);
    }

    /// <summary>Centre that puts a silhouette of <paramref name="drawnHeight"/> under the top.</summary>
    /// <param name="topY">World Y the schema top should take.</param>
    /// <param name="drawnHeight">World height the silhouette actually draws.</param>
    /// <returns>World Y the schema centre should take.</returns>
    internal static float CentreY(float topY, float drawnHeight)
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
