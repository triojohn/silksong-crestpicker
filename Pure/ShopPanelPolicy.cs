namespace CrestPicker.Pure;

/// <summary>
/// Which window panels the crest menu has to hide on close, and when.
/// Free of Unity types so it can be unit tested.
/// </summary>
public static class ShopPanelPolicy
{
    /// <summary>Nodes the vanilla close timer never fades; zeroed by us on close.</summary>
    public static readonly string[] PanelsHiddenOnClose =
    {
        "Item List Group",
        "Item Details",
        "Item Confirm Group",
        "Action Cancel",
        "Action Confirm",
    };

    /// <summary>True when a close should hide the shared panels.</summary>
    /// <param name="opening">The window is on its open chain, not closing.</param>
    /// <param name="alreadyHidden">The panels were hidden already this session.</param>
    public static bool ShouldHidePanels(bool opening, bool alreadyHidden)
    {
        return !opening && !alreadyHidden;
    }

    /// <summary>
    /// False for every phase: a normal close never writes the root fade alpha. The
    /// emergency teardown keeps its own write outside this policy, because a window
    /// left visible there is worse than a stale alpha.
    /// </summary>
    public static bool ShouldWriteRootAlpha(int phase)
    {
        return false;
    }
}
