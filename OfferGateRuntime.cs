using System;
using System.Collections.Generic;
using CrestPicker.Pure;

namespace CrestPicker;

/// <summary>
/// Feeds live game state into <see cref="OfferGate"/>: counts the normal crests the save
/// slots hold, records that count in the config once per session and answers whether the
/// chapel maid chain (wave and call) may run.
/// </summary>
internal static class OfferGateRuntime
{
    private static bool _countWritten;
    private static bool _overrideLogged;

    /// <summary>
    /// Starts the cross-save read once a save slot is loaded and writes the automatic
    /// crest count into the config when the read is done. Never throws.
    /// </summary>
    internal static void Tick()
    {
        try
        {
            Step();
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[GATE] tick failed: {ex.Message}");
        }
    }

    /// <summary>
    /// True when the maid chain may run now. The greeting calls this once per approach,
    /// so each call leaves one <c>[GATE]</c> line with the numbers it decided on.
    /// </summary>
    internal static bool ShouldRunChain()
    {
        int saveCrests = SaveCrests();
        int unlockedHere = NormalsHere();
        int slot = Plugin.CurrentSlotId();
        PlayerData? data = PlayerData.instance;
        bool introDone = data != null && data.churchKeeperIntro;
        bool offered = slot >= 0 && CrestOfferState.IsOffered(slot);
        OfferGateReason reason = OfferGate.Reason(saveCrests, unlockedHere, introDone, offered);
        if (reason == OfferGateReason.Ok)
        {
            Plugin.Log.LogInfo(
                $"[GATE] chain enabled saveCrests={saveCrests} unlockedHere={unlockedHere}" +
                $" reason={reason}");
        }
        else
        {
            Plugin.Log.LogInfo(
                $"[GATE] chain disabled reason={reason} saveCrests={saveCrests}" +
                $" unlockedHere={unlockedHere}");
        }

        return reason == OfferGateReason.Ok;
    }

    /// <summary>One frame of the automatic crest count: start the read, then write the value.</summary>
    private static void Step()
    {
        int manual = Plugin.SaveCrestCountOverride.Value;
        if (manual >= 0)
        {
            LogOverride(manual);
            return;
        }

        if (_countWritten)
        {
            return;
        }

        int slot = Plugin.CurrentSlotId();
        if (slot < 0)
        {
            return;
        }

        if (CrestPoolFromSaves.State == CrestPoolFromSaves.LoadState.Idle)
        {
            CrestPoolFromSaves.Begin(slot);
            return;
        }

        if (CrestPoolFromSaves.State != CrestPoolFromSaves.LoadState.Ready)
        {
            return;
        }

        List<ToolCrest> normals = Normals();
        if (normals.Count == 0)
        {
            return; // the crest list is not loaded yet; try again on the next frame
        }

        var elsewhere = new HashSet<string>(CrestPoolFromSaves.Elsewhere);
        int count = 0;
        foreach (ToolCrest crest in normals)
        {
            if (elsewhere.Contains(crest.name))
            {
                count++;
            }
        }

        Plugin.SaveCrestCount.Value = count;
        _countWritten = true;
        Plugin.Log.LogInfo($"[GATE] SaveCrestCount={count} (auto)");
    }

    /// <summary>Crest count the gate uses: the manual override when set, else the config value.</summary>
    private static int SaveCrests()
    {
        int manual = Plugin.SaveCrestCountOverride.Value;
        return manual >= 0 ? manual : Plugin.SaveCrestCount.Value;
    }

    /// <summary>Normal crests unlocked in this playthrough.</summary>
    private static int NormalsHere()
    {
        int count = 0;
        foreach (ToolCrest crest in Normals())
        {
            if (crest.IsUnlocked)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>Normal crests the game knows: base versions that are not hidden.</summary>
    private static List<ToolCrest> Normals()
    {
        var normals = new List<ToolCrest>();
        foreach (ToolCrest? crest in ToolItemManager.GetAllCrests())
        {
            if (crest != null && CrestPoolRules.IsNormal(crest.IsBaseVersion, crest.IsHidden))
            {
                normals.Add(crest);
            }
        }

        return normals;
    }

    /// <summary>Logs the manual value once per session, so the config state is visible.</summary>
    private static void LogOverride(int manual)
    {
        if (_overrideLogged)
        {
            return;
        }

        _overrideLogged = true;
        Plugin.Log.LogInfo($"[GATE] SaveCrestCount={manual} (override)");
    }
}
