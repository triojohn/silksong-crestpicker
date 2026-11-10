namespace CrestPicker.Pure;

/// <summary>
/// Lifecycle decisions for our own shop window clone, the window spawned from the
/// live <c>ShopOwnerBase</c> prefab. Free of Unity types so it can be unit tested.
/// </summary>
public static class OwnWindowPolicy
{
    /// <summary>
    /// Clone root name. Must not match the vanilla <c>Shop Menu(Clone)</c>, or the
    /// window lookup by name finds ours instead of the game's.
    /// </summary>
    public const string RootName = "CrestPicker Shop Menu";

    /// <summary>
    /// True when the cached clone no longer fits: the scene changed, the owner the
    /// clone was spawned for is gone, or another owner took its place.
    /// </summary>
    /// <param name="cachedScene">Scene handle the clone was spawned in.</param>
    /// <param name="scene">Handle of the scene active now.</param>
    /// <param name="cachedOwnerId">Instance id of the owner the clone was spawned for; 0 when none.</param>
    /// <param name="ownerId">Instance id of the owner alive now; 0 when none.</param>
    public static bool ShouldRebuild(int cachedScene, int scene, int cachedOwnerId, int ownerId)
    {
        return cachedScene != scene || cachedOwnerId == 0 || cachedOwnerId != ownerId;
    }

    /// <summary>
    /// True when the vanilla shop window is active, so our clone stays out of the way
    /// of the game's own events.
    /// </summary>
    /// <param name="vanillaWindowActive">The scene's shared shop window is active.</param>
    public static bool ShouldPause(bool vanillaWindowActive)
    {
        return vanillaWindowActive;
    }
}
