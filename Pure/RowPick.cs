using System;

namespace CrestPicker.Pure;

/// <summary>Row lookup by the position of the list selector bar, free of Unity types so it can be unit tested.</summary>
internal static class RowPick
{
    /// <summary>
    /// Row nearest to <paramref name="selectorY"/> within <paramref name="tolerance"/>.
    /// A selector strictly between two rows beyond the tolerance picks nothing.
    /// </summary>
    /// <param name="selectorY">Y of the selection bar in the same space as the rows.</param>
    /// <param name="rowYs">Row Y positions indexed by row number; NaN marks an absent row.</param>
    /// <param name="tolerance">Largest accepted distance to a row; negative accepts nothing.</param>
    /// <returns>Row index, or -1 when no row is close enough.</returns>
    internal static int PickRowIndex(float selectorY, float[] rowYs, float tolerance)
    {
        if (rowYs == null || rowYs.Length == 0 || !IsSane(selectorY))
        {
            return -1;
        }

        int best = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < rowYs.Length; i++)
        {
            float y = rowYs[i];
            if (!IsSane(y))
            {
                continue;
            }

            float distance = Math.Abs(y - selectorY);
            if (distance <= tolerance && distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>Rejects NaN and infinity so a half-built layout cannot pick a row.</summary>
    private static bool IsSane(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
