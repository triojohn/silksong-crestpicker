using System;
using System.Collections.Generic;
using System.Linq;

namespace CrestPicker;

/// <summary>
/// Core crest selection logic with no UI dependency. All game calls are wrapped
/// in try/catch so a failure never takes the game down.
/// </summary>
internal static class CrestPickerService
{
    /// <summary>
    /// When true, every base crest is offered regardless of save state.
    /// Set from the BepInEx config in <see cref="Plugin.Awake"/>.
    /// </summary>
    internal static bool GrantAllCrests;

    /// <summary>
    /// Returns the crests the player may pick: base versions that are not hidden,
    /// limited to unlocked ones unless <see cref="GrantAllCrests"/> is set.
    /// </summary>
    /// <returns>Selectable crests in crest-list order; empty on failure.</returns>
    internal static List<ToolCrest> GetSelectableCrests()
    {
        try
        {
            List<ToolCrest> baseCrests = ToolItemManager.GetAllCrests()
                .Where(crest => crest != null && crest.IsBaseVersion && !crest.IsHidden)
                .ToList();

            if (GrantAllCrests)
            {
                return baseCrests;
            }

            return baseCrests.Where(crest => crest.IsUnlocked).ToList();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[CREST] GetSelectableCrests failed: {ex}");
            return new List<ToolCrest>();
        }
    }

    /// <summary>
    /// Returns the label to show for a crest: the localised display name when the
    /// game provides one, otherwise the asset name.
    /// </summary>
    /// <param name="crest">Crest to label; may be null.</param>
    /// <returns>Non-empty label when possible.</returns>
    internal static string GetLabel(ToolCrest crest)
    {
        if (crest == null)
        {
            return string.Empty;
        }

        try
        {
            string localised = crest.DisplayName;
            if (!string.IsNullOrWhiteSpace(localised))
            {
                return localised;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[CREST] DisplayName lookup failed for '{crest.name}': {ex.Message}");
        }

        return crest.name ?? string.Empty;
    }

    /// <summary>
    /// Unlocks the crest if needed, equips it and forces the game to refresh
    /// equipped state and the hero's tool bindings.
    /// </summary>
    /// <param name="crest">Crest to apply; must not be null.</param>
    /// <returns>True when the crest was applied without exceptions.</returns>
    internal static bool ApplyCrest(ToolCrest crest)
    {
        if (crest == null)
        {
            Plugin.Log.LogError("[CREST] ApplyCrest called with null crest.");
            return false;
        }

        try
        {
            bool unlockedNow = false;
            if (!crest.IsUnlocked)
            {
                crest.Unlock();
                unlockedNow = true;
            }

            ToolItemManager.SetEquippedCrest(crest.name);
            ToolItemManager.SendEquippedChangedEvent(force: true);

            Plugin.Log.LogInfo(
                $"[CREST] Applied '{crest.name}' (unlockedNow={unlockedNow}, " +
                $"isUnlocked={crest.IsUnlocked}, isEquipped={crest.IsEquipped}).");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[CREST] ApplyCrest('{crest.name}') failed: {ex}");
            return false;
        }
    }
}
