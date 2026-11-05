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

    private Harmony? _harmony;

    private ConfigEntry<bool> _grantAllCrests = null!;
    private ConfigEntry<KeyboardShortcut> _testHotkey = null!;

    private bool _loggedAlive;

    private void Awake()
    {
        Log = Logger;

        Probe.Enabled = Config.Bind("Probe", "Enabled", true,
            "Log dialogue FSMs of early scenes and the collectable/tool name lists.").Value;

        _grantAllCrests = Config.Bind("General", "GrantAllCrests", true,
            "Выдавать все 7 знаков независимо от сейва");
        _testHotkey = Config.Bind("General", "TestHotkey", new KeyboardShortcut(KeyCode.F9),
            "Клавиша тестового выбора знака");

        CrestPickerService.GrantAllCrests = _grantAllCrests.Value;
        CrestSelectMenu.RunSelfCheck();

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
