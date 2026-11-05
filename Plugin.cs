using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GlobalEnums;
using HarmonyLib;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// BepInEx entry point for the CrestPicker mod.
/// Planned feature: let the player pick a starting crest at the first snail shaman
/// instead of receiving the vanilla one.
/// </summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInProcess("Hollow Knight Silksong.exe")]
public sealed class Plugin : BaseUnityPlugin
{
    /// <summary>Unique plugin identifier.</summary>
    public const string PluginGuid = "io.github.triojohn.crestpicker";

    /// <summary>Human-readable plugin name.</summary>
    public const string PluginName = "CrestPicker";

    /// <summary>Semantic version of the plugin.</summary>
    public const string PluginVersion = "0.1.0";

    /// <summary>Shared log source for static patch code.</summary>
    internal static ManualLogSource Log = null!;

    /// <summary>When true, the chapel maid dialogue offers the crest choice.</summary>
    internal static bool EnableSnailChoice;

    /// <summary>Seconds to wait for a playable state before dropping an offer.</summary>
    private const float OfferGrace = 2f;

    private Harmony? _harmony;

    private ConfigEntry<bool> _grantAllCrests = null!;
    private ConfigEntry<bool> _enableSnailChoice = null!;
    private ConfigEntry<KeyboardShortcut> _testHotkey = null!;

    private bool _loggedAlive;

    private static float _offerDeadline;

    private void Awake()
    {
        Log = Logger;

        Probe.Enabled = Config.Bind("Probe", "Enabled", true,
            "Log dialogue FSMs of early scenes and the collectable/tool name lists.").Value;

        _grantAllCrests = Config.Bind("General", "GrantAllCrests", true,
            "Выдавать все 7 знаков независимо от сейва");
        _enableSnailChoice = Config.Bind("General", "EnableSnailChoice", true,
            "Предлагать выбор знака после диалога с прислужницей капеллы");
        _testHotkey = Config.Bind("General", "TestHotkey", new KeyboardShortcut(KeyCode.F9),
            "Клавиша тестового выбора знака");

        CrestPickerService.GrantAllCrests = _grantAllCrests.Value;
        EnableSnailChoice = _enableSnailChoice.Value;
        CrestSelectMenu.Closed += OnMenuClosed;
        CrestSelectMenu.RunSelfCheck();
        CrestOfferState.RunSelfCheck();

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();

        Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    private void Update()
    {
        if (!_loggedAlive)
        {
            _loggedAlive = true;
            Log.LogInfo($"[CREST] Update alive (gameState={DescribeGameState()}).");
        }

        UpdatePendingOffer();

        if (!IsInGame() || CrestSelectMenu.IsOpen)
        {
            return;
        }

        if (_testHotkey.Value.IsDown())
        {
            Log.LogInfo($"[CREST] Hotkey {_testHotkey.Value} pressed.");
            CrestSelectMenu.TryOpen();
        }
    }

    /// <summary>
    /// Called by the FSM action when the maid dialogue ends. Shows the picker once
    /// per save slot unless it was already used, cancelled offers retry later.
    /// </summary>
    internal static void RequestOffer()
    {
        try
        {
            if (!EnableSnailChoice)
            {
                return;
            }

            if (CrestSelectMenu.IsOpen)
            {
                Log.LogInfo("[CREST] Offer skipped: picker already open.");
                return;
            }

            int slot = CurrentSlotId();
            if (slot < 0)
            {
                Log.LogWarning("[CREST] Offer skipped: save slot id is unknown.");
                return;
            }

            if (CrestOfferState.IsOffered(slot))
            {
                Log.LogInfo($"[CREST] Offer skipped: already used in slot {slot}.");
                return;
            }

            _offerDeadline = Time.unscaledTime + OfferGrace;
            Log.LogInfo($"[CREST] Offer requested for slot {slot}.");
        }
        catch (Exception ex)
        {
            Log.LogError($"[CREST] RequestOffer failed: {ex}");
        }
    }

    /// <summary>Opens a pending offer once the game is playable; drops it after a grace period.</summary>
    private void UpdatePendingOffer()
    {
        if (_offerDeadline <= 0f || CrestSelectMenu.IsOpen)
        {
            return;
        }

        if (IsInGame() && CrestSelectMenu.TryOpen(fromDialogue: true))
        {
            _offerDeadline = 0f;
            return;
        }

        if (Time.unscaledTime > _offerDeadline)
        {
            _offerDeadline = 0f;
            Log.LogWarning($"[CREST] Offer dropped: game not ready (state={DescribeGameState()}).");
        }
    }

    /// <summary>Consumes the one-time flag only for a real selection made by the dialogue offer.</summary>
    private static void OnMenuClosed(bool selected, bool fromDialogue)
    {
        if (!selected || !fromDialogue)
        {
            return;
        }

        int slot = CurrentSlotId();
        CrestOfferState.MarkOffered(slot);
        Log.LogInfo($"[CREST] Offer marked as used for slot {slot}.");
    }

    /// <summary>Game save slot index, taken from the fields the save system uses.</summary>
    private static int CurrentSlotId()
    {
        GameManager? manager = GameManager.instance;
        if (manager != null && manager.profileID >= 0)
        {
            return manager.profileID;
        }

        PlayerData? data = PlayerData.instance;
        return data != null ? data.profileID : -1;
    }

    /// <summary>Current game state for diagnostics, never throws.</summary>
    private static string DescribeGameState()
    {
        GameManager? manager = GameManager.instance;
        return manager == null ? "no GameManager" : manager.GameState.ToString();
    }

    /// <summary>True only while a loaded scene is being played.</summary>
    private static bool IsInGame()
    {
        GameManager? manager = GameManager.instance;
        return manager != null && manager.GameState == GameState.PLAYING;
    }

    private void OnDestroy()
    {
        CrestSelectMenu.Closed -= OnMenuClosed;
        _harmony?.UnpatchSelf();
    }
}

/// <summary>
/// Placeholder container for Harmony patches. No patches exist yet: the FSM hook
/// on the snail shaman dialogue lands in stage 3 (see PLAN.md).
/// </summary>
internal static class HarmonyPatches
{
    // TODO(stage 3): patch the snail shaman FSM end state and open the crest picker UI.
}
