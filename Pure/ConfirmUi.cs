namespace CrestPicker.Pure;

/// <summary>When the vanilla purchase confirmation covers the crest picture.</summary>
internal static class ConfirmUi
{
    /// <summary>Fade alpha above which the confirmation counts as visible.</summary>
    internal const float AlphaThreshold = 0.01f;

    /// <summary>True when the confirmation group is on and its fade alpha is above the threshold.</summary>
    /// <param name="groupActive">The confirmation group is active in the hierarchy.</param>
    /// <param name="groupAlpha">Fade alpha of the confirmation group.</param>
    internal static bool IsShowing(bool groupActive, float groupAlpha)
    {
        return groupActive && groupAlpha > AlphaThreshold;
    }
}
