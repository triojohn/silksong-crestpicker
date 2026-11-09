namespace CrestPicker.Pure;

/// <summary>
/// How a crest card is drawn, mirroring the vanilla selected/unselected look in
/// <c>InventoryToolCrest</c>: an unselected card shows only the silhouette with smaller
/// slots, a selected card shows the crest sprite with full-size slots.
/// </summary>
internal static class CrestCardLook
{
    /// <summary>Vanilla <c>_slotScaleOther</c> for an unselected card.</summary>
    private const float UnselectedSlotScale = 0.7f;

    /// <summary>Scale of one slot dot for the card kind.</summary>
    /// <param name="isCurrentCard">True when the card is the equipped/current crest.</param>
    /// <returns>1 for a current card, 0.7 otherwise.</returns>
    internal static float SlotScale(bool isCurrentCard) => isCurrentCard ? 1f : UnselectedSlotScale;

    /// <summary>Whether the card draws the silhouette instead of the crest sprite.</summary>
    /// <param name="isCurrentCard">True when the card is the equipped/current crest.</param>
    /// <returns>True for an unselected card, false for a current one.</returns>
    internal static bool UseSilhouette(bool isCurrentCard) => !isCurrentCard;
}
