using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace CrestPicker.Pure;

/// <summary>
/// Persistence for the per-save-slot offer flags, free of game and BepInEx types.
/// A broken file never throws: the caller gets an empty dictionary instead.
/// </summary>
internal static class OfferStore
{
    /// <summary>Parses the state file contents.</summary>
    /// <param name="json">File contents; may be broken.</param>
    /// <param name="slots">Parsed slots, empty when parsing fails.</param>
    /// <returns>False when the JSON could not be parsed.</returns>
    internal static bool Load(string json, out Dictionary<string, bool> slots)
    {
        slots = new Dictionary<string, bool>();
        try
        {
            Dictionary<string, bool>? parsed =
                JsonConvert.DeserializeObject<Dictionary<string, bool>>(json);
            if (parsed == null)
            {
                return false;
            }

            slots = parsed;
            return true;
        }
        catch (Exception)
        {
            // Broken JSON is a normal state here: the file is a cache, not save data.
            return false;
        }
    }

    /// <summary>Reads the state file.</summary>
    /// <param name="path">State file path.</param>
    /// <param name="slots">Parsed slots, empty when the file is missing or broken.</param>
    /// <returns>False when an existing file could not be read or parsed.</returns>
    internal static bool LoadFile(string path, out Dictionary<string, bool> slots)
    {
        slots = new Dictionary<string, bool>();
        if (!File.Exists(path))
        {
            return true;
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception)
        {
            return false;
        }

        return Load(json, out slots);
    }

    /// <summary>Serialises the slots for the state file.</summary>
    /// <param name="slots">Slots to write.</param>
    /// <returns>Indented JSON.</returns>
    internal static string Serialize(IDictionary<string, bool> slots)
    {
        return JsonConvert.SerializeObject(slots, Formatting.Indented);
    }

    /// <summary>Writes the state file, creating its directory when needed.</summary>
    /// <param name="path">State file path.</param>
    /// <param name="slots">Slots to write.</param>
    /// <returns>False when the write failed.</returns>
    internal static bool SaveFile(string path, IDictionary<string, bool> slots)
    {
        try
        {
            string directory = Path.GetDirectoryName(path) ?? ".";
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, Serialize(slots));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>True when the offer was already consumed for this save slot.</summary>
    /// <param name="slots">Stored slots.</param>
    /// <param name="slot">Save slot index; negative values are never offered.</param>
    /// <returns>True when the slot was marked offered.</returns>
    internal static bool IsOffered(IDictionary<string, bool> slots, int slot)
    {
        if (slot < 0)
        {
            return false;
        }

        return slots.TryGetValue(slot.ToString(), out bool offered) && offered;
    }

    /// <summary>Records that the offer was consumed for this save slot.</summary>
    /// <param name="slots">Stored slots, updated in place.</param>
    /// <param name="slot">Save slot index; negative values are ignored.</param>
    internal static void Mark(IDictionary<string, bool> slots, int slot)
    {
        if (slot < 0)
        {
            return;
        }

        slots[slot.ToString()] = true;
    }
}
