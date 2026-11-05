using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using Newtonsoft.Json;

namespace CrestPicker;

/// <summary>
/// Per-save-slot flag that remembers whether the chapel maid already offered
/// the crest choice. The slot key is <c>GameManager.profileID</c>, the same
/// number the game uses to address the save file.
/// </summary>
internal static class CrestOfferState
{
    private static readonly string StateFile =
        Path.Combine(Paths.ConfigPath, "CrestPicker", "state.json");

    private static Dictionary<string, bool>? _slots;

    /// <summary>True when the offer was already consumed for this save slot.</summary>
    /// <param name="slotId">Save slot index; negative values are never offered.</param>
    internal static bool IsOffered(int slotId)
    {
        if (slotId < 0)
        {
            return false;
        }

        Dictionary<string, bool> slots = Load();
        return slots.TryGetValue(slotId.ToString(), out bool offered) && offered;
    }

    /// <summary>Records that the player picked a crest in this save slot.</summary>
    /// <param name="slotId">Save slot index; negative values are ignored.</param>
    internal static void MarkOffered(int slotId)
    {
        if (slotId < 0)
        {
            Plugin.Log.LogWarning("[CREST] Offer state not saved: unknown save slot.");
            return;
        }

        Dictionary<string, bool> slots = Load();
        slots[slotId.ToString()] = true;
        Save(slots);
    }

    /// <summary>Logs a JSON round-trip and path check. Returns true on success.</summary>
    internal static bool RunSelfCheck()
    {
        try
        {
            Dictionary<string, bool> sample = new Dictionary<string, bool> { ["7"] = true };
            string json = JsonConvert.SerializeObject(sample);
            Dictionary<string, bool>? back =
                JsonConvert.DeserializeObject<Dictionary<string, bool>>(json);
            bool ok = back != null && back.TryGetValue("7", out bool value) && value;
            Plugin.Log.LogInfo($"[CREST] Offer state self-check {(ok ? "passed" : "FAILED")} ({StateFile}).");
            return ok;
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[CREST] Offer state self-check failed: {ex}");
            return false;
        }
    }

    private static Dictionary<string, bool> Load()
    {
        if (_slots != null)
        {
            return _slots;
        }

        try
        {
            if (File.Exists(StateFile))
            {
                string json = File.ReadAllText(StateFile);
                _slots = JsonConvert.DeserializeObject<Dictionary<string, bool>>(json);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[CREST] Offer state read failed: {ex.Message}");
        }

        return _slots ??= new Dictionary<string, bool>();
    }

    private static void Save(Dictionary<string, bool> slots)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
            File.WriteAllText(StateFile, JsonConvert.SerializeObject(slots, Formatting.Indented));
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[CREST] Offer state write failed: {ex}");
        }
    }
}
