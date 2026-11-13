using System;
using System.Collections.Generic;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// Reads crest unlocks from the save slots of the current session so the picker can
/// offer crests owned elsewhere. One slot is requested per frame; each file is only
/// deserialised into a fresh <see cref="SaveGameData"/>, the live player data and the
/// loaded game are never replaced. The merged name set is cached for the slot and
/// scene it was read in.
/// </summary>
internal static class CrestPoolFromSaves
{
    /// <summary>Progress of the read pass that is running now.</summary>
    internal enum LoadState
    {
        /// <summary>Nothing was requested yet.</summary>
        Idle,

        /// <summary>Slots are being read; the pool is not usable yet.</summary>
        Loading,

        /// <summary>Every slot was read; <see cref="Elsewhere"/> is usable.</summary>
        Ready,

        /// <summary>Reading gave up (timeout or broken platform); fall back to the config pool.</summary>
        Failed,
    }

    /// <summary>Upper bound for one full pass over all save slots.</summary>
    internal const float TimeoutSeconds = 2f;

    private const int SlotCount = 5;

    private static readonly HashSet<string> _elsewhere = new HashSet<string>();
    private static readonly Queue<int> _queue = new Queue<int>();

    private static LoadState _state = LoadState.Idle;
    private static int _slot = -1;
    private static string? _scene;
    private static int _slotsRead;
    private static float _startedAt;
    private static float _deadline;
    private static bool _pending;
    private static int _generation;

    /// <summary>Current progress of the read pass.</summary>
    internal static LoadState State => _state;

    /// <summary>True when <see cref="Elsewhere"/> holds a complete read of every slot.</summary>
    internal static bool Ready => _state == LoadState.Ready;

    /// <summary>Crest names unlocked in any save slot, current slot included.</summary>
    internal static IReadOnlyCollection<string> Elsewhere => _elsewhere;

    /// <summary>Number of slots whose bytes arrived during the last pass.</summary>
    internal static int SlotsRead => _slotsRead;

    /// <summary>
    /// Starts reading the other save slots for <paramref name="currentSlot"/>. A completed
    /// pass for the same slot and scene is reused instead of being read again.
    /// </summary>
    /// <param name="currentSlot">Slot of the running game; it is skipped.</param>
    internal static void Begin(int currentSlot)
    {
        if (currentSlot < 0 || currentSlot >= SlotCount)
        {
            Fail($"bad save slot {currentSlot}");
            return;
        }

        if (_state == LoadState.Loading && _slot == currentSlot)
        {
            return;
        }

        string? scene = CurrentScene();
        if (_state == LoadState.Ready && _slot == currentSlot && _scene != null && _scene == scene)
        {
            Plugin.Log.LogInfo(
                $"[POOL] cache hit slot={currentSlot} unique={_elsewhere.Count}");
            return;
        }

        Reset();
        _slot = currentSlot;
        _scene = scene;
        _startedAt = Time.unscaledTime;
        _deadline = _startedAt + TimeoutSeconds;
        for (int i = 0; i < SlotCount; i++)
        {
            if (i != currentSlot)
            {
                _queue.Enqueue(i);
            }
        }

        _state = LoadState.Loading;
    }

    /// <summary>
    /// Advances the read pass by at most one slot, or finishes it when the queue is empty.
    /// Must be called once per frame while the pass is <see cref="LoadState.Loading"/>.
    /// </summary>
    internal static void Tick()
    {
        if (_state != LoadState.Loading)
        {
            return;
        }

        if (Stale())
        {
            Plugin.Log.LogInfo($"[POOL] read reset: slot/scene changed (slot={_slot})");
            Reset();
            return;
        }

        if (Time.unscaledTime > _deadline)
        {
            Fail($"timeout after {TimeoutSeconds:0}s slots={_slotsRead} unique={_elsewhere.Count}");
            return;
        }

        if (_pending)
        {
            return;
        }

        Platform? platform = Platform.Current;
        if (platform == null)
        {
            Fail("Platform.Current is null");
            return;
        }

        if (_queue.Count == 0)
        {
            Finish();
            return;
        }

        int slot = _queue.Dequeue();
        _pending = true;
        int generation = _generation;
        try
        {
            platform.ReadSaveSlot(slot, bytes => OnSlotRead(generation, slot, bytes));
        }
        catch (Exception ex)
        {
            _pending = false;
            LogSlot(slot, 0, ex.GetType().Name + ": " + ex.Message);
        }
    }

    /// <summary>Drops the cached pass so the next <see cref="Begin"/> reads the slots again.</summary>
    internal static void Reset()
    {
        _generation++;
        _state = LoadState.Idle;
        _slot = -1;
        _scene = null;
        _slotsRead = 0;
        _pending = false;
        _elsewhere.Clear();
        _queue.Clear();
    }

    /// <summary>Reads crest unlocks out of one slot file; failures stay inside this slot.</summary>
    private static void OnSlotRead(int generation, int slot, byte[] bytes)
    {
        if (generation != _generation || _state != LoadState.Loading)
        {
            return;
        }

        _pending = false;
        _slotsRead++;

        if (bytes == null || bytes.Length == 0)
        {
            LogSlot(slot, 0, "empty");
            return;
        }

        int crests = 0;
        string? error = null;
        try
        {
            string json = GameManager.GetJsonForSaveBytesStatic(bytes);
            SaveGameData save = SaveDataUtility.DeserializeSaveData<SaveGameData>(json);
            List<string> names = save.playerData.ToolEquips.GetValidNames(data => data.IsUnlocked);
            crests = names.Count;
            foreach (string name in names)
            {
                _elsewhere.Add(name);
            }
        }
        catch (Exception ex)
        {
            error = ex.GetType().Name + ": " + ex.Message;
        }

        LogSlot(slot, crests, error);
    }

    /// <summary>Closes a pass that read every queued slot.</summary>
    private static void Finish()
    {
        _state = LoadState.Ready;
        int elapsedMs = (int)((Time.unscaledTime - _startedAt) * 1000f);
        Plugin.Log.LogInfo(
            $"[POOL] read done slots={_slotsRead} unique={_elsewhere.Count} ms={elapsedMs}");
    }

    /// <summary>Gives up on the pass and logs why.</summary>
    private static void Fail(string reason)
    {
        _state = LoadState.Failed;
        _pending = false;
        Plugin.Log.LogWarning($"[POOL] read failed: {reason}");
    }

    /// <summary>One line per slot: the crests it added, or why the slot was skipped.</summary>
    private static void LogSlot(int slot, int crests, string? error)
    {
        if (error == null)
        {
            Plugin.Log.LogInfo($"[POOL] slot {slot}: ok crests={crests}");
        }
        else if (error == "empty")
        {
            Plugin.Log.LogInfo($"[POOL] slot {slot}: empty");
        }
        else
        {
            Plugin.Log.LogWarning($"[POOL] slot {slot}: err={error}");
        }
    }

    /// <summary>Scene the game is in; the cached pass is bound to it.</summary>
    private static string? CurrentScene()
    {
        GameManager? manager = GameManager.instance;
        return manager != null ? manager.sceneName : null;
    }

    /// <summary>
    /// True when the running pass belongs to another save slot or scene: the cache is keyed
    /// by both, so the pass is dropped instead of being finished under the wrong key.
    /// </summary>
    private static bool Stale()
    {
        int slot = Plugin.CurrentSlotId();
        if (slot >= 0 && slot != _slot)
        {
            return true;
        }

        string? scene = CurrentScene();
        return scene != null && _scene != null && scene != _scene;
    }
}
