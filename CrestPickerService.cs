using System;
using System.Collections.Generic;
using System.Linq;
using CrestPicker.Pure;

namespace CrestPicker;

/// <summary>
/// Core crest selection logic with no UI dependency. All game calls are wrapped
/// in try/catch so a failure never takes the game down.
/// </summary>
internal static class CrestPickerService
{
    /// <summary>
    /// Crest pool source. Set from the BepInEx config in <see cref="Plugin.Awake"/>;
    /// defaults to <see cref="CrestPoolMode.All"/>.
    /// </summary>
    internal static CrestPoolMode PoolMode = CrestPoolMode.All;

    /// <summary>
    /// Returns the crests the player may pick: base versions that are not hidden.
    /// Unlock state is ignored while <c>IgnoreUnlockState</c> is true; otherwise
    /// <see cref="CrestPoolMode.Unlocked"/> limits the pool to unlocked crests.
    /// The currently equipped crest is excluded only while <c>IncludeEquippedCrest</c>
    /// is false.
    /// </summary>
    /// <returns>Selectable crests in crest-list order; empty on failure.</returns>
    internal static List<ToolCrest> GetSelectableCrests()
    {
        try
        {
            bool ignoreUnlock = Plugin.IgnoreUnlockState.Value;
            bool includeEquipped = Plugin.IncludeEquippedCrest.Value;
            List<ToolCrest> all = ToolItemManager.GetAllCrests();
            var byName = new Dictionary<string, ToolCrest>();
            var facts = new List<CrestFacts>();
            foreach (ToolCrest? crest in all)
            {
                if (crest == null)
                {
                    continue;
                }

                byName[crest.name] = crest;
                facts.Add(new CrestFacts(
                    crest.name, crest.IsBaseVersion, crest.IsHidden, crest.IsUnlocked, SafeIsEquipped(crest)));
            }

            List<CrestFacts> selected = CrestPoolRules.Select(facts, PoolMode, ignoreUnlock, includeEquipped);
            List<ToolCrest> pool = selected.Select(fact => byName[fact.Name]).ToList();
            int excluded = includeEquipped
                ? 0
                : CrestPoolRules.Select(facts, PoolMode, ignoreUnlock, includeEquipped: true).Count - pool.Count;
            Plugin.Log.LogInfo(
                $"[CREST] pool={pool.Count} ignoreUnlock={ignoreUnlock} includeEquipped={includeEquipped}" +
                $" all={all.Count}" +
                $" names={string.Join(",", pool.Select(crest => crest.name))}" +
                $" (mode={PoolMode}, equipped excluded={excluded}).");
            foreach (ToolCrest? crest in all)
            {
                Plugin.Log.LogInfo(crest == null
                    ? "[CREST]   <null>"
                    : $"[CREST]   {crest.name} base={crest.IsBaseVersion}" +
                        $" hidden={crest.IsHidden} unlocked={crest.IsUnlocked}");
            }

            return pool;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[CREST] GetSelectableCrests failed: {ex}");
            return new List<ToolCrest>();
        }
    }

    /// <summary>Equipped check that never throws when player data is not ready.</summary>
    private static bool SafeIsEquipped(ToolCrest crest)
    {
        try
        {
            return crest.IsEquipped;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[CREST] IsEquipped failed for '{crest.name}': {ex.Message}");
            return false;
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

        string? localised = null;
        try
        {
            localised = crest.DisplayName;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[CREST] DisplayName lookup failed for '{crest.name}': {ex.Message}");
        }

        return CrestLabel.LabelOrName(localised, crest.name);
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
