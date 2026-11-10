namespace CrestPicker.Pure;

/// <summary>
/// When the watcher has to write the fade alphas the vanilla shop leaves behind: the
/// close timer drops the root <c>AlphaSelf</c> to zero and the open chain never raises it,
/// and the open chain only raises <c>Window</c>, not the panels.
/// Free of Unity types so it can be unit tested.
/// </summary>
public static class WindowPinPolicy
{
    /// <summary>Window alpha above which the window counts as visible.</summary>
    private const float VisibleAlpha = 0.5f;

    /// <summary>
    /// True while the window is visible: окно видно — корень обязан быть 1; окно гаснет —
    /// не трогаем, иначе пин спорит с ванильным таймером закрытия и фейд не доходит до нуля.
    /// </summary>
    /// <param name="windowAlpha">Current fade alpha of the <c>Window</c> child.</param>
    public static bool ShouldPinRoot(float windowAlpha)
    {
        return windowAlpha > VisibleAlpha;
    }

    /// <summary>
    /// True while the panels have to be raised: the window is visible and its panels were
    /// not restored yet in this session. The session flag is passed in, not held here.
    /// </summary>
    /// <param name="windowAlpha">Current fade alpha of the <c>Window</c> child.</param>
    /// <param name="alreadyRestored">The panels were already raised in this window session.</param>
    public static bool ShouldRestorePanels(float windowAlpha, bool alreadyRestored)
    {
        return windowAlpha > VisibleAlpha && !alreadyRestored;
    }
}
