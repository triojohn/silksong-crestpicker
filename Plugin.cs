using System.Collections.Generic;
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

    private static readonly string[] ChoiceKeys =
    {
        "1", "2", "3", "4", "5", "6", "7",
    };

    private Harmony? _harmony;

    private ConfigEntry<bool> _grantAllCrests = null!;
    private ConfigEntry<KeyboardShortcut> _testHotkey = null!;

    private bool _choosing;
    private List<ToolCrest> _choices = new List<ToolCrest>();

    private void Awake()
    {
        Log = Logger;

        Probe.Enabled = Config.Bind("Probe", "Enabled", true,
            "Log dialogue FSMs of early scenes and the collectable/tool name lists.").Value;

        _grantAllCrests = Config.Bind("General", "GrantAllCrests", false,
            "Выдавать все 7 знаков независимо от сейва");
        _testHotkey = Config.Bind("General", "TestHotkey", new KeyboardShortcut(KeyCode.F9),
            "Клавиша тестового выбора знака");

        CrestPickerService.GrantAllCrests = _grantAllCrests.Value;

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();

        Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
    }

    private void Update()
    {
        if (!IsInGame())
        {
            return;
        }

        if (_choosing)
        {
            HandleChoiceInput();
            return;
        }

        if (_testHotkey.Value.IsDown())
        {
            BeginSelection();
        }
    }

    /// <summary>True only while a loaded scene is being played.</summary>
    private static bool IsInGame()
    {
        GameManager? manager = GameManager.instance;
        return manager != null && manager.GameState == GameState.PLAYING;
    }

    /// <summary>Snapshots the selectable crests and logs the numbered list.</summary>
    private void BeginSelection()
    {
        _choices = CrestPickerService.GetSelectableCrests();
        _choosing = true;

        if (_grantAllCrests.Value && _choices.Count != 7)
        {
            Log.LogWarning(
                $"[CREST] GrantAllCrests is on but {_choices.Count} base crests were found (expected 7).");
        }

        Log.LogInfo($"[CREST] Selectable crests ({_choices.Count}):");
        for (int i = 0; i < _choices.Count; i++)
        {
            ToolCrest crest = _choices[i];
            string key = i < ChoiceKeys.Length ? ChoiceKeys[i] : "?";
            Log.LogInfo(
                $"[CREST]   {key}) {crest.name} unlocked={crest.IsUnlocked} equipped={crest.IsEquipped}");
        }

        Log.LogInfo("[CREST] Press 1..7 to apply a crest, Esc to cancel.");
    }

    /// <summary>Reads the number keys and Escape while a selection is open.</summary>
    private void HandleChoiceInput()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            _choosing = false;
            Log.LogInfo("[CREST] Selection cancelled.");
            return;
        }

        for (int i = 0; i < 7; i++)
        {
            if (!Input.GetKeyDown(KeyCode.Alpha1 + i))
            {
                continue;
            }

            if (i >= _choices.Count)
            {
                Log.LogWarning($"[CREST] No crest at position {i + 1}.");
                return;
            }

            _choosing = false;
            CrestPickerService.ApplyCrest(_choices[i]);
            return;
        }
    }

    private void OnDestroy()
    {
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
