using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Newtonsoft.Json;
using CrestPicker.Pure;

namespace CrestPicker;

/// <summary>
/// Per-save-slot flag that remembers whether the chapel maid already offered
/// the crest choice. The slot key is <c>GameManager.profileID</c>, the same
/// number the game uses to address the save file. Parsing and writing live in
/// <see cref="OfferStore"/>; this class only adds the default path and logging.
/// </summary>
internal static class CrestOfferState
{
    /// <summary>State file used when a caller passes no path.</summary>
    internal static readonly string DefaultStateFile =
        Path.Combine(Paths.ConfigPath, "CrestPicker", "state.json");

    private static Dictionary<string, bool>? _slots;
    private static string? _loadedPath;

    /// <summary>True when the offer was already consumed for this save slot.</summary>
    /// <param name="slotId">Save slot index; negative values are never offered.</param>
    /// <param name="stateFile">State file path; defaults to <see cref="DefaultStateFile"/>.</param>
    internal static bool IsOffered(int slotId, string? stateFile = null)
    {
        return OfferStore.IsOffered(Load(stateFile), slotId);
    }

    /// <summary>Records that the player picked a crest in this save slot.</summary>
    /// <param name="slotId">Save slot index; negative values are ignored.</param>
    /// <param name="stateFile">State file path; defaults to <see cref="DefaultStateFile"/>.</param>
    internal static void MarkOffered(int slotId, string? stateFile = null)
    {
        if (slotId < 0)
        {
            Plugin.Log.LogWarning("[CREST] Offer state not saved: unknown save slot.");
            return;
        }

        Dictionary<string, bool> slots = Load(stateFile);
        OfferStore.Mark(slots, slotId);
        if (!OfferStore.SaveFile(stateFile ?? DefaultStateFile, slots))
        {
            Plugin.Log.LogError($"[CREST] Offer state write failed ({stateFile ?? DefaultStateFile}).");
        }
    }

    /// <summary>Logs a JSON round-trip and path check. Returns true on success.</summary>
    internal static bool RunSelfCheck()
    {
        try
        {
            Dictionary<string, bool> sample = new Dictionary<string, bool> { ["7"] = true };
            string json = OfferStore.Serialize(sample);
            bool ok = OfferStore.Load(json, out Dictionary<string, bool> back) &&
                OfferStore.IsOffered(back, 7);
            Plugin.Log.LogInfo($"[CREST] Offer state self-check {(ok ? "passed" : "FAILED")} ({DefaultStateFile}).");
            return ok;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[CREST] Offer state self-check failed: {ex}");
            return false;
        }
    }

    private static Dictionary<string, bool> Load(string? stateFile)
    {
        string path = stateFile ?? DefaultStateFile;
        if (_slots != null && _loadedPath == path)
        {
            return _slots;
        }

        if (!OfferStore.LoadFile(path, out Dictionary<string, bool> slots))
        {
            Plugin.Log.LogWarning($"[CREST] Offer state read failed, starting empty ({path}).");
        }

        _slots = slots;
        _loadedPath = path;
        return _slots;
    }
}
