using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

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

    private void Awake()
    {
        Log = Logger;

        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();

        Logger.LogInfo($"{PluginName} {PluginVersion} loaded.");
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
