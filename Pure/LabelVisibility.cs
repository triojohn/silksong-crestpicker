namespace CrestPicker.Pure;

/// <summary>Why a fallback row label is not drawn, or that it is drawn.</summary>
internal enum LabelHiddenReason
{
    /// <summary>All conditions hold, the label is drawn.</summary>
    Visible,

    /// <summary>Row object is not active.</summary>
    Inactive,

    /// <summary>Row no longer overlaps the item list.</summary>
    Outside,

    /// <summary>Label mesh is empty.</summary>
    Empty,

    /// <summary>Row fade group alpha is at or below the threshold.</summary>
    Alpha,
}

/// <summary>Row label visibility rule, free of Unity types so it can be unit tested.</summary>
internal static class LabelVisibility
{
    /// <summary>Mesh width at or below this is treated as empty.</summary>
    internal const float MinMeshWidth = 0.0001f;

    /// <summary>Alpha at or below this is treated as invisible.</summary>
    internal const float MinAlpha = 0.01f;

    /// <summary>
    /// Decides whether our own row label is drawn, and if not, the first failing condition
    /// in the order inactive, outside, empty, alpha.
    /// </summary>
    /// <param name="rowActive">Row object is active in the hierarchy.</param>
    /// <param name="insideList">Row overlaps the item list.</param>
    /// <param name="meshWidth">Width of the label renderer bounds.</param>
    /// <param name="fadeAlpha">Alpha of the row fade group.</param>
    /// <returns>Visibility decision for this tick.</returns>
    internal static LabelHiddenReason Evaluate(
        bool rowActive, bool insideList, float meshWidth, float fadeAlpha)
    {
        if (!rowActive)
        {
            return LabelHiddenReason.Inactive;
        }

        if (!insideList)
        {
            return LabelHiddenReason.Outside;
        }

        if (!(meshWidth > MinMeshWidth))
        {
            return LabelHiddenReason.Empty;
        }

        return fadeAlpha > MinAlpha ? LabelHiddenReason.Visible : LabelHiddenReason.Alpha;
    }
}
