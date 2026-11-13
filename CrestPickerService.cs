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
    /// <see cref="CrestPoolMode.FromSaves"/> builds the pool from crests unlocked in the
    /// other save slots, starting that read on first use, and reports
    /// <paramref name="waiting"/> instead of falling back to every crest while those slots
    /// are still being read. The other modes keep the config rules: unlock state is ignored
    /// while <c>IgnoreUnlockState</c> is true, and <see cref="CrestPoolMode.Unlocked"/> limits
    /// the pool to unlocked crests. The currently equipped crest is excluded only while
    /// <c>IncludeEquippedCrest</c> is false.
    /// </summary>
    /// <param name="waiting">True when the cross-save read has not finished; the list is empty.</param>
    /// <param name="empty">Why the cross-save pool has no crests, if it has none.</param>
    /// <returns>Selectable crests in crest-list order; empty on failure or while waiting.</returns>
    internal static List<ToolCrest> GetSelectableCrests(out bool waiting, out CrestPoolEmpty empty)
    {
        waiting = false;
        empty = CrestPoolEmpty.None;
        try
        {
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

            List<CrestFacts> selected;
            if (PoolMode == CrestPoolMode.FromSaves)
            {
                CrestPoolFromSaves.LoadState state = CrestPoolFromSaves.State;
                if (state == CrestPoolFromSaves.LoadState.Idle)
                {
                    // First need for the pool this session: start the read instead of falling back.
                    CrestPoolFromSaves.Begin(Plugin.CurrentSlotId());
                    state = CrestPoolFromSaves.State;
                }

                if (state == CrestPoolFromSaves.LoadState.Loading)
                {
                    waiting = true;
                    Plugin.Log.LogInfo($"[POOL] waiting slots={CrestPoolFromSaves.SlotsRead}");
                    return new List<ToolCrest>();
                }

                IEnumerable<IEnumerable<string>> slots;
                int elsewhere;
                if (state == CrestPoolFromSaves.LoadState.Ready)
                {
                    slots = new[] { CrestPoolFromSaves.Elsewhere };
                    elsewhere = CrestPoolFromSaves.Elsewhere.Count;
                }
                else
                {
                    // The read gave up (timeout or broken platform): build the pool as if no
                    // other slot owns anything, which yields every normal crest.
                    Plugin.Log.LogInfo(
                        $"[POOL] fallback all-normals reason={state.ToString().ToLowerInvariant()}");
                    slots = Array.Empty<IEnumerable<string>>();
                    elsewhere = 0;
                }

                CrestPoolSelection selection = CrossSavePool.Build(facts, slots, includeEquipped);
                selected = selection.Crests;
                empty = selection.Empty;
                Plugin.Log.LogInfo(
                    $"[POOL] source=saves elsewhere={elsewhere} here={selection.UnlockedHere}" +
                    $" pool={selected.Count} reason={ReasonName(selection.Empty)}" +
                    $" names={string.Join(",", selected.Select(fact => fact.Name))}");
            }
            else
            {
                bool ignoreUnlock = Plugin.IgnoreUnlockState.Value;
                selected = CrestPoolRules.Select(facts, PoolMode, ignoreUnlock, includeEquipped);
                Plugin.Log.LogInfo(
                    $"[POOL] source={PoolMode.ToString().ToLowerInvariant()} pool={selected.Count}" +
                    $" ignoreUnlock={ignoreUnlock} includeEquipped={includeEquipped}" +
                    $" all={all.Count}" +
                    $" names={string.Join(",", selected.Select(fact => fact.Name))}");
            }

            return selected.Select(fact => byName[fact.Name]).ToList();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[CREST] GetSelectableCrests failed: {ex}");
            return new List<ToolCrest>();
        }
    }

    /// <summary>Log name for the reason the cross-save pool fell back, if it did.</summary>
    /// <param name="empty">Reason reported by <see cref="CrossSavePool.Build"/>.</param>
    /// <returns>Single lowercase word for the log line.</returns>
    private static string ReasonName(CrestPoolEmpty empty)
    {
        switch (empty)
        {
            case CrestPoolEmpty.AllNormalsUnlockedHere:
                return "all-normals-unlocked-here";
            case CrestPoolEmpty.NothingAnywhere:
                return "nothing-anywhere";
            case CrestPoolEmpty.NothingNewElsewhere:
                return "nothing-new-elsewhere";
            default:
                return "none";
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
